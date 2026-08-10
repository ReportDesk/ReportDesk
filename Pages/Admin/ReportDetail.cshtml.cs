using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReportSystem.Helpers;
using ReportSystem.Models;
using ReportSystem.Services;
using System.ComponentModel.DataAnnotations;

namespace ReportSystem.Pages.Admin
{
    public class ReportDetailModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly IR2StorageService _r2;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly PhotoValidationOptions _photoValidationOptions;

        public ReportDetailModel(
            AppDbContext db,
            IR2StorageService r2,
            IDataProtectionProvider dataProtectionProvider,
            IOptions<PhotoValidationOptions> photoValidationOptions)
        {
            _db = db;
            _r2 = r2;
            _dataProtectionProvider = dataProtectionProvider;
            _photoValidationOptions = photoValidationOptions.Value;
        }

        [BindProperty(SupportsGet = true)]
        public int reportId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string reportType { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public string detailId { get; set; } = string.Empty;

        public string ReportType { get; private set; } = string.Empty;
        public string ReportedBy { get; private set; } = string.Empty;
        public string ReportAbout { get; private set; } = string.Empty;
        public string MachineName { get; private set; } = string.Empty;
        public string ReportDateText { get; private set; } = string.Empty;
        public string ReportArea { get; private set; } = string.Empty;
        public string ReportDescription { get; private set; } = string.Empty;
        public string ReportDescription2 { get; private set; } = string.Empty;
        public string ReportStatus { get; private set; } = string.Empty;
        public List<AttachmentDto> ReportPhotos { get; private set; } = new();
        public List<AttachmentDto> BeforePhotos { get; private set; } = new();
        public List<AttachmentDto> AfterPhotos { get; private set; } = new();

        public int? ActiveFollowUpId { get; private set; }
        public bool HasFollowUp => ActiveFollowUpId.HasValue;
        public bool IsReportClosed { get; private set; }
        public string FollowUpDateText { get; private set; } = string.Empty;
        public List<AttachmentDto> FollowUpAttachments { get; private set; } = new();
        public List<AttachmentDto> FollowUpPhotos { get; private set; } = new();

        public List<AttachmentDto> ReportAttachments { get; private set; } = new();

        [BindProperty]
        [Required(ErrorMessage = "Name is required.")]
        public string FollowUpName { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Follow up description is required.")]
        public string FollowUpDescription { get; set; } = string.Empty;

        [BindProperty]
        public string? MaterialsUsed { get; set; }

        [BindProperty]
        public List<string> FollowUpPhotoTempKeys { get; set; } = new();

        [BindProperty]
        public List<string> FollowUpPhotoUploadOwnerTokens { get; set; } = new();

        [BindProperty]
        public string? DraftStatus { get; set; }

        [TempData]
        public string? SuccessMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }


        private static readonly string[] AllowedStatuses = { "open", "on progress", "close" };

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            var redirect = RedirectIfNotAuthorizedForReportType();
            if (redirect is not null)
                return redirect;

            return await LoadReportAsync(cancellationToken) ? Page() : NotFound();
        }

        public async Task<IActionResult> OnPostSaveFollowUpAsync(CancellationToken ct)
        {
            var redirect = RedirectIfNotAuthorizedForReportType();
            if (redirect is not null)
                return redirect;

            if (!await LoadReportAsync(ct))
                return NotFound();

            if (IsReportClosed)
            {
                ModelState.AddModelError(string.Empty, "Closed reports cannot be edited.");
                ErrorMessage = "Closed reports cannot be edited.";
                return Page();
            }

            ModelState.Remove("SelectedStatus");

            if (string.Equals(ReportType, "Machine", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(MaterialsUsed))
            {
                ModelState.AddModelError(nameof(MaterialsUsed), "Materials used is required for machine reports.");
            }

            var normalizedStatus = !string.IsNullOrWhiteSpace(DraftStatus)
                ? "on progress"
                : "close";

            if (!AllowedStatuses.Contains(normalizedStatus))
            {
                ModelState.AddModelError(string.Empty, "Invalid status value.");
            }

            if (FollowUpPhotoTempKeys.Count != FollowUpPhotoUploadOwnerTokens.Count)
                ModelState.AddModelError(nameof(FollowUpPhotoTempKeys), "Uploaded photo tokens do not match the selected photos.");

            for (var i = 0; i < FollowUpPhotoTempKeys.Count; i++)
            {
                var tempKey = FollowUpPhotoTempKeys[i];
                var ownerToken = FollowUpPhotoUploadOwnerTokens[i];

                if (string.IsNullOrWhiteSpace(tempKey) || string.IsNullOrWhiteSpace(ownerToken))
                {
                    ModelState.AddModelError(nameof(FollowUpPhotoTempKeys), "Photo upload token is required.");
                    continue;
                }

                if (!tempKey.StartsWith("temp/", StringComparison.Ordinal) ||
                    !tempKey.StartsWith($"temp/{ownerToken}/", StringComparison.Ordinal))
                {
                    ModelState.AddModelError(nameof(FollowUpPhotoTempKeys), "One or more uploaded photos are invalid.");
                    continue;
                }

                var isAllowedUnverifiedPhoto = _photoValidationOptions.AllowUnverifiedUploads &&
                                               await _r2.IsUnverifiedReportPhotoAsync(tempKey, ct);
                if (!isAllowedUnverifiedPhoto && !await _r2.IsValidatedReportPhotoAsync(tempKey, ct))
                    ModelState.AddModelError(nameof(FollowUpPhotoTempKeys), "One or more follow-up photos were not validated as recent. Please upload the photo again.");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var now = DateTime.Now;
            var userId = GetCurrentUserId();

            try
            {
                var report = await _db.MasterReports
                    .FirstOrDefaultAsync(r => r.ReportId == reportId, ct);

                if (report == null)
                    return NotFound();

                report.Status = normalizedStatus;
                report.UpdatedDate = now;
                report.UpdatedBy = userId;

                MasterFollowUp followUp;
                if (ActiveFollowUpId.HasValue)
                {
                    followUp = await _db.MasterFollowUps
                        .FirstOrDefaultAsync(f => f.FollowUpId == ActiveFollowUpId.Value && f.ReportId == reportId, ct);

                    if (followUp == null)
                        return NotFound();

                    followUp.FollowUpDate = now;
                    followUp.ResponsiblePerson = FollowUpName.Trim();
                    followUp.Description = FollowUpDescription.Trim();
                    followUp.MaterialsUsed = string.Equals(ReportType, "Machine", StringComparison.OrdinalIgnoreCase)
                        ? MaterialsUsed!.Trim()
                        : string.Empty;
                    followUp.UpdatedBy = userId;
                }
                else
                {
                    followUp = new MasterFollowUp
                    {
                        ReportId = reportId,
                        FollowUpDate = now,
                        ResponsiblePerson = FollowUpName.Trim(),
                        Description = FollowUpDescription.Trim(),
                        MaterialsUsed = string.Equals(ReportType, "Machine", StringComparison.OrdinalIgnoreCase)
                                            ? MaterialsUsed!.Trim()
                                            : string.Empty,
                        UpdatedBy = userId
                    };
                    _db.MasterFollowUps.Add(followUp);
                }

                await _db.SaveChangesAsync(ct);

                if (FollowUpPhotoTempKeys.Count > 0)
                {
                    for (var i = 0; i < FollowUpPhotoTempKeys.Count; i++)
                    {
                        var finalKey = await _r2.FinalizeTempAsync(FollowUpPhotoTempKeys[i], reportId.ToString(), ct);

                        _db.Attachments.Add(new Attachment
                        {
                            ReportId = null,
                            FollowUpId = followUp.FollowUpId,
                            FileName = $"followup_{i + 1}_{Path.GetFileName(finalKey)}",
                            FilePath = _r2.GetPublicUrl(finalKey),
                            FileSize = 0,
                            FileType = "image/webp",
                            UploadDate = now,
                            UploadedBy = HttpContext.Session.GetString("UserId") ?? "system"
                        });
                    }

                    await _db.SaveChangesAsync(ct);
                }

                SuccessMessage = "Follow-up saved successfully.";
                var redirectUrl = Url.Page("/Admin/ReportList", new { reportType = report.ReportType })
                                  ?? $"/Admin/ReportList?reportType={Uri.EscapeDataString(report.ReportType)}";

                if (IsAjaxRequest())
                {
                    return new JsonResult(new
                    {
                        success = true,
                        message = SuccessMessage,
                        redirectUrl
                    });
                }

                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                ErrorMessage = "An error occurred while saving the follow up data";
                await LoadReportAsync(ct);
                return Page();
            }
        }

        public async Task<IActionResult> OnPostDeleteFollowUpAttachmentAsync(int attachmentId, CancellationToken ct)
        {
            var redirect = RedirectIfNotAuthorizedForReportType();
            if (redirect is not null)
                return redirect;

            if (!await LoadReportAsync(ct))
                return NotFound();

            if (IsReportClosed)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return new JsonResult(new { success = false, message = "Closed reports cannot be edited." });
            }

            if (!ActiveFollowUpId.HasValue)
                return NotFound();

            var attachment = await _db.Attachments
                .FirstOrDefaultAsync(a =>
                    a.AttachmentId == attachmentId &&
                    a.FollowUpId == ActiveFollowUpId.Value &&
                    (a.ReportId == null || a.ReportId == 0),
                    ct);

            if (attachment == null)
                return NotFound();

            var storageKey = TryGetR2KeyFromPublicUrl(attachment.FilePath);
            _db.Attachments.Remove(attachment);
            await _db.SaveChangesAsync(ct);

            if (!string.IsNullOrWhiteSpace(storageKey))
            {
                try
                {
                    await _r2.DeleteAsync(storageKey, ct);
                }
                catch
                {
                }
            }

            return new JsonResult(new { success = true });
        }

        // ---- Helpers

        private IActionResult? RedirectIfNotAuthorizedForReportType()
        {
            var userId = HttpContext.Session.GetString("UserId");
            var role = HttpContext.Session.GetString("Role") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(userId) || !int.TryParse(userId, out _))
                return RedirectToPage("/Login");

            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                return null;

            var allowedReportType = GetReportTypeForRole(role);
            if (allowedReportType != null && IsMatchingReportType(allowedReportType, reportType))
                return null;

            return RedirectToPage("/AccessDenied");
        }

        private async Task<bool> LoadReportAsync(CancellationToken ct)
        {
            if (!TryResolveReportId())
                return false;

            var report = await _db.MasterReports
                .AsNoTracking()
                .Include(x => x.Machine)
                .FirstOrDefaultAsync(x => x.ReportId== reportId, ct);

            if (report == null)
                return false;

            if (!IsMatchingReportType(report.ReportType, reportType))
                return false;

            var activeFollowUp = await _db.MasterFollowUps
                .AsNoTracking()
                .Where(x => x.ReportId == reportId)
                .OrderByDescending(x => x.FollowUpDate)
                .ThenByDescending(x => x.FollowUpId)
                .FirstOrDefaultAsync(ct);
            var activeFollowUpId = activeFollowUp?.FollowUpId;

            var attachments = await _db.Attachments
                .AsNoTracking()
                .Where(a =>
                    (a.ReportId == reportId && (a.FollowUpId == null || a.FollowUpId == 0)) ||
                    (activeFollowUpId.HasValue && a.FollowUpId == activeFollowUpId.Value && (a.ReportId == null || a.ReportId == 0)))
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
            IsReportClosed = IsClosedStatus(report.Status);

            var reportAttachments = attachments
                .Where(a => a.ReportId == reportId && (a.FollowUpId == null || a.FollowUpId == 0))
                .OrderBy(a => a.AttachmentId)
                .Select(a => new AttachmentDto
                {
                    AttachmentId = a.AttachmentId,
                    FileName = a.FileName,
                    FilePath = a.FilePath,
                    FileType = a.FileType ?? string.Empty
                }).ToList();

            ReportPhotos = reportAttachments
                .Where(IsImageAttachment)
                .ToList();

            await PopulatePhotoValidationMetadataAsync(ReportPhotos, ct);

            BeforePhotos = ReportPhotos
                .Where(a => a.FileName.StartsWith("before_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            AfterPhotos = ReportPhotos
                .Where(a => a.FileName.StartsWith("after_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            ReportAttachments = reportAttachments
                .Where(a => !IsImageAttachment(a))
                .ToList();

            ActiveFollowUpId = activeFollowUp?.FollowUpId;
            FollowUpDateText = activeFollowUp?.FollowUpDate.ToString("dd-MM-yyyy HH:mm") ?? string.Empty;

            if (activeFollowUp != null && (!HttpContext.Request.HasFormContentType || IsReportClosed))
            {
                FollowUpName = activeFollowUp.ResponsiblePerson;
                FollowUpDescription = activeFollowUp.Description;
                MaterialsUsed = activeFollowUp.MaterialsUsed ?? string.Empty;
            }

            FollowUpAttachments = activeFollowUp == null
                ? new List<AttachmentDto>()
                : attachments
                    .Where(a => a.FollowUpId == activeFollowUp.FollowUpId && (a.ReportId == null || a.ReportId == 0))
                    .OrderBy(a => a.AttachmentId)
                    .Select(a => new AttachmentDto
                    {
                        AttachmentId = a.AttachmentId,
                        FileName = a.FileName,
                        FilePath = a.FilePath,
                        FileType = a.FileType ?? string.Empty
                    }).ToList();

            FollowUpPhotos = FollowUpAttachments
                .Where(IsImageAttachment)
                .ToList();

            await PopulatePhotoValidationMetadataAsync(FollowUpPhotos, ct);

            return true;
        }

        private bool TryResolveReportId()
        {
            if (!ReportIdProtector.TryUnprotect(_dataProtectionProvider, detailId, out var resolvedReportId))
                return false;

            reportId = resolvedReportId;
            return true;
        }

        private static bool IsMatchingReportType(string actualReportType, string routeReportType)
        {
            return string.Equals(
                NormalizeReportType(actualReportType),
                NormalizeReportType(routeReportType),
                StringComparison.Ordinal);
        }

        private static string? GetReportTypeForRole(string role)
        {
            return role.Trim().ToLowerInvariant() switch
            {
                "machine" => "Machine",
                "nearmiss" => "NearMiss",
                "suggestion" => "Suggestion",
                "kaizen" => "Kaizen",
                _ => null
            };
        }

        private static string NormalizeReportType(string reportType)
        {
            return reportType.Trim().ToLowerInvariant() switch
            {
                "machine" => "machine",
                "nearmiss" => "nearmiss",
                "suggestion" => "suggestion",
                "kaizen" => "kaizen",
                _ => string.Empty
            };
        }

        private static bool IsClosedStatus(string? status)
        {
            return string.Equals(status, "close", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "closed", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsImageAttachment(AttachmentDto attachment)
        {
            return attachment.FileType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                   attachment.FilePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                   attachment.FilePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   attachment.FilePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   attachment.FilePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        private async Task PopulatePhotoValidationMetadataAsync(
            List<AttachmentDto> photos,
            CancellationToken ct)
        {
            var validationTasks = photos.Select(async photo =>
            {
                var storageKey = TryGetR2KeyFromPublicUrl(photo.FilePath);
                if (string.IsNullOrWhiteSpace(storageKey))
                    return;

                var metadata = await _r2.GetPhotoValidationAsync(storageKey, ct);
                if (metadata is null)
                    return;

                photo.PhotoValidationLabel = metadata.Method switch
                {
                    "exif" when metadata.CapturedAt.HasValue =>
                        $"Verified by EXIF · captured {metadata.CapturedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}",
                    "in-app-camera" => "Verified by in-app camera",
                    "unverified" => "Unverified development upload",
                    _ => "Verification status unavailable"
                };
            });

            await Task.WhenAll(validationTasks);
        }

        private bool IsAjaxRequest()
        {
            return string.Equals(
                Request.Headers["X-Requested-With"].ToString(),
                "XMLHttpRequest",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string? TryGetR2KeyFromPublicUrl(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            var reportsIndex = filePath.IndexOf("/reports/", StringComparison.OrdinalIgnoreCase);
            if (reportsIndex >= 0)
                return filePath[(reportsIndex + 1)..];

            return filePath.StartsWith("reports/", StringComparison.OrdinalIgnoreCase)
                ? filePath
                : null;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }
    }

    public class AttachmentDto
    {
        public int AttachmentId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string PhotoValidationLabel { get; set; } = "Verification status unavailable";
    }
}
