using System.Text;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

public sealed class ManagementUploadHardeningTests
{
    [Fact]
    public void SaveFollowUp_DeclaresRequestFormAndRateLimits()
    {
        // Given
        var action = typeof(ManagementController).GetMethod(nameof(ManagementController.SaveFollowUp))!;

        // When
        var requestLimit = action.GetCustomAttribute<RequestSizeLimitAttribute>();
        var formLimit = action.GetCustomAttribute<RequestFormLimitsAttribute>();
        var rateLimit = action.GetCustomAttribute<EnableRateLimitingAttribute>();

        // Then
        Assert.Equal(50L * 1024 * 1024, ((IRequestSizeLimitMetadata?)requestLimit)?.MaxRequestBodySize);
        Assert.Equal(50L * 1024 * 1024, formLimit?.MultipartBodyLengthLimit);
        Assert.Equal("managementUpload", rateLimit?.PolicyName);
    }

    [Fact]
    public async Task SaveFollowUp_RejectsMalformedImageBeforeStorage()
    {
        // Given
        await using var database = await TestDatabase.CreateAsync();
        database.Context.MasterReports.Add(CreateReport(51));
        await database.Context.SaveChangesAsync();
        await using var fileStream = new MemoryStream(Encoding.UTF8.GetBytes("not an image"));
        var controller = CreateController(database.Context, database.RootPath);
        var form = CreateForm(51,
            new FormFile(fileStream, 0, fileStream.Length, "attachments", "fake.png")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/png"
            });

        // When
        var result = await controller.SaveFollowUp(form);

        // Then
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await database.Context.Attachments.ToListAsync());
        Assert.False(Directory.Exists(Path.Combine(database.RootPath, "uploads")));
    }

    [Fact]
    public async Task SaveFollowUp_RejectsTooManyAttachmentsWithoutStorageWrites()
    {
        // Given
        await using var database = await TestDatabase.CreateAsync();
        database.Context.MasterReports.Add(CreateReport(52));
        await database.Context.SaveChangesAsync();
        var storage = new TrackingStorageService();
        var files = Enumerable.Range(0, 6).Select(index => CreateFile($"{index}.png")).ToArray();
        var controller = CreateController(database.Context, database.RootPath, storage: storage);

        // When
        var result = await controller.SaveFollowUp(CreateForm(52, files));

        // Then
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(storage.UploadedKeys);
        Assert.Empty(await database.Context.MasterFollowUps.ToListAsync());
    }

    [Fact]
    public async Task SaveFollowUp_ProcessingFailureReturnsSafeErrorWithoutStorageWrites()
    {
        // Given
        await using var database = await TestDatabase.CreateAsync();
        database.Context.MasterReports.Add(CreateReport(53));
        await database.Context.SaveChangesAsync();
        var storage = new TrackingStorageService();
        var controller = CreateController(
            database.Context,
            database.RootPath,
            processor: new RejectingImageProcessor(),
            storage: storage);

        // When
        var result = await controller.SaveFollowUp(CreateForm(53, CreateFile("valid.png")));

        // Then
        var rejected = Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.DoesNotContain("decoder detail", rejected.Value?.ToString(), StringComparison.Ordinal);
        Assert.Empty(storage.UploadedKeys);
    }

    [Fact]
    public async Task SaveFollowUp_FinalizeFailureRollsBackDatabaseAndDeletesStorageObjects()
    {
        // Given
        await using var database = await TestDatabase.CreateAsync();
        database.Context.MasterReports.Add(CreateReport(54));
        await database.Context.SaveChangesAsync();
        var storage = new TrackingStorageService { FailFinalizeCall = 2 };
        var controller = CreateController(database.Context, database.RootPath, storage: storage);

        // When
        var result = await controller.SaveFollowUp(CreateForm(54, CreateFile("one.png"), CreateFile("two.png")));

        // Then
        Assert.IsType<ObjectResult>(result);
        database.Context.ChangeTracker.Clear();
        Assert.Empty(await database.Context.MasterFollowUps.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.Attachments.AsNoTracking().ToListAsync());
        Assert.Contains("reports/54/followups/final-1.webp", storage.DeletedKeys);
        Assert.All(storage.UploadedKeys, key => Assert.Contains(key, storage.DeletedKeys));
    }

    [Fact]
    public async Task SaveFollowUp_DatabaseFailureRollsBackAndDeletesFinalizedObject()
    {
        // Given
        await using var database = await TestDatabase.CreateAsync();
        database.Context.MasterReports.Add(CreateReport(55));
        await database.Context.SaveChangesAsync();
        await database.Context.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_attachment_insert BEFORE INSERT ON ATTACHMENT BEGIN SELECT RAISE(FAIL, 'db detail'); END;");
        var storage = new TrackingStorageService();
        var controller = CreateController(database.Context, database.RootPath, storage: storage);

        // When
        var result = await controller.SaveFollowUp(CreateForm(55, CreateFile("one.png")));

        // Then
        var failure = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, failure.StatusCode);
        Assert.DoesNotContain("db detail", failure.Value?.ToString(), StringComparison.Ordinal);
        database.Context.ChangeTracker.Clear();
        Assert.Empty(await database.Context.MasterFollowUps.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.Attachments.AsNoTracking().ToListAsync());
        Assert.Contains("reports/55/followups/final-1.webp", storage.DeletedKeys);
    }

    [Fact]
    public async Task SaveFollowUp_RecordsFailedCompensationForLaterCleanupRetry()
    {
        // Given
        await using var database = await TestDatabase.CreateAsync();
        database.Context.MasterReports.Add(CreateReport(59));
        await database.Context.SaveChangesAsync();
        await database.Context.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_attachment_insert BEFORE INSERT ON ATTACHMENT BEGIN SELECT RAISE(FAIL, 'db detail'); END;");
        var storage = new TrackingStorageService();
        storage.FailNextDeleteFor("reports/59/followups/final-1.webp");
        var controller = CreateController(database.Context, database.RootPath, storage: storage);

        // When
        var result = await controller.SaveFollowUp(CreateForm(59, CreateFile("one.png")));

        // Then
        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<ObjectResult>(result).StatusCode);
        database.Context.ChangeTracker.Clear();
        var pending = Assert.Single(await database.Context.PendingR2ObjectDeletions.ToListAsync());
        Assert.Equal("reports/59/followups/final-1.webp", pending.ObjectKey);

        var cleanup = new OrphanedObjectCleanupService(
            new TestAppDbContextScopeFactory(database.Context),
            NullLogger<OrphanedObjectCleanupService>.Instance);
        await cleanup.RetryPendingDeletesAsync(storage);

        database.Context.ChangeTracker.Clear();
        Assert.Empty(await database.Context.PendingR2ObjectDeletions.ToListAsync());
        Assert.Equal(2, storage.DeleteAttemptsFor("reports/59/followups/final-1.webp"));
    }

    [Fact]
    public async Task SaveFollowUp_RepeatedIdempotencyKeyDoesNotDuplicateRowsOrObjects()
    {
        // Given
        await using var database = await TestDatabase.CreateAsync();
        database.Context.MasterReports.Add(CreateReport(56));
        await database.Context.SaveChangesAsync();
        var storage = new TrackingStorageService();
        var controller = CreateController(database.Context, database.RootPath, storage: storage);
        controller.Request.Headers["Idempotency-Key"] = "followup-56-once";
        var form = CreateForm(56, CreateFile("one.png"));

        // When
        var first = await controller.SaveFollowUp(form);
        var retry = await controller.SaveFollowUp(form);

        // Then
        Assert.IsType<OkObjectResult>(first);
        Assert.IsType<OkObjectResult>(retry);
        Assert.Single(await database.Context.MasterFollowUps.ToListAsync());
        Assert.Single(await database.Context.Attachments.ToListAsync());
        Assert.Single(storage.UploadedKeys);
    }

    [Fact]
    public async Task SaveFollowUp_RetryFromFreshControllerDoesNotDuplicateFollowUp()
    {
        // Given
        await using var database = await TestDatabase.CreateAsync();
        database.Context.MasterReports.Add(CreateReport(57));
        await database.Context.SaveChangesAsync();
        var firstController = CreateController(database.Context, database.RootPath);
        firstController.Request.Headers["Idempotency-Key"] = "followup-57-restart";

        // When
        var first = await firstController.SaveFollowUp(CreateForm(57));
        await using var freshContext = database.CreateContext();
        var freshController = CreateController(freshContext, database.RootPath);
        freshController.Request.Headers["Idempotency-Key"] = "followup-57-restart";
        var retry = await freshController.SaveFollowUp(CreateForm(57));

        // Then
        Assert.IsType<OkObjectResult>(first);
        Assert.IsType<OkObjectResult>(retry);
        Assert.Single(await freshContext.MasterFollowUps.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SaveFollowUp_ConcurrentControllersWithSameKeyCreateOneFollowUp()
    {
        // Given
        await using var database = await TestDatabase.CreateAsync();
        database.Context.MasterReports.Add(CreateReport(58));
        await database.Context.SaveChangesAsync();
        await using var secondContext = database.CreateContext();
        var firstController = CreateController(database.Context, database.RootPath);
        var secondController = CreateController(secondContext, database.RootPath);
        firstController.Request.Headers["Idempotency-Key"] = "followup-58-concurrent";
        secondController.Request.Headers["Idempotency-Key"] = "followup-58-concurrent";

        // When
        var results = await Task.WhenAll(
            firstController.SaveFollowUp(CreateForm(58)),
            secondController.SaveFollowUp(CreateForm(58)));

        // Then
        Assert.All(results, result => Assert.IsType<OkObjectResult>(result));
        await using var verificationContext = database.CreateContext();
        Assert.Single(await verificationContext.MasterFollowUps.AsNoTracking().ToListAsync());
    }

    private static IFormFile CreateFile(string name)
    {
        var stream = new MemoryStream([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        return new FormFile(stream, 0, stream.Length, "attachments", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
    }

    private static ManagementController.SaveFollowUpForm CreateForm(int reportId, params IFormFile[] files) => new()
    {
        reportId = reportId,
        status = "On Progress",
        responsiblePerson = "Owner",
        description = "Follow-up",
        materialsUsed = "Material",
        attachments = files.ToList()
    };

    private static MasterReport CreateReport(int reportId) => new()
    {
        ReportId = reportId,
        ReportType = "Machine",
        ReportedBy = "Reporter",
        Area = "Area A",
        Description = "Initial description",
        Status = "Open",
        SubmissionDate = new DateTime(2026, 1, 2)
    };

    private static ManagementController CreateController(
        AppDbContext context,
        string rootPath,
        IImageProcessingService? processor = null,
        TrackingStorageService? storage = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<Microsoft.AspNetCore.Http.Features.ISessionFeature>(
            new TestSessionFeature { Session = new TestSession() });
        httpContext.Session.SetString("UserId", "7");

        var controller = new ManagementController(
            context,
            processor is null ? new FileValidationService() : new AcceptingFileValidationService(),
            processor ?? new PassthroughImageProcessingService(),
            storage ?? new TrackingStorageService(),
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

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _connectionString;
        public AppDbContext Context { get; }
        public string RootPath { get; }

        private TestDatabase(string connectionString, AppDbContext context, string rootPath)
        {
            _connectionString = connectionString;
            Context = context;
            RootPath = rootPath;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "ReportSystem.UploadHardeningTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var databasePath = Path.Combine(rootPath, "test.db");
            var connectionString = $"Data Source={databasePath};Default Timeout=5;Pooling=False";
            var context = CreateContext(connectionString);
            await context.Database.EnsureCreatedAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            return new TestDatabase(connectionString, context, rootPath);
        }

        public AppDbContext CreateContext() => CreateContext(_connectionString);

        private static AppDbContext CreateContext(string connectionString) => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .Options);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment(string rootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = typeof(ManagementUploadHardeningTests).Assembly.GetName().Name!;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = rootPath;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = rootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestSessionFeature : Microsoft.AspNetCore.Http.Features.ISessionFeature
    {
        public ISession Session { get; set; } = new TestSession();
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
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
