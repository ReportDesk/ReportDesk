using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using ReportSystem.Models;
using ReportSystem.Services;
using Xunit;

namespace ReportSystem.ContractTests;

public sealed class AttachmentMigrationReconciliationServiceTests
{
    [Fact]
    public async Task ReconcileAsync_ReturnsResumableAuditBatchesWithoutStorageCalls()
    {
        // Given
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var root = Path.Combine(Path.GetTempPath(), "ReportSystem.MigrationTests", Guid.NewGuid().ToString("N"));
        var uploadRoot = Path.Combine(root, "uploads", "followups");
        Directory.CreateDirectory(uploadRoot);
        await File.WriteAllBytesAsync(Path.Combine(uploadRoot, "exists.webp"), [1, 2, 3]);
        context.Attachments.AddRange(
            CreateAttachment(1, "/uploads/followups/exists.webp"),
            CreateAttachment(2, "/uploads/followups/missing.webp"));
        await context.SaveChangesAsync();
        var service = new AttachmentMigrationReconciliationService(
            context,
            new MigrationTestEnvironment(root),
            Options.Create(new AttachmentDeliveryCompatibilityOptions
            {
                EnableMigrationReconciliation = true
            }));

        try
        {
            // When
            var first = await service.ReconcileAsync(0, 1, CancellationToken.None);
            var second = await service.ReconcileAsync(first.NextCursor!.Value, 1, CancellationToken.None);

            // Then
            Assert.True(first.HasMore);
            Assert.Equal(1, first.NextCursor);
            Assert.True(Assert.Single(first.Items).LocalFileExists);
            Assert.False(second.HasMore);
            Assert.False(Assert.Single(second.Items).LocalFileExists);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReconcileAsync_DryRunRecordsStableChecksumWithoutWritingMigrationState()
    {
        await using var fixture = await MigrationFixture.CreateAsync();
        fixture.Context.Attachments.Add(CreateAttachment(1, "/uploads/followups/exists.webp"));
        await fixture.Context.SaveChangesAsync();
        await File.WriteAllBytesAsync(Path.Combine(fixture.UploadRoot, "exists.webp"), [1, 2, 3]);

        var batch = await fixture.Service.ReconcileAsync(
            new AttachmentMigrationRequest(0, 100, DryRun: true),
            CancellationToken.None);

        var item = Assert.Single(batch.Items);
        Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81", item.SourceChecksum);
        Assert.Equal(AttachmentMigrationOutcome.Ready, item.Outcome);
        Assert.Empty(fixture.ObjectStore.Copies);
        Assert.False(File.Exists(fixture.CheckpointPath));
        Assert.False(File.Exists(fixture.ManifestPath));
    }

    [Fact]
    public async Task ReconcileAsync_ExecuteCopiesVerifiesAuditsAndResumesWithoutDeletingSource()
    {
        await using var fixture = await MigrationFixture.CreateAsync(executionEnabled: true);
        var sourcePath = Path.Combine(fixture.UploadRoot, "exists.webp");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
        fixture.Context.Attachments.Add(CreateAttachment(1, "/uploads/followups/exists.webp"));
        await fixture.Context.SaveChangesAsync();

        var first = await fixture.Service.ReconcileAsync(
            new AttachmentMigrationRequest(0, 1, DryRun: false), CancellationToken.None);
        var replay = await fixture.Service.ReconcileAsync(
            new AttachmentMigrationRequest(0, 1, DryRun: false), CancellationToken.None);

        var completed = Assert.Single(first.Items);
        Assert.Equal(AttachmentMigrationOutcome.Completed, completed.Outcome);
        Assert.Equal("attachment-migration/1/exists.webp", completed.DestinationKey);
        Assert.Equal("r2://attachment-migration/1/exists.webp",
            (await fixture.Context.Attachments.SingleAsync()).FilePath);
        Assert.True(File.Exists(sourcePath));
        Assert.Single(fixture.ObjectStore.Copies);
        Assert.Empty(replay.Items);
        Assert.Contains("\"lastAttachmentId\":1", await File.ReadAllTextAsync(fixture.CheckpointPath));
        var manifest = await File.ReadAllTextAsync(fixture.ManifestPath);
        Assert.Contains("\"phase\":\"prepared\"", manifest);
        Assert.Contains("\"phase\":\"completed\"", manifest);
        Assert.Contains("\"outcome\":\"completed\"", manifest);
        Assert.Contains(completed.SourceChecksum!, manifest);

        var evidenceDirectory = Environment.GetEnvironmentVariable("REPORTDESK_MIGRATION_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            Directory.CreateDirectory(evidenceDirectory);
            File.Copy(fixture.CheckpointPath, Path.Combine(evidenceDirectory, "checkpoint.json"), overwrite: true);
            File.Copy(fixture.ManifestPath, Path.Combine(evidenceDirectory, "manifest.jsonl"), overwrite: true);
            await File.WriteAllTextAsync(
                Path.Combine(evidenceDirectory, "observable.txt"),
                $"databasePath={(await fixture.Context.Attachments.SingleAsync()).FilePath}{Environment.NewLine}" +
                $"sourceRetained={File.Exists(sourcePath)}{Environment.NewLine}" +
                $"copyCalls={fixture.ObjectStore.Copies.Count}{Environment.NewLine}" +
                $"replayItems={replay.Items.Count}{Environment.NewLine}");
        }
    }

    [Fact]
    public async Task ReconcileAsync_ChecksumMismatchLeavesDatabaseAndCheckpointUnchanged()
    {
        await using var fixture = await MigrationFixture.CreateAsync(executionEnabled: true);
        await File.WriteAllBytesAsync(Path.Combine(fixture.UploadRoot, "exists.webp"), [1, 2, 3]);
        fixture.Context.Attachments.Add(CreateAttachment(1, "/uploads/followups/exists.webp"));
        await fixture.Context.SaveChangesAsync();
        fixture.ObjectStore.ReturnedChecksum = new string('0', 64);

        var batch = await fixture.Service.ReconcileAsync(
            new AttachmentMigrationRequest(0, 1, DryRun: false), CancellationToken.None);

        Assert.Equal(AttachmentMigrationOutcome.ChecksumMismatch, Assert.Single(batch.Items).Outcome);
        Assert.Equal("/uploads/followups/exists.webp",
            (await fixture.Context.Attachments.SingleAsync()).FilePath);
        Assert.False(File.Exists(fixture.CheckpointPath));
        Assert.Contains("\"phase\":\"failed\"", await File.ReadAllTextAsync(fixture.ManifestPath));

        var evidenceDirectory = Environment.GetEnvironmentVariable("REPORTDESK_MIGRATION_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            Directory.CreateDirectory(evidenceDirectory);
            File.Copy(fixture.ManifestPath, Path.Combine(evidenceDirectory, "mismatch-manifest.jsonl"), overwrite: true);
            await File.WriteAllTextAsync(
                Path.Combine(evidenceDirectory, "rollback-observable.txt"),
                $"databasePath={(await fixture.Context.Attachments.SingleAsync()).FilePath}{Environment.NewLine}" +
                $"checkpointExists={File.Exists(fixture.CheckpointPath)}{Environment.NewLine}" +
                $"sourceRetained={File.Exists(Path.Combine(fixture.UploadRoot, "exists.webp"))}{Environment.NewLine}");
        }
    }

    [Fact]
    public async Task ReconcileAsync_DryRunFlagsDuplicateChecksumsForOperatorReview()
    {
        await using var fixture = await MigrationFixture.CreateAsync();
        await File.WriteAllBytesAsync(Path.Combine(fixture.UploadRoot, "one.webp"), [7, 8]);
        await File.WriteAllBytesAsync(Path.Combine(fixture.UploadRoot, "two.webp"), [7, 8]);
        fixture.Context.Attachments.AddRange(
            CreateAttachment(1, "/uploads/followups/one.webp"),
            CreateAttachment(2, "/uploads/followups/two.webp"));
        await fixture.Context.SaveChangesAsync();

        var batch = await fixture.Service.ReconcileAsync(
            new AttachmentMigrationRequest(0, 100, DryRun: true), CancellationToken.None);

        Assert.All(batch.Items, item => Assert.Equal(AttachmentMigrationOutcome.DuplicateChecksum, item.Outcome));
        Assert.Empty(fixture.ObjectStore.Copies);
    }

    [Fact]
    public async Task ReconcileAsync_ExecuteIsBlockedByIndependentDefaultOffSwitch()
    {
        await using var fixture = await MigrationFixture.CreateAsync(executionEnabled: false);

        var batch = await fixture.Service.ReconcileAsync(
            new AttachmentMigrationRequest(0, 100, DryRun: false), CancellationToken.None);

        Assert.Equal(AttachmentMigrationOutcome.ExecutionDisabled, Assert.Single(batch.Items).Outcome);
        Assert.Empty(fixture.ObjectStore.Copies);
        Assert.False(File.Exists(fixture.CheckpointPath));
        Assert.False(File.Exists(fixture.ManifestPath));
    }

    [Fact]
    public async Task ReconcileAsync_ExecuteUsesConfiguredLocalObjectStoreAndRetainsHistoricalSource()
    {
        // Given
        await using var fixture = await MigrationFixture.CreateAsync(executionEnabled: true);
        var sourcePath = Path.Combine(fixture.UploadRoot, "exists.webp");
        var objectStoreRoot = Path.Combine(fixture.Root, "migration-object-store");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
        fixture.Context.Attachments.Add(CreateAttachment(1, "/uploads/followups/exists.webp"));
        await fixture.Context.SaveChangesAsync();
        var options = new AttachmentDeliveryCompatibilityOptions
        {
            EnableMigrationReconciliation = true,
            EnableMigrationExecution = true,
            MigrationCheckpointPath = fixture.CheckpointPath,
            MigrationAuditManifestPath = fixture.ManifestPath,
            MigrationLocalObjectStorePath = objectStoreRoot
        };
        var service = new AttachmentMigrationReconciliationService(
            fixture.Context,
            new MigrationTestEnvironment(Path.Combine(fixture.Root, "wwwroot")),
            Options.Create(options),
            AttachmentMigrationObjectStoreFactory.Create(options));

        // When
        var batch = await service.ReconcileAsync(
            new AttachmentMigrationRequest(0, 1, DryRun: false), CancellationToken.None);

        // Then
        Assert.Equal(AttachmentMigrationOutcome.Completed, Assert.Single(batch.Items).Outcome);
        Assert.True(File.Exists(Path.Combine(objectStoreRoot, "attachment-migration", "1", "exists.webp")));
        Assert.True(File.Exists(sourcePath));
        Assert.Equal("r2://attachment-migration/1/exists.webp",
            (await fixture.Context.Attachments.SingleAsync()).FilePath);

        var evidenceDirectory = Environment.GetEnvironmentVariable("REPORTDESK_MIGRATION_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            Directory.CreateDirectory(evidenceDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(evidenceDirectory, "local-adapter-observable.txt"),
                $"adapter={nameof(LocalAttachmentMigrationObjectStore)}{Environment.NewLine}" +
                $"destinationExists={File.Exists(Path.Combine(objectStoreRoot, "attachment-migration", "1", "exists.webp"))}{Environment.NewLine}" +
                $"sourceRetained={File.Exists(sourcePath)}{Environment.NewLine}" +
                $"databasePath={(await fixture.Context.Attachments.SingleAsync()).FilePath}{Environment.NewLine}");
        }
    }

    [Fact]
    public void Create_DefaultsToDisabledAdapterUntilExecutionAndLocalRootAreConfigured()
    {
        // Given
        var options = new AttachmentDeliveryCompatibilityOptions
        {
            MigrationLocalObjectStorePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        };

        // When
        var objectStore = AttachmentMigrationObjectStoreFactory.Create(options);

        // Then
        Assert.IsType<DisabledAttachmentMigrationObjectStore>(objectStore);

        var evidenceDirectory = Environment.GetEnvironmentVariable("REPORTDESK_MIGRATION_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            Directory.CreateDirectory(evidenceDirectory);
            File.WriteAllText(
                Path.Combine(evidenceDirectory, "default-adapter-observable.txt"),
                $"adapter={objectStore.GetType().Name}{Environment.NewLine}" +
                "executionEnabled=False" + Environment.NewLine +
                "liveR2Adapter=False" + Environment.NewLine);
        }
    }

    [Fact]
    public void Program_RegistersLocalMigrationAdapterOnlyForExplicitExecutionConfiguration()
    {
        // Given
        var objectStoreRoot = Path.Combine(Path.GetTempPath(), "ReportSystem.MigrationProgramTests", Guid.NewGuid().ToString("N"));
        using var factory = new MigrationExecutionApplicationFactory(objectStoreRoot);

        // When
        using var scope = factory.Services.CreateScope();
        var objectStore = scope.ServiceProvider.GetRequiredService<IAttachmentMigrationObjectStore>();

        // Then
        Assert.IsType<LocalAttachmentMigrationObjectStore>(objectStore);

        var evidenceDirectory = Environment.GetEnvironmentVariable("REPORTDESK_MIGRATION_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            Directory.CreateDirectory(evidenceDirectory);
            File.WriteAllText(
                Path.Combine(evidenceDirectory, "program-wiring-observable.txt"),
                $"adapter={objectStore.GetType().Name}{Environment.NewLine}" +
                "executionEnabled=True" + Environment.NewLine +
                "liveR2Adapter=False" + Environment.NewLine);
        }
    }

    private static Attachment CreateAttachment(int id, string path) => new()
    {
        AttachmentId = id,
        FileName = Path.GetFileName(path),
        FilePath = path,
        FileType = "image/webp",
        UploadDate = new DateTime(2026, 1, 1),
        UploadedBy = "fixture"
    };

    private sealed class MigrationTestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = nameof(AttachmentMigrationReconciliationServiceTests);
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class MigrationExecutionApplicationFactory(string objectStoreRoot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("AttachmentDelivery:EnableMigrationExecution", "true");
            builder.UseSetting("AttachmentDelivery:MigrationLocalObjectStorePath", objectStoreRoot);
        }
    }

    private sealed class RecordingMigrationObjectStore : IAttachmentMigrationObjectStore
    {
        public List<(string SourcePath, string DestinationKey, string ExpectedChecksum)> Copies { get; } = [];
        public string? ReturnedChecksum { get; set; }

        public Task<string> CopyFromLocalAsync(
            string sourcePath,
            string destinationKey,
            string expectedSha256,
            CancellationToken ct)
        {
            Copies.Add((sourcePath, destinationKey, expectedSha256));
            return Task.FromResult(ReturnedChecksum ?? expectedSha256);
        }
    }

    private sealed class MigrationFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public string Root { get; }
        public string UploadRoot { get; }
        public string CheckpointPath { get; }
        public string ManifestPath { get; }
        public AppDbContext Context { get; }
        public RecordingMigrationObjectStore ObjectStore { get; }
        public AttachmentMigrationReconciliationService Service { get; }

        private MigrationFixture(
            string root,
            SqliteConnection connection,
            AppDbContext context,
            RecordingMigrationObjectStore objectStore,
            AttachmentMigrationReconciliationService service)
        {
            Root = root;
            UploadRoot = Path.Combine(root, "wwwroot", "uploads", "followups");
            CheckpointPath = Path.Combine(root, "state", "checkpoint.json");
            ManifestPath = Path.Combine(root, "state", "manifest.jsonl");
            _connection = connection;
            Context = context;
            ObjectStore = objectStore;
            Service = service;
        }

        public static async Task<MigrationFixture> CreateAsync(bool executionEnabled = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "ReportSystem.MigrationWorkflowTests", Guid.NewGuid().ToString("N"));
            var webRoot = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(Path.Combine(webRoot, "uploads", "followups"));
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var objectStore = new RecordingMigrationObjectStore();
            var options = new AttachmentDeliveryCompatibilityOptions
            {
                EnableMigrationReconciliation = true,
                EnableMigrationExecution = executionEnabled,
                MigrationCheckpointPath = Path.Combine(root, "state", "checkpoint.json"),
                MigrationAuditManifestPath = Path.Combine(root, "state", "manifest.jsonl")
            };
            var service = new AttachmentMigrationReconciliationService(
                context, new MigrationTestEnvironment(webRoot), Options.Create(options), objectStore);
            return new MigrationFixture(root, connection, context, objectStore, service);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
