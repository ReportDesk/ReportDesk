using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ReportSystem.Models;
using ReportSystem.Services;

namespace ReportSystem.ContractTests;

public sealed class ManagementSecurityApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            _connection.Open();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
            services.RemoveAll<IR2StorageService>();
            services.RemoveAll<IFileValidationService>();
            services.RemoveAll<IImageProcessingService>();
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IR2StorageService, RecordingStorageService>();
            services.AddSingleton<IFileValidationService, AcceptingFileValidationService>();
            services.AddSingleton<IImageProcessingService, PassthroughImageProcessingService>();
            services.AddSingleton<IEmailService, NoOpEmailService>();
            services.AddSingleton<IStartupFilter, AntiforgeryTokenEndpointStartupFilter>();
        });
    }

    public async Task SeedAsync()
    {
        _ = Server;
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        if (await db.MasterUsers.AnyAsync()) return;
        db.MasterUsers.AddRange(User(1, "admin", "Admin"), User(2, "viewer", "Suggestion"));
        db.MasterReports.Add(new MasterReport { ReportId = 73, ReportType = "Machine", ReportedBy = "Fixture Reporter", Area = "Fixture Area", Description = "Fixture report", Status = "Open", SubmissionDate = new DateTime(2026, 1, 2) });
        await db.SaveChangesAsync();
    }

    public TestBrowser CreateBrowser() => new(CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") }));

    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) _connection.Dispose(); }

    private static MasterUser User(int id, string username, string role) => new() { UserId = id, Username = username, Fullname = username, Email = $"{username}@test.invalid", Password = BCrypt.Net.BCrypt.HashPassword("Test-password-1!"), Role = role, IsActive = 1, CreatedDate = new DateTime(2026, 1, 1) };

    private sealed class AntiforgeryTokenEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Path != "/__test/antiforgery") { await continuation(); return; }
                var auth = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                if (auth.Succeeded && auth.Principal is not null) context.User = auth.Principal;
                var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(tokens.RequestToken!);
            });
            next(app);
        };
    }
}

public sealed class TestBrowser(HttpClient client) : IDisposable
{
    public async Task<string> GetAntiforgeryTokenAsync() { using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "/__test/antiforgery")); response.EnsureSuccessStatusCode(); return await response.Content.ReadAsStringAsync(); }
    public async Task LoginAsync(string username)
    {
        var token = await GetAntiforgeryTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Login") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = username, ["Password"] = "Test-password-1!", ["__RequestVerificationToken"] = token }) };
        using var response = await SendAsync(request);
        if (response.StatusCode != HttpStatusCode.Redirect) throw new InvalidOperationException($"Login returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        return await client.SendAsync(request);
    }
    public void Dispose() => client.Dispose();
}

public sealed class RecordingStorageService : IR2StorageService
{
    public List<string> DeletedKeys { get; } = [];
    public Task<string> UploadTempAsync(Stream stream, string fileName, string userId, CancellationToken ct = default) => Task.FromResult($"temp/{userId}/fixture.webp");
    public Task<string> FinalizeTempAsync(string tempKey, string reportId, CancellationToken ct = default) => Task.FromResult($"reports/{reportId}/fixture.webp");
    public Task<string> FinalizeFollowUpTempAsync(string tempKey, string reportId, CancellationToken ct = default) => Task.FromResult($"reports/{reportId}/followups/fixture.webp");
    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
    public Task DeleteAsync(string key, CancellationToken ct = default) { DeletedKeys.Add(key); return Task.CompletedTask; }
    public Task DeleteOldTempFilesAsync(TimeSpan olderThan, CancellationToken ct = default) => Task.CompletedTask;
    public string GetPublicUrl(string key) => $"https://storage.test/{key}";
}
public sealed class AcceptingFileValidationService : IFileValidationService { public Task<FileValidationResult> ValidateAsync(IFormFile file, CancellationToken ct = default) => Task.FromResult(new FileValidationResult(true)); }
public sealed class PassthroughImageProcessingService : IImageProcessingService
{
    public async Task<ProcessedImageResult> ProcessAsync(Stream input, CancellationToken ct = default) { var output = new MemoryStream(); await input.CopyToAsync(output, ct); output.Position = 0; return new ProcessedImageResult(output, 1, 1); }
}
public sealed class NoOpEmailService : IEmailService
{
    public Task SendForgotPasswordAsync(string toEmail, string resetLink) => Task.CompletedTask;
    public Task SendReportNotificationAsync(string reportType, string toEmail, ReportNotificationData reportData, string? reportDetailUrl = null) => Task.CompletedTask;
}
