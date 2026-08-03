using Microsoft.Extensions.Options;

namespace ReportSystem.Services;

/// <summary>
/// Classifies attachment paths during the transition from public local files to
/// private object storage. This is deliberately a path resolver, not a file
/// server: the authenticated attachment delivery action owns authorization and
/// opening private objects.
/// </summary>
public interface IAttachmentDeliveryCompatibilityService
{
    AttachmentDeliveryResolution Resolve(int attachmentId, string? filePath);
}

public sealed class AttachmentDeliveryCompatibilityOptions
{
    public const string SectionName = "AttachmentDelivery";

    /// <summary>Whether already-persisted /uploads/followups paths remain valid.</summary>
    public bool EnableLegacyFollowUpReads { get; set; } = true;

    /// <summary>Writes a structured warning whenever an old local path is resolved.</summary>
    public bool LogLegacyFollowUpUsage { get; set; } = true;

    /// <summary>Enables the local reconciliation endpoint. Execution remains separately disabled.</summary>
    public bool EnableMigrationReconciliation { get; set; }

    /// <summary>Second opt-in required before reconciliation may update rows.</summary>
    public bool EnableMigrationExecution { get; set; }

    /// <summary>Durable cursor file for an explicitly enabled execution; must be outside wwwroot.</summary>
    public string? MigrationCheckpointPath { get; set; }

    /// <summary>Append-only JSON-lines audit file for execution; must be outside wwwroot.</summary>
    public string? MigrationAuditManifestPath { get; set; }

    /// <summary>
    /// Local migration-only object-store root. It is required when execution is
    /// enabled and is never used by the normal R2 upload service.
    /// </summary>
    public string? MigrationLocalObjectStorePath { get; set; }

    /// <summary>Deterministic destination prefix used by the migration-only object-store adapter.</summary>
    public string MigrationObjectKeyPrefix { get; set; } = "attachment-migration";

    /// <summary>
    /// The authenticated application route used to deliver a private attachment.
    /// It must contain {attachmentId}; storage keys are never placed in this URL.
    /// </summary>
    public string PrivateDeliveryPathTemplate { get; set; } = "/api/attachments/{attachmentId}";

    /// <summary>Informational sunset date for the legacy local-file transition.</summary>
    public string? LegacyFollowUpSunset { get; set; }
}

public enum AttachmentStorageLocation
{
    Invalid,
    LegacyLocalFollowUp,
    PrivateObject,
    ExistingPublic
}

public sealed record AttachmentDeliveryResolution(
    AttachmentStorageLocation Location,
    string? DeliveryPath,
    string? PrivateObjectKey = null)
{
    public bool IsDeliverable => !string.IsNullOrWhiteSpace(DeliveryPath);
}

public sealed class AttachmentDeliveryCompatibilityService : IAttachmentDeliveryCompatibilityService
{
    private const string LegacyPrefix = "/uploads/followups/";
    private static readonly string[] PrivatePrefixes = ["r2://", "private://"];
    private readonly AttachmentDeliveryCompatibilityOptions _options;
    private readonly ILogger<AttachmentDeliveryCompatibilityService> _logger;

    public AttachmentDeliveryCompatibilityService(
        IOptions<AttachmentDeliveryCompatibilityOptions> options,
        ILogger<AttachmentDeliveryCompatibilityService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public AttachmentDeliveryResolution Resolve(int attachmentId, string? filePath)
    {
        if (attachmentId <= 0 || string.IsNullOrWhiteSpace(filePath))
            return new AttachmentDeliveryResolution(AttachmentStorageLocation.Invalid, null);

        if (filePath.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!_options.EnableLegacyFollowUpReads || !IsSafeLegacyPath(filePath))
                return new AttachmentDeliveryResolution(AttachmentStorageLocation.Invalid, null);

            if (_options.LogLegacyFollowUpUsage)
            {
                _logger.LogWarning(
                    "Legacy follow-up attachment path resolved. AttachmentId: {AttachmentId}; Path: {AttachmentPath}; Sunset: {Sunset}",
                    attachmentId,
                    filePath,
                    _options.LegacyFollowUpSunset ?? "not scheduled");
            }

            // Preserve the exact historical URL: Static File Middleware continues
            // to return a normal 404 if the old file is absent.
            return new AttachmentDeliveryResolution(AttachmentStorageLocation.LegacyLocalFollowUp, filePath);
        }

        var privatePrefix = PrivatePrefixes.FirstOrDefault(candidate =>
            filePath.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
        if (privatePrefix is not null)
        {
            var privateKey = GetPrivateObjectKey(filePath, privatePrefix);
            if (privateKey is null)
                return new AttachmentDeliveryResolution(AttachmentStorageLocation.Invalid, null);

            var deliveryPath = BuildPrivateDeliveryPath(attachmentId);
            return deliveryPath is null
                ? new AttachmentDeliveryResolution(AttachmentStorageLocation.Invalid, null)
                : new AttachmentDeliveryResolution(AttachmentStorageLocation.PrivateObject, deliveryPath, privateKey);
        }

        // Preserve unrelated pre-existing public attachment URLs. New follow-up
        // records must use r2:// or private:// and therefore cannot reach this branch.
        return Uri.TryCreate(filePath, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? new AttachmentDeliveryResolution(AttachmentStorageLocation.ExistingPublic, filePath)
            : new AttachmentDeliveryResolution(AttachmentStorageLocation.Invalid, null);
    }

    private static bool IsSafeLegacyPath(string filePath)
    {
        var relativePath = filePath[LegacyPrefix.Length..];
        return relativePath.Length > 0 &&
               !relativePath.Contains("..", StringComparison.Ordinal) &&
               !relativePath.Contains('\\') &&
               !relativePath.Contains('?') &&
               !relativePath.Contains('#') &&
               !relativePath.StartsWith("/", StringComparison.Ordinal);
    }

    private static string? GetPrivateObjectKey(string filePath, string prefix)
    {
        var key = filePath[prefix.Length..].TrimStart('/');
        return IsSafePrivateObjectKey(key) ? key : null;
    }

    private static bool IsSafePrivateObjectKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 1024 || key.Contains("..", StringComparison.Ordinal))
            return false;

        return key.All(character =>
            char.IsLetterOrDigit(character) || character is '/' or '.' or '-' or '_');
    }

    private string? BuildPrivateDeliveryPath(int attachmentId)
    {
        var template = _options.PrivateDeliveryPathTemplate;
        if (string.IsNullOrWhiteSpace(template) ||
            !template.StartsWith("/", StringComparison.Ordinal) ||
            !template.Contains("{attachmentId}", StringComparison.Ordinal))
        {
            _logger.LogError("AttachmentDelivery:PrivateDeliveryPathTemplate is invalid.");
            return null;
        }

        return template.Replace("{attachmentId}", attachmentId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
