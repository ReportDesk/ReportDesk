using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ReportSystem.Services;

public interface IAttachmentMigrationReconciliationService
{
    bool Enabled { get; }
    Task<AttachmentMigrationBatch> ReconcileAsync(int afterAttachmentId, int batchSize, CancellationToken ct);
    Task<AttachmentMigrationBatch> ReconcileAsync(AttachmentMigrationRequest request, CancellationToken ct);
}

/// <summary>
/// A deliberately migration-only copy seam. The default implementation is disabled;
/// operators must replace it with an explicitly configured local migration adapter.
/// It is intentionally not implemented by <see cref="IR2StorageService"/>.
/// </summary>
public interface IAttachmentMigrationObjectStore
{
    Task<string> CopyFromLocalAsync(
        string sourcePath,
        string destinationKey,
        string expectedSha256,
        CancellationToken ct);
}

public sealed class DisabledAttachmentMigrationObjectStore : IAttachmentMigrationObjectStore
{
    public Task<string> CopyFromLocalAsync(
        string sourcePath,
        string destinationKey,
        string expectedSha256,
        CancellationToken ct) =>
        throw new InvalidOperationException(
            "Attachment migration execution has no object-store adapter. Dry-run remains available.");
}

public sealed record AttachmentMigrationRequest(int AfterAttachmentId, int BatchSize, bool DryRun = true);

[JsonConverter(typeof(AttachmentMigrationOutcomeJsonConverter))]
public enum AttachmentMigrationOutcome
{
    Ready,
    NotFound,
    Rejected,
    DuplicateChecksum,
    Completed,
    ChecksumMismatch,
    ExecutionDisabled
}

public sealed class AttachmentMigrationOutcomeJsonConverter()
    : JsonStringEnumConverter<AttachmentMigrationOutcome>(JsonNamingPolicy.KebabCaseLower);

public sealed record AttachmentMigrationItem(
    int AttachmentId,
    string LegacyPath,
    bool LocalFileExists,
    long? SourceSize = null,
    string? SourceChecksum = null,
    string? DestinationKey = null,
    AttachmentMigrationOutcome Outcome = AttachmentMigrationOutcome.Ready,
    string? Detail = null);

public sealed record AttachmentMigrationBatch(
    int AfterAttachmentId,
    int? NextCursor,
    bool HasMore,
    IReadOnlyList<AttachmentMigrationItem> Items,
    bool DryRun = true);

public sealed class AttachmentMigrationReconciliationService : IAttachmentMigrationReconciliationService
{
    private const string LegacyPrefix = "/uploads/followups/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly AttachmentDeliveryCompatibilityOptions _options;
    private readonly IAttachmentMigrationObjectStore _objectStore;

    public AttachmentMigrationReconciliationService(
        AppDbContext context,
        IWebHostEnvironment environment,
        IOptions<AttachmentDeliveryCompatibilityOptions> options,
        IAttachmentMigrationObjectStore? objectStore = null)
    {
        _context = context;
        _environment = environment;
        _options = options.Value;
        _objectStore = objectStore ?? new DisabledAttachmentMigrationObjectStore();
    }

    public bool Enabled => _options.EnableMigrationReconciliation;

    public Task<AttachmentMigrationBatch> ReconcileAsync(
        int afterAttachmentId,
        int batchSize,
        CancellationToken ct) =>
        ReconcileAsync(new AttachmentMigrationRequest(afterAttachmentId, batchSize), ct);

    public async Task<AttachmentMigrationBatch> ReconcileAsync(
        AttachmentMigrationRequest request,
        CancellationToken ct)
    {
        if (!Enabled)
            return new AttachmentMigrationBatch(request.AfterAttachmentId, null, false, [], request.DryRun);

        var safeBatchSize = Math.Clamp(request.BatchSize, 1, 100);
        var afterAttachmentId = Math.Max(0, request.AfterAttachmentId);
        MigrationPaths? migrationPaths = null;
        if (!request.DryRun)
        {
            if (!_options.EnableMigrationExecution)
            {
                return new AttachmentMigrationBatch(
                    afterAttachmentId,
                    null,
                    false,
                    [new AttachmentMigrationItem(0, string.Empty, false, Outcome: AttachmentMigrationOutcome.ExecutionDisabled,
                        Detail: "AttachmentDelivery:EnableMigrationExecution must be explicitly enabled.")],
                    false);
            }

            migrationPaths = ResolveMigrationPaths();
            afterAttachmentId = Math.Max(afterAttachmentId, await ReadCheckpointAsync(migrationPaths.CheckpointPath, ct));
        }

        var attachments = await _context.Attachments
            .Where(attachment =>
                attachment.AttachmentId > afterAttachmentId &&
                attachment.FilePath.StartsWith(LegacyPrefix))
            .OrderBy(attachment => attachment.AttachmentId)
            .Take(safeBatchSize + 1)
            .ToListAsync(ct);
        var hasMore = attachments.Count > safeBatchSize;
        var page = attachments.Take(safeBatchSize).ToList();
        var webRoot = Path.GetFullPath(_environment.WebRootPath);
        var legacyRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads", "followups")) + Path.DirectorySeparatorChar;
        var items = new List<AttachmentMigrationItem>(page.Count);

        foreach (var attachment in page)
        {
            ct.ThrowIfCancellationRequested();
            var inspected = await InspectAsync(attachment.AttachmentId, attachment.FilePath, legacyRoot, ct);
            if (request.DryRun)
            {
                items.Add(inspected);
                continue;
            }

            var executed = await ExecuteAsync(attachment, inspected, migrationPaths!, ct);
            items.Add(executed);
            if (executed.Outcome is AttachmentMigrationOutcome.ChecksumMismatch)
            {
                hasMore = true;
                break;
            }

            await WriteCheckpointAsync(migrationPaths!.CheckpointPath, attachment.AttachmentId, ct);
        }


        if (request.DryRun)
        {
            var duplicates = items
                .Where(candidate => candidate.SourceChecksum is not null)
                .GroupBy(candidate => candidate.SourceChecksum!, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group.Select(candidate => candidate.AttachmentId))
                .ToHashSet();
            items = items.Select(candidate => duplicates.Contains(candidate.AttachmentId)
                ? candidate with
                {
                    Outcome = AttachmentMigrationOutcome.DuplicateChecksum,
                    Detail = "Another attachment in this deterministic batch has the same source checksum."
                }
                : candidate).ToList();
        }

        var processedCursor = items.Count > 0 &&
                              items[^1].Outcome is not AttachmentMigrationOutcome.ChecksumMismatch
            ? items[^1].AttachmentId
            : (int?)null;
        return new AttachmentMigrationBatch(
            afterAttachmentId,
            hasMore ? processedCursor : null,
            hasMore,
            items,
            request.DryRun);
    }

    private async Task<AttachmentMigrationItem> ExecuteAsync(
        Models.Attachment attachment,
        AttachmentMigrationItem item,
        MigrationPaths paths,
        CancellationToken ct)
    {
        if (item.Outcome is AttachmentMigrationOutcome.NotFound or AttachmentMigrationOutcome.Rejected or AttachmentMigrationOutcome.DuplicateChecksum)
        {
            await AppendAuditAsync(paths.ManifestPath, item, "observed", ct);
            return item;
        }

        var destinationKey = BuildDestinationKey(attachment.AttachmentId, attachment.FileName);
        var prepared = item with { DestinationKey = destinationKey };
        await AppendAuditAsync(paths.ManifestPath, prepared, "prepared", ct);
        var sourcePath = ResolveLocalPath(item.LegacyPath);
        var copiedChecksum = await _objectStore.CopyFromLocalAsync(
            sourcePath, destinationKey, item.SourceChecksum!, ct);
        if (!string.Equals(copiedChecksum, item.SourceChecksum, StringComparison.OrdinalIgnoreCase))
        {
            var mismatch = prepared with
            {
                Outcome = AttachmentMigrationOutcome.ChecksumMismatch,
                Detail = "Copied object checksum did not match the source; database and checkpoint were not changed."
            };
            await AppendAuditAsync(paths.ManifestPath, mismatch, "failed", ct);
            return mismatch;
        }

        attachment.FilePath = $"r2://{destinationKey}";
        await _context.SaveChangesAsync(ct);
        var completed = prepared with
        {
            Outcome = AttachmentMigrationOutcome.Completed,
            Detail = "Source retained; database row updated after checksum verification."
        };
        await AppendAuditAsync(paths.ManifestPath, completed, "completed", ct);
        return completed;
    }

    private static async Task<AttachmentMigrationItem> InspectAsync(
        int attachmentId,
        string legacyPath,
        string legacyRoot,
        CancellationToken ct)
    {
        var relativePath = legacyPath[LegacyPrefix.Length..];
        string localPath;
        try
        {
            localPath = Path.GetFullPath(Path.Combine(legacyRoot, relativePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new AttachmentMigrationItem(attachmentId, legacyPath, false,
                Outcome: AttachmentMigrationOutcome.Rejected, Detail: "Legacy path is invalid.");
        }

        if (!localPath.StartsWith(legacyRoot, StringComparison.OrdinalIgnoreCase))
            return new AttachmentMigrationItem(attachmentId, legacyPath, false,
                Outcome: AttachmentMigrationOutcome.Rejected, Detail: "Legacy path escapes the follow-up upload root.");
        if (!File.Exists(localPath))
            return new AttachmentMigrationItem(attachmentId, legacyPath, false,
                Outcome: AttachmentMigrationOutcome.NotFound, Detail: "Source file was not found.");

        await using var stream = File.OpenRead(localPath);
        var checksum = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        return new AttachmentMigrationItem(
            attachmentId, legacyPath, true, stream.Length, checksum,
            Outcome: AttachmentMigrationOutcome.Ready);
    }

    private string ResolveLocalPath(string legacyPath)
    {
        var webRoot = Path.GetFullPath(_environment.WebRootPath);
        var legacyRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads", "followups")) + Path.DirectorySeparatorChar;
        var localPath = Path.GetFullPath(Path.Combine(legacyRoot, legacyPath[LegacyPrefix.Length..]));
        if (!localPath.StartsWith(legacyRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Legacy attachment path escaped the upload root.");
        return localPath;
    }

    private string BuildDestinationKey(int attachmentId, string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "attachment";
        var prefix = (_options.MigrationObjectKeyPrefix ?? "attachment-migration").Trim('/');
        return $"{prefix}/{attachmentId}/{safeName}";
    }

    private MigrationPaths ResolveMigrationPaths()
    {
        if (string.IsNullOrWhiteSpace(_options.MigrationCheckpointPath) ||
            string.IsNullOrWhiteSpace(_options.MigrationAuditManifestPath))
            throw new InvalidOperationException("Migration checkpoint and audit manifest paths are required for execution.");

        var checkpoint = Path.GetFullPath(_options.MigrationCheckpointPath);
        var manifest = Path.GetFullPath(_options.MigrationAuditManifestPath);
        var webRoot = Path.GetFullPath(_environment.WebRootPath) + Path.DirectorySeparatorChar;
        if (checkpoint.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase) ||
            manifest.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Migration state must not be stored under wwwroot.");
        if (string.Equals(checkpoint, manifest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Migration checkpoint and audit manifest must use different files.");

        Directory.CreateDirectory(Path.GetDirectoryName(checkpoint)!);
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        return new MigrationPaths(checkpoint, manifest);
    }

    private static async Task<int> ReadCheckpointAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return 0;
        await using var stream = File.OpenRead(path);
        var checkpoint = await JsonSerializer.DeserializeAsync<MigrationCheckpoint>(stream, JsonOptions, ct);
        return checkpoint?.LastAttachmentId ?? 0;
    }

    private static async Task WriteCheckpointAsync(string path, int attachmentId, CancellationToken ct)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, new MigrationCheckpoint(attachmentId), JsonOptions, ct);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static async Task AppendAuditAsync(
        string path,
        AttachmentMigrationItem item,
        string phase,
        CancellationToken ct)
    {
        var entry = JsonSerializer.Serialize(new MigrationAuditEntry(
            DateTimeOffset.UtcNow, phase, item), JsonOptions);
        await File.AppendAllTextAsync(path, entry + Environment.NewLine, ct);
    }

    private sealed record MigrationPaths(string CheckpointPath, string ManifestPath);
    private sealed record MigrationCheckpoint(int LastAttachmentId);
    private sealed record MigrationAuditEntry(DateTimeOffset RecordedAt, string Phase, AttachmentMigrationItem Item);
}
