using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ReportSystem.Controllers.API;
using ReportSystem.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ReportSystem.ContractTests;

public sealed class ImageUploadResourceLimitTests
{
    [Fact]
    public async Task UploadTemp_AnonymousRequestCompletesThroughRegisteredRateLimiter()
    {
        using var factory = new ManagementSecurityApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var request = CreateTempUploadRequest();

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var token = document.RootElement.GetProperty("uploadOwnerToken").GetString();
        Assert.Matches("^[a-f0-9]{32}$", token);
        Assert.Equal(
            $"temp/{token}/fixture.webp",
            document.RootElement.GetProperty("tempKey").GetString());
    }

    [Fact]
    public async Task UploadTemp_RejectsSixthAnonymousRequestFromSameIp()
    {
        using var factory = new ManagementSecurityApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        for (var requestNumber = 0; requestNumber < 5; requestNumber++)
        {
            using var request = CreateTempUploadRequest();
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var rejectedRequest = CreateTempUploadRequest();
        using var rejectedResponse = await client.SendAsync(rejectedRequest);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
    }

    [Fact]
    public void UploadTemp_HasExplicitMultipartAndRequestBodyLimits()
    {
        var action = typeof(UploadController).GetMethod(nameof(UploadController.UploadTemp))
            ?? throw new InvalidOperationException("UploadTemp action was not found.");

        var requestLimit = action.GetCustomAttribute<RequestSizeLimitAttribute>();
        var multipartLimit = action.GetCustomAttribute<RequestFormLimitsAttribute>();

        Assert.Equal(10L * 1024 * 1024, ((IRequestSizeLimitMetadata?)requestLimit)?.MaxRequestBodySize);
        Assert.Equal(10L * 1024 * 1024, multipartLimit?.MultipartBodyLengthLimit);
    }

    [Fact]
    public async Task UploadTemp_DoesNotExposeImageProcessorExceptionDetails()
    {
        var controller = new UploadController(
            new AcceptingFileValidationService(),
            new ThrowingImageProcessingService(),
            new CountingStorageService(),
            NullLogger<UploadController>.Instance);
        var file = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", "fixture.png");

        var result = await controller.UploadTemp(file, CancellationToken.None);
        var response = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var body = JsonSerializer.Serialize(response.Value);

        Assert.Contains("could not be processed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private decoder diagnostic", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_RejectsOversizedPngFromHeaderBeforePixelDecode()
    {
        await using var fixture = new MemoryStream(CreatePngHeader(width: 5000, height: 5000));
        var processor = new ImageProcessingService(NullLogger<ImageProcessingService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessAsync(fixture));

        Assert.Contains("5000x5000", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task ProcessAsync_RejectsMalformedImageWithSafeError()
    {
        await using var fixture = new MemoryStream(
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00]);
        var processor = new ImageProcessingService(NullLogger<ImageProcessingService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessAsync(fixture));

        Assert.Equal("Image content is invalid.", exception.Message);
    }

    [Fact]
    public async Task ProcessAsync_PreservesValidPngUpload()
    {
        await using var fixture = new MemoryStream();
        using (var image = new Image<Rgba32>(2, 3))
        {
            await image.SaveAsPngAsync(fixture);
        }
        fixture.Position = 0;
        var processor = new ImageProcessingService(NullLogger<ImageProcessingService>.Instance);

        var result = await processor.ProcessAsync(fixture);
        await using var output = result.Stream;

        Assert.Equal(2, result.Width);
        Assert.Equal(3, result.Height);
        Assert.True(result.Stream.Length > 0);
        Assert.Equal(0, result.Stream.Position);
    }

    [Fact]
    public async Task UploadTemp_RealKestrelHostRejectsOversizedMultipartBody()
    {
        await using var host = await UploadKestrelHost.StartAsync();
        using var client = new HttpClient { BaseAddress = host.BaseAddress };
        using var multipart = new MultipartFormDataContent();
        multipart.Add(
            new ByteArrayContent(new byte[(10 * 1024 * 1024) + 1]),
            "file",
            "oversized.png");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/upload/temp")
        {
            Content = multipart
        };
        request.Headers.ExpectContinue = true;
        using var response = await client.SendAsync(request);

        // MVC converts Kestrel's request-body-too-large model-binding failure to 400.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.Storage.UploadCount);
    }

    private static byte[] CreatePngHeader(int width, int height)
    {
        var bytes = new byte[33];
        byte[] signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        signature.CopyTo(bytes, 0);
        bytes[11] = 13;
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        bytes[24] = 8;
        bytes[25] = 6;
        var crc = Crc32(bytes.AsSpan(12, 17));
        WriteBigEndian(bytes, 29, unchecked((int)crc));
        return bytes;
    }

    private static HttpRequestMessage CreateTempUploadRequest()
    {
        var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent([1, 2, 3]), "file", "fixture.png");
        return new HttpRequestMessage(HttpMethod.Post, "/api/upload/temp") { Content = multipart };
    }

    private static void WriteBigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
            }
        }
        return ~crc;
    }

    private sealed class UploadKestrelHost : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private UploadKestrelHost(
            WebApplication application,
            Uri baseAddress,
            CountingStorageService storage)
        {
            _application = application;
            BaseAddress = baseAddress;
            Storage = storage;
        }

        public Uri BaseAddress { get; }
        public CountingStorageService Storage { get; }

        public static async Task<UploadKestrelHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development"
            });
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services.AddControllers().AddApplicationPart(typeof(UploadController).Assembly);
            builder.Services.AddSingleton<IFileValidationService, AcceptingFileValidationService>();
            builder.Services.AddSingleton<IImageProcessingService, PassthroughImageProcessingService>();
            builder.Services.AddSingleton<CountingStorageService>();
            builder.Services.AddSingleton<IR2StorageService>(services =>
                services.GetRequiredService<CountingStorageService>());

            var app = builder.Build();
            app.MapControllers();
            await app.StartAsync();

            var addresses = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!;
            var address = new Uri(addresses.Addresses.Single());
            return new UploadKestrelHost(
                app,
                address,
                app.Services.GetRequiredService<CountingStorageService>());
        }

        public async ValueTask DisposeAsync()
        {
            await _application.StopAsync();
            await _application.DisposeAsync();
        }
    }

    private sealed class CountingStorageService : IR2StorageService
    {
        public int UploadCount { get; private set; }
        public Task<string> UploadTempAsync(Stream stream, string fileName, string userId, CancellationToken ct = default)
        {
            UploadCount++;
            return Task.FromResult("temp/test/result.webp");
        }
        public Task<string> FinalizeTempAsync(string tempKey, string reportId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> FinalizeFollowUpTempAsync(string tempKey, string reportId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteOldTempFilesAsync(TimeSpan olderThan, CancellationToken ct = default) => Task.CompletedTask;
        public string GetPublicUrl(string key) => $"https://storage.test/{key}";
    }

    private sealed class ThrowingImageProcessingService : IImageProcessingService
    {
        public Task<ProcessedImageResult> ProcessAsync(Stream input, CancellationToken ct = default) =>
            throw new InvalidOperationException("private decoder diagnostic");
    }
}
