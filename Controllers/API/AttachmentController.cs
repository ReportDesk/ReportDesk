using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Filters;
using ReportSystem.Models;
using ReportSystem.Services;

namespace ReportSystem.Controllers.API;

[ApiController]
[Route("api/attachments")]
[SessionAuthorize("Admin", "Machine", "NearMiss", "Suggestion", "Kaizen")]
public sealed class AttachmentController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IAttachmentDeliveryCompatibilityService _delivery;
    private readonly IR2StorageService _storage;
    private readonly ILogger<AttachmentController> _logger;
    private readonly IAttachmentMigrationReconciliationService _migration;

    public AttachmentController(
        AppDbContext context,
        IAttachmentDeliveryCompatibilityService delivery,
        IR2StorageService storage,
        IAttachmentMigrationReconciliationService migration,
        ILogger<AttachmentController> logger)
    {
        _context = context;
        _delivery = delivery;
        _storage = storage;
        _migration = migration;
        _logger = logger;
    }

    [HttpGet("{attachmentId:int}")]
    public async Task<IActionResult> Get(int attachmentId, CancellationToken ct)
    {
        var attachment = await _context.Attachments
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.AttachmentId == attachmentId, ct);
        if (attachment == null)
            return NotFound();

        var resolution = _delivery.Resolve(attachmentId, attachment.FilePath);
        if (!resolution.IsDeliverable)
            return NotFound();

        if (resolution.Location != AttachmentStorageLocation.PrivateObject)
            return Redirect(resolution.DeliveryPath!);

        if (!await IsSessionRoleAuthorizedForPrivateAttachmentAsync(attachment, ct))
            return StatusCode(StatusCodes.Status403Forbidden);

        try
        {
            var stream = await _storage.OpenReadAsync(resolution.PrivateObjectKey!, ct);
            return File(stream, attachment.FileType, attachment.FileName, enableRangeProcessing: false);
        }
        catch (OperationCanceledException)
        {
            return BadRequest(new { message = "The attachment request was canceled." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Private attachment delivery failed for attachment {AttachmentId}", attachmentId);
            return StatusCode(500, new { message = "The attachment could not be retrieved." });
        }
    }

    private async Task<bool> IsSessionRoleAuthorizedForPrivateAttachmentAsync(Attachment attachment, CancellationToken ct)
    {
        var role = HttpContext.Session.GetString("Role") ?? string.Empty;
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            return true;

        var reportId = attachment.ReportId.GetValueOrDefault();
        if (reportId <= 0)
        {
            var followUpId = attachment.FollowUpId.GetValueOrDefault();
            if (followUpId <= 0)
                return false;

            reportId = await _context.MasterFollowUps
                .AsNoTracking()
                .Where(followUp => followUp.FollowUpId == followUpId)
                .Select(followUp => followUp.ReportId)
                .FirstOrDefaultAsync(ct);
        }

        var reportType = await _context.MasterReports
            .AsNoTracking()
            .Where(report => report.ReportId == reportId)
            .Select(report => report.ReportType)
            .FirstOrDefaultAsync(ct);

        return IsRoleAuthorizedForReportType(role, reportType);
    }

    private static bool IsRoleAuthorizedForReportType(string role, string? reportType)
    {
        var normalizedRole = role.Trim().ToLowerInvariant();
        var normalizedReportType = reportType?.Trim().ToLowerInvariant();

        return (normalizedRole, normalizedReportType) switch
        {
            ("machine", "machine") => true,
            ("nearmiss", "nearmiss") => true,
            ("suggestion", "suggestion") => true,
            ("kaizen", "kaizen") => true,
            _ => false
        };
    }

    [HttpGet("migration/reconcile")]
    [SessionAuthorize("Admin")]
    public async Task<IActionResult> ReconcileMigration(
        int afterAttachmentId = 0,
        int batchSize = 100,
        bool dryRun = true,
        CancellationToken ct = default)
    {
        if (!_migration.Enabled)
            return NotFound();

        return Ok(await _migration.ReconcileAsync(
            new AttachmentMigrationRequest(afterAttachmentId, batchSize, dryRun), ct));
    }
}
