using Microsoft.AspNetCore.Mvc;
using ReportSystem.Services;
using ReportSystem.Models;
using System.ComponentModel.DataAnnotations;

namespace ReportSystem.Controllers.API;

[ApiController]
[Route("api/report")]
[Produces("application/json")]
public sealed class ReportController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IR2StorageService _r2;
    private readonly ILogger<ReportController> _logger;

    public ReportController(AppDbContext context, IR2StorageService r2, ILogger<ReportController> logger)
    {
        _context = context;
        _r2 = r2;
        _logger = logger;
    }

    // ── POST /api/report/submit ───────────────────────────────────────────────
    /// <summary>
    /// Final step of the report submission flow.
    ///
    /// Expects the form fields + one or more tempKeys that were already
    /// uploaded via POST /api/upload/temp.
    ///
    /// Flow:
    ///   1. Validate inputs
    ///   2. Create MasterReport row (get ReportId from DB)
    ///   3. Move each temp file → reports/{reportId}/ in R2
    ///   4. Create Attachment rows pointing to the final R2 URLs
    /// </summary>
    [HttpPost("submit")]
    public async Task<IActionResult> SubmitReport([FromForm] SubmitReportForm form, CancellationToken ct)
    {
        // ── 1. Basic field validation ─────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(form.ReportType) ||
            string.IsNullOrWhiteSpace(form.Title) ||
            string.IsNullOrWhiteSpace(form.Name) ||
            string.IsNullOrWhiteSpace(form.Area) ||
            string.IsNullOrWhiteSpace(form.Description))
        {
            return BadRequest(new { message = "Please fill all required fields before submitting." });
        }

        var validTypes = new[] { "NearMiss", "Suggestion", "Kaizen", "Machine" };
        if (!validTypes.Contains(form.ReportType))
            return BadRequest(new { message = "Invalid report type." });

        if (form.ReportType == "Machine" && (form.MachineId == null || form.MachineId == 0))
            return BadRequest(new { message = "Machine is required for Machine report." });

        // ── 2. Validate temp keys ─────────────────────────────────────────────
        // Collect whichever keys were sent (Photo / BeforePhoto / AfterPhoto slots)
        var tempKeys = new List<(string Key, string Slot)>();

        if (!string.IsNullOrWhiteSpace(form.PhotoTempKey))
        {
            if (form.ReportType == "Kaizen")
                return BadRequest(new { message = "Kaizen uses BeforePhoto and AfterPhoto, not Photo." });

            tempKeys.Add((form.PhotoTempKey, "photo"));
        }

        if (!string.IsNullOrWhiteSpace(form.BeforePhotoTempKey))
        {
            if (form.ReportType != "Kaizen")
                return BadRequest(new { message = "BeforePhoto is only valid for Kaizen reports." });

            tempKeys.Add((form.BeforePhotoTempKey, "before"));
        }

        if (!string.IsNullOrWhiteSpace(form.AfterPhotoTempKey))
        {
            if (form.ReportType != "Kaizen")
                return BadRequest(new { message = "AfterPhoto is only valid for Kaizen reports." });

            tempKeys.Add((form.AfterPhotoTempKey, "after"));
        }

        // All supplied keys must come from the temp/ prefix (never let clients
        // reference already-finalized report files)
        if (tempKeys.Any(t => !t.Key.StartsWith("temp/")))
            return BadRequest(new { message = "One or more keys are not valid temp uploads." });

        // ── 3. Create MasterReport ────────────────────────────────────────────
        var now = DateTime.UtcNow;

        var report = new MasterReport
        {
            ReportType = form.ReportType,
            ReportTitle = form.Title,
            ReportedBy = form.Name,
            Area = form.Area,
            Description = form.Description,
            Status = "open",
            SubmissionDate = now,
            UpdatedDate = now,
            UpdatedBy = 0,          // replace with int user ID from claims when auth is added
            MachineId = form.MachineId
        };

        _context.MasterReports.Add(report);
        await _context.SaveChangesAsync(ct);   // EF populates report.ReportId here

        // ── 4. Finalize each image in R2 + build Attachment rows ──────────────
        var attachments = new List<Attachment>();

        try
        {
            foreach (var (tempKey, slot) in tempKeys)
            {
                var finalKey = await _r2.FinalizeTempAsync(tempKey, report.ReportId.ToString(), ct);
                var publicUrl = _r2.GetPublicUrl(finalKey);

                attachments.Add(new Attachment
                {
                    ReportId = report.ReportId,
                    FollowUpId = null,
                    FileName = $"{slot}_{Path.GetFileName(finalKey)}",
                    FilePath = publicUrl,
                    FileSize = 0,          // size not tracked at this point; update if needed
                    FileType = "image/webp",
                    UploadDate = now,
                    UploadedBy = form.Name
                });
            }
        }
        catch (Exception ex)
        {
            // R2 finalize failed — the MasterReport row was already saved.
            // Log and return 500; the temp files will be cleaned up by TempFileCleanupService.
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

        return Ok(new
        {
            message = "Report submitted successfully.",
            reportId = report.ReportId
        });
    }

    // ── Form model ────────────────────────────────────────────────────────────

    public class SubmitReportForm
    {
        // Report type: "NearMiss" | "Suggestion" | "Kaizen" | "Machine"
        public string ReportType { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        // Machine reports only
        public int? MachineId { get; set; }

        // Temp keys returned by POST /api/upload/temp
        // NearMiss, Suggestion, Machine → PhotoTempKey
        // Kaizen                         → BeforePhotoTempKey + AfterPhotoTempKey
        public string? PhotoTempKey { get; set; }
        public string? BeforePhotoTempKey { get; set; }
        public string? AfterPhotoTempKey { get; set; }
    }
}