using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Filters;
using ReportSystem.Models;
using ReportSystem.Services;

namespace ReportSystem.Controllers.API
{
    [ApiController]
    [Route("api/management")]
    [Produces("application/json")]
    [SessionAuthorize("Admin")]
    public partial class ManagementController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ManagementController> _logger;
        private readonly IFileValidationService _fileValidator;
        private readonly IImageProcessingService _imageProcessor;
        private readonly IR2StorageService _r2;
        private readonly IAttachmentDeliveryCompatibilityService _attachmentDelivery;
        private readonly IOrphanedObjectCleanupService _orphanedObjectCleanup;

        public ManagementController(
            AppDbContext context,
            IFileValidationService fileValidator,
            IImageProcessingService imageProcessor,
            IR2StorageService r2,
            IAttachmentDeliveryCompatibilityService attachmentDelivery,
            IOrphanedObjectCleanupService orphanedObjectCleanup,
            ILogger<ManagementController> logger)
        {
            _context = context;
            _fileValidator = fileValidator;
            _imageProcessor = imageProcessor;
            _r2 = r2;
            _attachmentDelivery = attachmentDelivery;
            _orphanedObjectCleanup = orphanedObjectCleanup;
            _logger = logger;
        }

        public class UpdateStatusForm
        {
            public string status { get; set; } = "";
        }


        // -- GET /api/management/reports/{id} --
        //[HttpGet("reports/{id:int}")]

        [HttpGet("report/details")]
        public async Task<IActionResult> GetReportDetails(int id)
        {
            try
            {
                var report = await _context.MasterReports
                                    .Include(r => r.Machine)
                                    .FirstOrDefaultAsync(r => r.ReportId == id);

                if (report == null)
                    return NotFound(new { message = "Report not found." });

                var followUps = await _context.MasterFollowUps
                                        .Where(f => f.ReportId == id)
                                        .OrderBy(f => f.FollowUpDate)
                                        .ToListAsync();

                var followUpIds = followUps.Select(f => f.FollowUpId).ToList();

                var attachments = await _context.Attachments
                                        .Where(a => followUpIds.Contains(a.FollowUpId))
                                        .ToListAsync();

                var followUpDtos = followUps.Select(f => new
                {
                    followUpId = f.FollowUpId,
                    responsiblePerson = f.ResponsiblePerson,
                    followUpDateText = f.FollowUpDate.ToString("dd-MM-yyyy"),
                    description = f.Description,
                    materialsUsed = f.MaterialsUsed,
                    attachments = attachments
                        .Where(a => a.FollowUpId == f.FollowUpId)
                        .Select(a => new
                        {
                            attahcmentId = a.AttachmentId,
                            fileName = a.FileName,
                            filePatch = _attachmentDelivery.Resolve(a.AttachmentId, a.FilePath).DeliveryPath
                        }).ToList()
                }).ToList();

                return Ok(new
                {
                    reportId = report.ReportId,
                    reportType = report.ReportType,
                    reportedBy = report.ReportedBy,
                    area = report.Area,
                    machineName = report.Machine != null ? report.Machine.MachineName : null,
                    submissionDate = report.SubmissionDate.ToString("dd-MM-yyyy"),
                    description = report.Description,
                    status = report.Status,
                    followUps
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving report detail for ID {ReportId}", id);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // ── 3. POST /api/management/UpdateStatus ──────────────────────────────
        [HttpPost("UpdateStatus")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusForm form)
        {
            var allowedStatuses = new[] { "Open", "On Progress", "Close", "Done" };

            if (form == null || string.IsNullOrWhiteSpace(form.status) || !allowedStatuses.Contains(form.status))
                return BadRequest(new { message = "Invalid or missing status value." });

            try
            {
                var report = await _context.MasterReports.FirstOrDefaultAsync(r => r.ReportId == id);

                if (report == null)
                    return NotFound(new { message = "Report not found." });

                report.Status = form.status;
                report.UpdatedDate = DateTime.Now;
                report.UpdatedBy = GetCurrentUserId();

                await _context.SaveChangesAsync();

                return Ok(new { message = "Status updated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating status for report ID {ReportId}", id);
                return StatusCode(500, new { message = "An error occurred." });
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private int GetCurrentUserId()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }
    }
}
