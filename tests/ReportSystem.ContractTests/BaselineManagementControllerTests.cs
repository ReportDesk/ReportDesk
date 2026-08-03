using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReportSystem.Controllers.API;
using ReportSystem.Models;
using ReportSystem.Services;
using Xunit;

namespace ReportSystem.ContractTests;

public sealed class BaselineManagementControllerTests
{
    [Fact]
    public void LegacyRoutesAndMultipartFieldNamesRemainDiscoverable()
    {
        var controllerType = typeof(ManagementController);
        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        var getDetails = GetAction(nameof(ManagementController.GetReportDetails));
        var saveFollowUp = GetAction(nameof(ManagementController.SaveFollowUp));
        var updateStatus = GetAction(nameof(ManagementController.UpdateStatus));

        Assert.Equal("api/management", route?.Template);
        Assert.Equal("report/details", getDetails.GetCustomAttribute<HttpGetAttribute>()?.Template);
        Assert.Equal("followup/save", saveFollowUp.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal("UpdateStatus", updateStatus.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Contains(
            saveFollowUp.GetCustomAttribute<ConsumesAttribute>()?.ContentTypes ?? [],
            contentType => contentType == "multipart/form-data");

        var fields = typeof(ManagementController.SaveFollowUpForm)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            ["attachments", "description", "materialsUsed", "reportId", "responsiblePerson", "status"],
            fields.OrderBy(field => field, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetReportDetails_BaselineReturnsLegacyTopLevelShape()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.Context.MasterReports.Add(new MasterReport
        {
            ReportId = 41,
            ReportType = "Machine",
            ReportedBy = "Reporter",
            Area = "Area A",
            Description = "Initial description",
            Status = "Open",
            SubmissionDate = new DateTime(2026, 1, 2)
        });
        await database.Context.SaveChangesAsync();

        var controller = CreateController(database.Context, database.RootPath);
        var result = await controller.GetReportDetails(41);

        var ok = Assert.IsType<OkObjectResult>(result);
        var responseFields = ok.Value!.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            ["area", "description", "followUps", "machineName", "reportId", "reportType", "reportedBy", "status", "submissionDate"],
            responseFields.OrderBy(field => field, StringComparer.Ordinal));
        Assert.Equal("Open", ReadProperty<string>(ok.Value, "status"));
        Assert.Empty((System.Collections.IEnumerable)ReadProperty<object>(ok.Value, "followUps"));
    }

    [Fact]
    public async Task SaveFollowUp_BaselineAcceptsLegacyMultipartFieldsAndWritesPrivatePath()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.Context.MasterReports.Add(new MasterReport
        {
            ReportId = 42,
            ReportType = "Machine",
            ReportedBy = "Reporter",
            Area = "Area A",
            Description = "Initial description",
            Status = "Open",
            SubmissionDate = new DateTime(2026, 1, 2)
        });
        await database.Context.SaveChangesAsync();

        var controller = CreateController(database.Context, database.RootPath, userId: "7");
        await using var fileStream = new MemoryStream(Encoding.UTF8.GetBytes("legacy file contents"));
        var form = new ManagementController.SaveFollowUpForm
        {
            reportId = 42,
            status = "On Progress",
            responsiblePerson = " Legacy owner ",
            description = " Legacy follow-up ",
            materialsUsed = " Legacy material ",
            attachments =
            [
                new FormFile(fileStream, 0, fileStream.Length, "attachments", "legacy.txt")
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "text/plain"
                }
            ]
        };

        var result = await controller.SaveFollowUp(form);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Follow-up saved successfully.", ReadProperty<string>(ok.Value!, "message"));
        var attachment = await database.Context.Attachments.SingleAsync();
        Assert.StartsWith("r2://reports/42/followups/", attachment.FilePath, StringComparison.Ordinal);
        Assert.Equal("legacy.txt", attachment.FileName);
        Assert.Equal(" Legacy owner ".Trim(), (await database.Context.MasterFollowUps.SingleAsync()).ResponsiblePerson);
        Assert.False(Directory.Exists(Path.Combine(database.RootPath, "uploads")));
    }

    private static ManagementController CreateController(AppDbContext context, string rootPath, string? userId = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<Microsoft.AspNetCore.Http.Features.ISessionFeature>(
            new TestSessionFeature { Session = new TestSession(userId) });

        var controller = new ManagementController(
            context,
            new AcceptingFileValidationService(),
            new PassthroughImageProcessingService(),
            new RecordingStorageService(),
            new AttachmentDeliveryCompatibilityService(
                Options.Create(new AttachmentDeliveryCompatibilityOptions()),
                NullLogger<AttachmentDeliveryCompatibilityService>.Instance),
            new OrphanedObjectCleanupService(
                new TestAppDbContextScopeFactory(context),
                NullLogger<OrphanedObjectCleanupService>.Instance),
            NullLogger<ManagementController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static MethodInfo GetAction(string methodName) =>
        typeof(ManagementController).GetMethod(methodName) ?? throw new InvalidOperationException(methodName);

    private static T ReadProperty<T>(object value, string propertyName) =>
        (T)(value.GetType().GetProperty(propertyName)?.GetValue(value)
            ?? throw new InvalidOperationException(propertyName));

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Context { get; }
        public string RootPath { get; }

        private TestDatabase(SqliteConnection connection, AppDbContext context, string rootPath)
        {
            _connection = connection;
            Context = context;
            RootPath = rootPath;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();
            var rootPath = Path.Combine(Path.GetTempPath(), "ReportSystem.ContractTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new TestDatabase(connection, context, rootPath);
        }

        public async ValueTask DisposeAsync()
        {
            Context.Dispose();
            await _connection.DisposeAsync();
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string rootPath)
        {
            WebRootPath = rootPath;
            ContentRootPath = rootPath;
        }

        public string ApplicationName { get; set; } = typeof(BaselineManagementControllerTests).Assembly.GetName().Name!;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestSessionFeature : Microsoft.AspNetCore.Http.Features.ISessionFeature
    {
        public ISession Session { get; set; } = new TestSession();
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public TestSession(string? userId = null)
        {
            if (userId is not null)
                Set("UserId", Encoding.UTF8.GetBytes(userId));
        }

        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[]? value) => _values.TryGetValue(key, out value);
    }
}
