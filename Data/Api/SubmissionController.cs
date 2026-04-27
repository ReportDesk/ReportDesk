using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using ReportSystem.Data;
using ReportSystem.Models;
using System.Net.NetworkInformation;

namespace ReportSystem.Data.Api
{
    [ApiController]
    [Route("api/submission")]
    [Produces("application/json")]

    public class SubmissionController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SubmissionController> _logger;
        private readonly IWebHostEnvironment _env;

        public SubmissionController(AppDbContext context, ILogger<SubmissionController> logger, IWebHostEnvironment env) 
        {
            _context = context;
            _logger = logger;
            _env = env;
        }

        public class SubmitReportForm
        {
            public string reportType { get; set; } = "";
            public string title { get; set; } = "";
            public string description { get; set; } = "";
            public string area { get; set; } = "";
            public int? machineId { get; set; }
            public List<IFormFile>? attachments { get; set; }
        }

        private static readonly string[] AllowedReportTypes = {"Machine", "NearMiss", " Suggestion", "Kaizen"};

        // POST /api/submission/SubmitReport

        [HttpPost("SubmitReport")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> SubmitReport([FromForm] SubmitReportForm form)
        {
            if (form == null ||
                string.IsNullOrWhiteSpace(form.reportType) ||
                string.IsNullOrWhiteSpace(form.title) ||
                string.IsNullOrWhiteSpace(form.description) ||
                string.IsNullOrWhiteSpace(form.area))
            {
                return BadRequest(new { message = "Please fill all required fields before submitting." });
            }

            if (!AllowedReportTypes.Contains(form.reportType))
                return BadRequest(new { message = "Invalid report type." });

            if (form.reportType == "Machine" && (form.machineId == null || form.machineId == 0))
                return BadRequest(new { message = "Machine ID is required for Machine Reports." });

            var now = DateTime.Now;
            var currentId = GetCurrentUserId();

            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var report = new MasterReport
                {
                    ReportType = form.reportType.Trim(),
                    ReportTitle = form.title.Trim(),
                    ReportedBy = HttpContext.Session.GetString("Username") ?? "system",
                    Description = form.description.Trim(),
                    Area = form.area.Trim(),
                    MachineId = form.machineId ?? 0,
                    SubmissionDate = now,
                    Status = "Open",
                    UpdatedDate = now,
                    UpdatedBy = currentId
                };

                _context.MasterReports.Add(report);
                await _context.SaveChangesAsync();

                if (form.attachments != null && form.attachments.Count > 0)
                {
                    var uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "reports");
                    Directory.CreateDirectory(uploadFolder);

                    foreach(var file in form.attachments)
                    {
                        if (file.Length == 0) continue;

                        var ext = Path.GetExtension(file.FileName);
                        var safeName = $"{Guid.NewGuid()}{ext}";
                        var fullPath = Path.Combine(uploadFolder, safeName);

                        await using var stream = new FileStream(fullPath, FileMode.Create);
                        await file.CopyToAsync(stream);

                        _context.Attachments.Add(new Attachment
                        {
                            ReportId = report.ReportId,
                            FollowUpId = 0,

                            FileName = file.FileName,
                            FilePath = $"/uploads/reports/{safeName}",
                            FileSize = (int)file.Length,
                            FileType = file.ContentType,
                            UploadDate = now,
                            UploadedBy = currentId.ToString()
                        });
                    }
                    await _context.SaveChangesAsync();
                }
                await tx.CommitAsync();
                return Ok(new { message = "Report submitted successfully.", reportId = report.ReportId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error submitting report (type: {reportType})", form.reportType);

                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
            }
        }


        // -- Helpers
        private int GetCurrentUserId()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            return int.TryParse(userIdStr, out var userid) ? userid : 0;
        }
    }
}
