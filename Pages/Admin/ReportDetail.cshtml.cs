using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;
using ReportSystem.Services;
using System.ComponentModel.DataAnnotations;
using System.Threading;

namespace ReportSystem.Pages.Admin
{
    public class ReportDetailModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public ReportDetailModel(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [BindProperty(SupportsGet = true)]
        public int reportId { get; set; }

        public string ReportType { get; private set; } = string.Empty;
        public string ReportedBy { get; private set; } = string.Empty;
        public string ReportAbout { get; private set; } = string.Empty;
        public string MachineName { get; private set; } = string.Empty;
        public string ReportDateText { get; private set; } = string.Empty;
        public string ReportArea { get; private set; } = string.Empty;
        public string ReportDescription { get; private set; } = string.Empty;
        public string ReportDescription2 { get; private set; } = string.Empty;
        public string ReportStatus { get; private set; } = string.Empty;
        public string? Photo1Url { get; private set; }
        public string? Photo2Url { get; private set; }

        public List<FollowUpDto> FollowUps { get; private set; } = new();

        public List<AttachmentDto> ReportAttachments { get; private set; } = new();

        [BindProperty]
        [Required(ErrorMessage = "Status is required.")]
        public string SelectedStatus { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Name is required.")]
        public string FollowUpName { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Follow up description is required.")]
        public string FollowUpDescription { get; set; } = string.Empty;

        [BindProperty]
        public string? MaterialsUsed { get; set; }

        [BindProperty]
        public List<IFormFile>? FollowUpAttachments { get; set; }

        [BindProperty]
        public string SubmitMode { get; set; } = "save";

        [BindProperty]
        public string? DraftStatus { get; set; }

        [TempData]
        public string? SuccessMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }


        private static readonly string[] AllowedStatuses = { "Open", "On Progress", "Close" };

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            //var redirect = RedirectIfNotAdmin();
            //if (redirect is not null)
            //{
            //    return redirect;
            //}

            return await LoadReportAsync(cancellationToken) ? Page() : NotFound();
        }

        public async Task<IActionResult> OnPostSaveFollowUpAsync(CancellationToken ct)
        {

            if (ReportType == "Machine" && string.IsNullOrWhiteSpace(MaterialsUsed))
                ModelState.AddModelError(nameof(MaterialsUsed), "Materials used is required for machine reports.");

            if (!AllowedStatuses.Contains(SelectedStatus))
                ModelState.AddModelError(nameof(SelectedStatus), "Invalid status value.");

            if (!ModelState.IsValid)
                return Page();

            if (!string.IsNullOrWhiteSpace(DraftStatus))
                SelectedStatus = "On Progress";

            var now = DateTime.Now;
            var userId = GetCurrentUserId();

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                var report = await _db.MasterReports
                    .FirstOrDefaultAsync(r => r.ReportId == reportId);

                if (report == null)
                    return NotFound();

                report.Status = SelectedStatus;
                report.UpdatedDate = now;
                report.UpdatedBy = userId;

                var followUp = new MasterFollowUp
                {
                    ReportId = reportId,
                    FollowUpDate = now,
                    ResponsiblePerson = FollowUpName.Trim(),
                    Description = FollowUpDescription.Trim(),
                    MaterialsUsed = ReportType == "Machine"
                                        ? MaterialsUsed!.Trim()
                                        : string.Empty,
                    UpdatedBy = userId
                };
                _db.MasterFollowUps.Add(followUp);
                await _db.SaveChangesAsync(ct);

                if (FollowUpAttachments != null && FollowUpAttachments.Count > 0)
                {
                    var uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "followups");

                    foreach (var file in FollowUpAttachments)
                    {
                        if (file.Length == 0) continue;

                        var ext = Path.GetExtension(file.FileName);
                        var safeName = $"{Guid.NewGuid()}{ext}";
                        var fullPath = Path.Combine(uploadFolder, safeName);

                        await using var stream = new FileStream(fullPath, FileMode.Create);
                        await file.CopyToAsync(stream, ct);

                        _db.Attachments.Add(new Attachment
                        {
                            ReportId = null,
                            FollowUpId = followUp.FollowUpId,
                            FileName = file.FileName,
                            FilePath = $"/uploads/followups/{safeName}",
                            FileSize = (int)file.Length,
                            FileType = file.ContentType,
                            UploadDate = now,
                            UploadedBy = HttpContext.Session.GetString("UserId") ?? "system"
                        });
                    }

                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);

                SuccessMessage = "Follow-up saved successfully.";
                return RedirectToPage(new { reportId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                ErrorMessage = "An error occurred while saving the follow up data";
                return Page();
            }
        }

        // ---- Helpers

        private async Task<bool> LoadReportAsync(CancellationToken ct)
        {
            var report = await _db.MasterReports
                .AsNoTracking()
                .Include(x => x.Machine)
                .FirstOrDefaultAsync(x => x.ReportId== reportId, ct);

            if (report == null)
                return false;

            var followUps = await _db.MasterFollowUps
                .AsNoTracking()
                .Where(x => x.ReportId == reportId)
                .OrderBy(x => x.FollowUpDate)
                .ToListAsync(ct);

            var followUpsIds = followUps.Select(f => f.FollowUpId);

            var attachments = await _db.Attachments
                .AsNoTracking()
                .Where(a =>
                    (a.ReportId == reportId && (a.FollowUpId == null || a.FollowUpId == 0)) ||
                    (followUpsIds.Contains(a.FollowUpId) && (a.ReportId == null || a.ReportId == 0)))
                .ToListAsync(ct);

            // Map report fields
            ReportDateText = report.SubmissionDate.ToString("yyyy-MM-dd HH:mm");
            ReportType = report.ReportType;
            ReportedBy = report.ReportedBy;
            ReportAbout = report.About;
            ReportArea = report.Area;
            MachineName = report.Machine?.MachineName ?? string.Empty;
            ReportDescription = report.Description;
            ReportDescription2 = report.Description2;
            ReportStatus = report.Status;

            if (!HttpContext.Request.HasFormContentType)
                SelectedStatus = report.Status;
            else if (string.IsNullOrWhiteSpace(SelectedStatus))
                SelectedStatus = report.Status;

            // Report-level attachments
            ReportAttachments = attachments
                .Where(a => a.ReportId == reportId && (a.FollowUpId == null || a.FollowUpId == 0))
                .Select(a => new AttachmentDto
                {
                    AttachmentId = a.AttachmentId,
                    FileName = a.FileName,
                    FilePath = a.FilePath
                }).ToList();

            // Follow-ups with their attachments
            FollowUps = followUps.Select(f => new FollowUpDto
            {
                FollowUpId = f.FollowUpId,
                ResponsiblePerson = f.ResponsiblePerson,
                FollowUpDateText = f.FollowUpDate.ToString("dd-MM-yyyy HH:mm"),
                Description = f.Description,
                MaterialsUsed = f.MaterialsUsed ?? string.Empty,
                Attachments = attachments
                    .Where(a => a.FollowUpId == f.FollowUpId && (a.ReportId == null || a.ReportId == 0))
                    .Select(a => new AttachmentDto
                    {
                        AttachmentId = a.AttachmentId,
                        FileName = a.FileName,
                        FilePath = a.FilePath
                    }).ToList()
            }).ToList();

            return true;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }
    }

    public class FollowUpDto
    {
        public int? FollowUpId { get; set; }
        public string ResponsiblePerson { get; set; } = string.Empty;
        public string FollowUpDateText { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? MaterialsUsed { get; set; } = string.Empty;
        public List<AttachmentDto> Attachments { get; set; } = new();
    }

    public class AttachmentDto
    {
        public int AttachmentId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }
}
