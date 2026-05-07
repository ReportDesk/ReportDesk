using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;

namespace ReportSystem.Data.Api
{
    [ApiController]
    [Route("api/management")]
    [Produces("application/json")]
    public class ManagementController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ManagementController> _logger;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public ManagementController(AppDbContext context, ILogger<ManagementController> logger, IConfiguration config, IWebHostEnvironment env)
        {
            _context = context;
            _logger = logger;
            _config = config;
            _env = env;
        }

        public class SaveFollowUpForm
        {
            public int reportId { get; set; }
            public string status { get; set; }
            public string responsiblePerson { get; set; }
            public string description { get; set; }
            public string materialsUsed { get; set; }
            public List<IFormFile>? attachments { get; set; }
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
                            filePatch = a.FilePath
                        }).ToList()
                }).ToList();

                return Ok(new
                {
                    reportId = report.ReportId,
                    reportType = report.ReportType,
                    title = report.ReportTitle,
                    reportedBy = report.ReportedBy,
                    area = report.Area,
                    machineName = report.Machine != null ? report.Machine.MachineName : null,
                    submissionDate = report.SubmissionDate.ToString("dd-MM-yyyy"),
                    description = report.Description,
                    status = report.Status,
                    followUps = followUps
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving report detail for ID {ReportId}", id);
                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
            }
        }

        // -- POST /api/managemnet/SaveFollowUp --

        [HttpPost("followup/save")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> SaveFollowUp([FromForm] SaveFollowUpForm form)
        {
            if (form == null ||
                form.reportId == 0 ||
                string.IsNullOrWhiteSpace(form.status) ||
                string.IsNullOrWhiteSpace(form.responsiblePerson) ||
                string.IsNullOrWhiteSpace(form.description))
            {
                return BadRequest(new { message = "Please fill all required fields before submitting." });
            }

            var allowedStatuses = new[] { "Open", "On Progress", "Close", "Done" };
            if (!allowedStatuses.Contains(form.status))
                return BadRequest(new { message = "Invalid status value." });

            var now = DateTime.Now;
            var actor = User?.Identity?.Name ?? "system";

            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                // Update report status
                var report = await _context.MasterReports.FirstOrDefaultAsync(r => r.ReportId == form.reportId);

                if (report == null)
                    return NotFound(new { message = "Report not found." });

                report.Status = form.status;
                report.UpdatedDate = now;
                report.UpdatedBy = GetCurrentUserId();

                // Insert follow-up record
                var followUp = new MasterFollowUp
                {
                    ReportId = form.reportId,
                    FollowUpDate = now,
                    ResponsiblePerson = form.responsiblePerson.Trim(),
                    MaterialsUsed = form.materialsUsed?.Trim() ?? string.Empty,
                    Description = form.description.Trim(),
                    UpdatedBy = GetCurrentUserId()
                };
                _context.MasterFollowUps.Add(followUp);
                await _context.SaveChangesAsync(); // save first to get FollowUpId

                // Handle file attachments
                if (form.attachments != null && form.attachments.Count > 0)
                {
                    var uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "followups");
                    Directory.CreateDirectory(uploadFolder);

                    foreach (var file in form.attachments)
                    {
                        if (file.Length == 0) continue;

                        var ext = Path.GetExtension(file.FileName);
                        var safeName = $"{Guid.NewGuid()}{ext}";
                        var fullPath = Path.Combine(uploadFolder, safeName);

                        await using var stream = new FileStream(fullPath, FileMode.Create);
                        await file.CopyToAsync(stream);

                        _context.Attachments.Add(new Attachment
                        {
                            ReportId = form.reportId,
                            FollowUpId = followUp.FollowUpId,
                            FileName = file.FileName,
                            FilePath = $"/uploads/followups/{safeName}",
                            FileSize = (int)file.Length,
                            FileType = file.ContentType,
                            UploadDate = now,
                            UploadedBy = actor
                        });
                    }

                    await _context.SaveChangesAsync();
                }

                await tx.CommitAsync();

                return Ok(new { message = "Follow-up saved successfully." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error saving follow-up for report ID {ReportId}", form.reportId);
                return StatusCode(500, new { message = "An error occurred while saving follow-up.", error = ex.Message });
            }
        }

        // ── 3. POST /api/management/UpdateStatus ──────────────────────────────
        [HttpPost("UpdateStatus")]
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
                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
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
