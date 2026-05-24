using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Helpers;
using ReportSystem.Models;
using ReportSystem.Services;

namespace ReportSystem.Controllers.API;

[ApiController]
[Route("api/report")]
[Produces("application/json")]
public sealed class ReportController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IR2StorageService _r2;
    private readonly IEmailService _emailService;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<ReportController> _logger;

    public ReportController(
        AppDbContext context,
        IR2StorageService r2,
        IEmailService emailService,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<ReportController> logger)
    {
        _context = context;
        _r2 = r2;
        _emailService = emailService;
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    [HttpGet("machines")]
    public async Task<IActionResult> GetMachines(CancellationToken ct)
    {
        try
        {
            var machines = await _context.MasterMachines
                .AsNoTracking()
                .OrderBy(machine => machine.MachineName)
                .ThenBy(machine => machine.MachineArea)
                .Select(machine => new
                {
                    machine.MachineId,
                    machine.MachineName,
                    MachineArea = machine.MachineArea ?? string.Empty
                })
                .ToListAsync(ct);

            return Ok(machines.Select(machine => new
            {
                machine.MachineId,
                machine.MachineName,
                machine.MachineArea,
                Label = string.IsNullOrWhiteSpace(machine.MachineArea)
                    ? machine.MachineName
                    : $"{machine.MachineName} - {machine.MachineArea}"
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load machine list.");
            return StatusCode(500, new { message = "Could not load machine list. Please refresh or contact support." });
        }
    }

    [HttpPost("submit")]
    public async Task<IActionResult> SubmitReport([FromForm] SubmitReportForm form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form.ReportType) ||
            string.IsNullOrWhiteSpace(form.Name) ||
            string.IsNullOrWhiteSpace(form.Description))
        {
            return BadRequest(new { message = "Please fill all required fields before submitting." });
        }

        var validTypes = new[] { "NearMiss", "Suggestion", "Kaizen", "Machine" };
        if (!validTypes.Contains(form.ReportType))
            return BadRequest(new { message = "Invalid report type." });

        if (form.ReportType == "Machine" && (form.MachineId is null or 0))
            return BadRequest(new { message = "Machine is required for Machine report." });

        var tempUploads = new List<(string Key, string Slot, string Token)>();

        if (form.PhotoTempKeys.Count != form.PhotoUploadOwnerTokens.Count)
            return BadRequest(new { message = "Uploaded photo tokens do not match the selected photos." });

        if (form.PhotoTempKeys.Count > 0)
        {
            if (form.ReportType == "Kaizen")
                return BadRequest(new { message = "Kaizen uses BeforePhoto and AfterPhoto, not Photo." });

            for (var i = 0; i < form.PhotoTempKeys.Count; i++)
            {
                var photoTempKey = form.PhotoTempKeys[i];
                var photoToken = form.PhotoUploadOwnerTokens[i];

                if (string.IsNullOrWhiteSpace(photoTempKey) || string.IsNullOrWhiteSpace(photoToken))
                    return BadRequest(new { message = "Photo upload token is required." });

                tempUploads.Add((photoTempKey, "photo", photoToken));
            }
        }

        if (form.BeforePhotoTempKeys.Count != form.BeforePhotoUploadOwnerTokens.Count)
            return BadRequest(new { message = "Before photo tokens do not match the selected photos." });

        if (form.AfterPhotoTempKeys.Count != form.AfterPhotoUploadOwnerTokens.Count)
            return BadRequest(new { message = "After photo tokens do not match the selected photos." });

        if (form.BeforePhotoTempKeys.Count > 0)
        {
            if (form.ReportType != "Kaizen")
                return BadRequest(new { message = "BeforePhoto is only valid for Kaizen reports." });

            for (var i = 0; i < form.BeforePhotoTempKeys.Count; i++)
            {
                var beforePhotoTempKey = form.BeforePhotoTempKeys[i];
                var beforePhotoToken = form.BeforePhotoUploadOwnerTokens[i];

                if (string.IsNullOrWhiteSpace(beforePhotoTempKey) || string.IsNullOrWhiteSpace(beforePhotoToken))
                    return BadRequest(new { message = "Before photo upload token is required." });

                tempUploads.Add((beforePhotoTempKey, "before", beforePhotoToken));
            }
        }

        if (form.AfterPhotoTempKeys.Count > 0)
        {
            if (form.ReportType != "Kaizen")
                return BadRequest(new { message = "AfterPhoto is only valid for Kaizen reports." });

            for (var i = 0; i < form.AfterPhotoTempKeys.Count; i++)
            {
                var afterPhotoTempKey = form.AfterPhotoTempKeys[i];
                var afterPhotoToken = form.AfterPhotoUploadOwnerTokens[i];

                if (string.IsNullOrWhiteSpace(afterPhotoTempKey) || string.IsNullOrWhiteSpace(afterPhotoToken))
                    return BadRequest(new { message = "After photo upload token is required." });

                tempUploads.Add((afterPhotoTempKey, "after", afterPhotoToken));
            }
        }

        if (tempUploads.Any(t => !t.Key.StartsWith("temp/", StringComparison.Ordinal)))
            return BadRequest(new { message = "One or more keys are not valid temp uploads." });

        if (tempUploads.Any(t => !t.Key.StartsWith($"temp/{t.Token}/", StringComparison.Ordinal)))
            return BadRequest(new { message = "One or more temp uploads do not match the provided upload token." });

        var now = DateTime.UtcNow;

        var report = new MasterReport
        {
            ReportType = form.ReportType,
            ReportedBy = form.Name,
            About = form.About,
            Area = form.Area,
            Description = form.Description,
            Description2 = form.Description2 ?? string.Empty,
            Status = "open",
            SubmissionDate = now,
            UpdatedDate = now,
            UpdatedBy = 0,
            MachineId = form.MachineId
        };

        _context.MasterReports.Add(report);
        await _context.SaveChangesAsync(ct);

        var attachments = new List<Attachment>();

        try
        {
            foreach (var (tempKey, slot, _) in tempUploads)
            {
                var finalKey = await _r2.FinalizeTempAsync(tempKey, report.ReportId.ToString(), ct);
                var publicUrl = _r2.GetPublicUrl(finalKey);

                attachments.Add(new Attachment
                {
                    ReportId = report.ReportId,
                    FollowUpId = null,
                    FileName = $"{slot}_{attachments.Count + 1}_{Path.GetFileName(finalKey)}",
                    FilePath = publicUrl,
                    FileSize = 0,
                    FileType = "image/webp",
                    UploadDate = now,
                    UploadedBy = form.Name
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "R2 finalize failed for report {ReportId}", report.ReportId);
            return StatusCode(500, new { message = "Report saved but image upload failed. Please try again." });
        }

        if (attachments.Count > 0)
        {
            await _context.Attachments.AddRangeAsync(attachments, ct);
            await _context.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Report {ReportId} ({Type}) submitted by {Name} with {Count} attachment(s)",
            report.ReportId, report.ReportType, form.Name, attachments.Count);

        await SendReportNotificationsAsync(report, ct);

        return Ok(new
        {
            message = "Report submitted successfully.",
            reportId = report.ReportId
        });
    }

    private async Task SendReportNotificationsAsync(MasterReport report, CancellationToken ct)
    {
        try
        {
            var recipientRole = GetNotificationRole(report.ReportType);
            if (recipientRole == null)
                return;

            var recipientQuery = _context.MasterUsers
                .AsNoTracking()
                .Where(user => user.IsActive == 1 && user.Role == recipientRole);

            // To include Admin recipients later, customize the filter above, for example:
            // .Where(user => user.IsActive == 1 && (user.Role == recipientRole || user.Role == "Admin"));

            var recipientEmails = await recipientQuery
                .Select(user => user.Email)
                .Distinct()
                .ToListAsync(ct);

            var recipients = recipientEmails
                .Where(email => !string.IsNullOrWhiteSpace(email))
                .ToList();

            var protectedReportId = ReportIdProtector.Protect(_dataProtectionProvider, report.ReportId);
            var notificationData = await CreateReportNotificationDataAsync(report, protectedReportId, ct);
            var reportDetailUrl = Url.Page(
                "/Admin/ReportDetail",
                pageHandler: null,
                values: new { reportType = report.ReportType, detailId = protectedReportId },
                protocol: Request.Scheme);

            foreach (var email in recipients)
            {
                try
                {
                    await _emailService.SendReportNotificationAsync(report.ReportType, email, notificationData, reportDetailUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to send report notification for report {ReportId} to {Email}",
                        report.ReportId,
                        email);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process report notifications for report {ReportId}", report.ReportId);
        }
    }

    private static string? GetNotificationRole(string reportType)
    {
        return reportType.Trim().ToLowerInvariant() switch
        {
            "machine" => "Machine",
            "nearmiss" => "Nearmiss",
            "suggestion" => "Suggestion",
            "kaizen" => "Kaizen",
            _ => null
        };
    }

    private async Task<object> CreateReportNotificationDataAsync(
        MasterReport report,
        string protectedReportId,
        CancellationToken ct)
    {
        var machineName = string.Empty;

        if (report.MachineId.HasValue)
        {
            machineName = await _context.MasterMachines
                .AsNoTracking()
                .Where(machine => machine.MachineId == report.MachineId.Value)
                .Select(machine => machine.MachineName)
                .FirstOrDefaultAsync(ct) ?? string.Empty;
        }

        return new
        {
            ReportReference = protectedReportId,
            report.ReportType,
            ReportedBy = report.ReportedBy,
            report.SubmissionDate,
            report.Area,
            report.About,
            MachineName = machineName,
            report.Description,
            report.Description2
        };
    }

    public class SubmitReportForm
    {
        public string ReportType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public string? About { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Description2 { get; set; } = string.Empty;
        public int? MachineId { get; set; }
        public List<string> PhotoTempKeys { get; set; } = new();
        public List<string> PhotoUploadOwnerTokens { get; set; } = new();
        public List<string> BeforePhotoTempKeys { get; set; } = new();
        public List<string> BeforePhotoUploadOwnerTokens { get; set; } = new();
        public List<string> AfterPhotoTempKeys { get; set; } = new();
        public List<string> AfterPhotoUploadOwnerTokens { get; set; } = new();
    }
}
