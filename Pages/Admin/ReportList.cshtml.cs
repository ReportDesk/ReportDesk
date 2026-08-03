using ClosedXML.Excel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Helpers;
using ReportSystem.Models;
using ReportSystem.Services;

namespace ReportSystem.Pages.Admin
{
    public class ReportListModel : PageModel
    {
        private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private readonly AppDbContext _db;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly IAttachmentDeliveryCompatibilityService _attachmentDelivery;

        public ReportListModel(
            AppDbContext db,
            IDataProtectionProvider dataProtectionProvider,
            IAttachmentDeliveryCompatibilityService attachmentDelivery)
        {
            _db = db;
            _dataProtectionProvider = dataProtectionProvider;
            _attachmentDelivery = attachmentDelivery;
        }

        [BindProperty(SupportsGet = true)] public string ReportType { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)] public string FromDate { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)] public string ToDate { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)] public List<string> Status { get; set; } = ["open", "on progress", "close"];

        // Machine-only filter
        [BindProperty(SupportsGet = true)] public int? MachineId { get; set; }
        public List<SelectListItem> MachineOptions { get; set; } = [];

        public bool IsMachineType => ReportType == "Machine";
        public bool IsSuggestionType => ReportType == "Suggestion";
        public bool IsNearMissOrKaizen => ReportType == "NearMiss" || ReportType == "Kaizen";

        public string ContextColumnLabel => ReportType switch
        {
            "Machine" => "Machine",
            "Suggestion" => "About",
            _ => "Area"   // NearMiss, Kaizen, unknown
        };

        public string PageTitle => ReportType switch
        {
            "Machine" => "Machine Report List",
            "NearMiss" => "NearMiss Report List",
            "Suggestion" => "Suggestion",
            "Kaizen" => "Kaizen Report List",
            _ => "Report List"
        };

        public List<ReportRow> ReportRows { get; set; } = [];
        public string LoadError { get; set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            ReportType = CanonicalizeReportType(ReportType);

            var authorizationRedirect = GetAuthorizationRedirect();
            if (authorizationRedirect is not null)
                return authorizationRedirect;

            try
            {
                await LoadMachineOptionsAsync(ct);

                var reports = await BuildFilteredReportQuery()
                    .OrderByDescending(r => r.SubmissionDate)
                    .ToListAsync(ct);

                ReportRows = reports.Select(r => new ReportRow
                {
                    ReportId = r.ReportId,
                    ProtectedReportId = ReportIdProtector.Protect(_dataProtectionProvider, r.ReportId),
                    ReportType = r.ReportType,
                    ReportedBy = r.ReportedBy,
                    About = r.About,
                    Area = r.Area,
                    MachineName = r.Machine?.MachineName ?? string.Empty,
                    SubmissionDateText = r.SubmissionDate.ToString("dd MMM yyyy"),
                    Status = r.Status,
                    Title = r.Description ?? string.Empty,
                    ShortDescription = r.Description ?? string.Empty,
                    // ContextValue is the value shown in the dynamic "Area / Mesin / Tentang" column.
                    ContextValue = r.ReportType switch
                    {
                        "Machine" => r.Machine?.MachineName ?? string.Empty,
                        "Suggestion" => FirstNonBlank(r.About, r.Area),
                        _ => r.Area ?? string.Empty
                    }
                }).ToList();
            }
            catch (Exception ex)
            {
                LoadError = $"Error loading report: {ex.Message}";
            }

            return Page();
        }

        public async Task<IActionResult> OnGetExportAsync(CancellationToken ct)
        {
            ReportType = CanonicalizeReportType(ReportType);

            var authorizationRedirect = GetAuthorizationRedirect();
            if (authorizationRedirect is not null)
                return authorizationRedirect;

            var reports = await BuildFilteredReportQuery()
                .OrderByDescending(r => r.SubmissionDate)
                .ToListAsync(ct);

            var reportIds = reports.Select(r => r.ReportId).ToList();
            var followUps = reportIds.Count == 0
                ? new List<MasterFollowUp>()
                : await _db.MasterFollowUps
                    .AsNoTracking()
                    .Where(f => reportIds.Contains(f.ReportId))
                    .OrderByDescending(f => f.FollowUpDate)
                    .ThenByDescending(f => f.FollowUpId)
                    .ToListAsync(ct);

            var latestFollowUpsByReport = followUps
                .GroupBy(f => f.ReportId)
                .ToDictionary(g => g.Key, g => g.First());

            var followUpIds = latestFollowUpsByReport.Values
                .Where(f => f.FollowUpId.HasValue)
                .Select(f => f.FollowUpId!.Value)
                .ToList();

            var attachments = reportIds.Count == 0
                ? new List<Attachment>()
                : await _db.Attachments
                    .AsNoTracking()
                    .Where(a =>
                        (a.ReportId.HasValue &&
                         reportIds.Contains(a.ReportId.Value) &&
                         (!a.FollowUpId.HasValue || a.FollowUpId == 0)) ||
                        (a.FollowUpId.HasValue &&
                         followUpIds.Contains(a.FollowUpId.Value) &&
                         (!a.ReportId.HasValue || a.ReportId == 0)))
                    .OrderBy(a => a.AttachmentId)
                    .ToListAsync(ct);

            using var workbook = CreateExportWorkbook(reports, latestFollowUpsByReport, attachments);
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var fileName = $"{GetReportTypeFileName(ReportType)}_Report_Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(stream.ToArray(), ExcelContentType, fileName);
        }

        public bool IsStatusSelected(string status)
        {
            return (Status ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Any(s => string.Equals(s.Trim(), status, StringComparison.OrdinalIgnoreCase));
        }

        private async Task LoadMachineOptionsAsync(CancellationToken ct)
        {
            if (!IsMachineType)
                return;

            var machines = await _db.MasterMachines
                .AsNoTracking()
                .OrderBy(m => m.MachineName)
                .ToListAsync(ct);

            MachineOptions = machines.Select(m => new SelectListItem
            {
                Value = m.MachineId.ToString(),
                Text = m.MachineName
            }).ToList();
        }

        private IQueryable<MasterReport> BuildFilteredReportQuery()
        {
            var query = _db.MasterReports
                .AsNoTracking()
                .Include(r => r.Machine)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(ReportType))
                query = query.Where(r => r.ReportType == ReportType);

            if (DateTime.TryParse(FromDate, out var fromDate))
                query = query.Where(r => r.SubmissionDate >= fromDate);

            if (DateTime.TryParse(ToDate, out var toDate))
                query = query.Where(r => r.SubmissionDate <= toDate.AddDays(1).AddTicks(-1));

            var normalizedStatuses = (Status ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLower())
                .ToList();

            if (normalizedStatuses.Count > 0)
                query = query.Where(r => normalizedStatuses.Contains(r.Status.ToLower()));

            if (IsMachineType && MachineId.HasValue)
                query = query.Where(r => r.MachineId == MachineId.Value);

            return query;
        }

        private IActionResult? GetAuthorizationRedirect()
        {
            var role = HttpContext.Session.GetString("Role") ?? string.Empty;

            var roleTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Machine"] = "Machine",
                ["Nearmiss"] = "NearMiss",
                ["Suggestion"] = "Suggestion",
                ["Kaizen"] = "Kaizen"
            };

            if (roleTypeMap.TryGetValue(role, out var allowedType))
            {
                if (!string.Equals(ReportType, allowedType, StringComparison.OrdinalIgnoreCase))
                    return Redirect($"/Admin/ReportList?ReportType={allowedType}");

                return null;
            }

            return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                ? null
                : Redirect("/Login");
        }

        private XLWorkbook CreateExportWorkbook(
            List<MasterReport> reports,
            Dictionary<int, MasterFollowUp> latestFollowUpsByReport,
            List<Attachment> attachments)
        {
            var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(GetReportTypeFileName(ReportType));
            var headers = GetExportHeaders(ReportType);

            for (var i = 0; i < headers.Length; i++)
                worksheet.Cell(1, i + 1).Value = headers[i];

            var reportAttachmentsByReport = attachments
                .Where(a => a.ReportId.HasValue && (!a.FollowUpId.HasValue || a.FollowUpId == 0))
                .GroupBy(a => a.ReportId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var followUpAttachmentsByFollowUp = attachments
                .Where(a => a.FollowUpId.HasValue && (!a.ReportId.HasValue || a.ReportId == 0))
                .GroupBy(a => a.FollowUpId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            for (var i = 0; i < reports.Count; i++)
            {
                var report = reports[i];
                latestFollowUpsByReport.TryGetValue(report.ReportId, out var followUp);
                reportAttachmentsByReport.TryGetValue(report.ReportId, out var reportAttachments);

                var followUpAttachments = followUp?.FollowUpId is int followUpId &&
                                          followUpAttachmentsByFollowUp.TryGetValue(followUpId, out var items)
                    ? items
                    : new List<Attachment>();

                var values = GetExportValues(report, followUp, reportAttachments ?? new List<Attachment>(), followUpAttachments);

                for (var j = 0; j < values.Length; j++)
                    worksheet.Cell(i + 2, j + 1).Value = values[j];
            }

            var usedRange = worksheet.RangeUsed();
            if (usedRange is not null)
            {
                usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                usedRange.Style.Alignment.WrapText = true;
                usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            }

            var headerRange = worksheet.Range(1, 1, 1, headers.Length);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
            worksheet.SheetView.FreezeRows(1);
            worksheet.Columns().AdjustToContents(1, 50);
            SetPhotoLinkColumnWidths(worksheet, headers);

            return workbook;
        }

        private static void SetPhotoLinkColumnWidths(IXLWorksheet worksheet, string[] headers)
        {
            for (var i = 0; i < headers.Length; i++)
            {
                if (!headers[i].Contains("Link Foto", StringComparison.OrdinalIgnoreCase))
                    continue;

                var column = worksheet.Column(i + 1);
                column.Width = 35;
                column.Style.Alignment.WrapText = true;
            }
        }

        private static string[] GetExportHeaders(string reportType)
        {
            return reportType switch
            {
                "Machine" =>
                [
                    "Status",
                    "Reporter",
                    "Report Date",
                    "Name Mesin",
                    "Description",
                    "Link Foto",
                    "Follow-up oleh",
                    "Sparepart",
                    "Detail Follow-Up",
                    "Link Foto Follow Up"
                ],
                "Suggestion" =>
                [
                    "Status",
                    "Reporter",
                    "Report Date",
                    "Perihal/About",
                    "Description",
                    "Link Foto",
                    "Follow-up oleh",
                    "Detail Follow-Up",
                    "Link Foto Follow Up"
                ],
                "Kaizen" =>
                [
                    "Status",
                    "Reporter",
                    "Report Date",
                    "Area",
                    "Kondisi Sebelum",
                    "Link Foto Sebelum",
                    "Kondisi Sesudah",
                    "Link Foto Sesudah",
                    "Follow-up oleh",
                    "Detail Follow-Up",
                    "Link Foto Follow Up"
                ],
                "NearMiss" =>
                [
                    "Status",
                    "Pelapor",
                    "Tanggal Laporan",
                    "Area",
                    "Kondisi Berbahaya",
                    "Link Foto",
                    "Follow-up oleh",
                    "Detail Follow-up",
                    "Link Foto Follow-up"
                ],
                _ =>
                [
                    "Status",
                    "Reporter",
                    "Report Date",
                    "Area",
                    "Description",
                    "Link Foto",
                    "Follow-up oleh",
                    "Detail Follow-Up",
                    "Link Foto Follow Up"
                ]
            };
        }

        private string[] GetExportValues(
            MasterReport report,
            MasterFollowUp? followUp,
            List<Attachment> reportAttachments,
            List<Attachment> followUpAttachments)
        {
            var reportPhotos = reportAttachments.Where(IsImageAttachment).ToList();
            var followUpPhotoLinks = JoinLinks(followUpAttachments.Where(IsImageAttachment));

            return report.ReportType switch
            {
                "Machine" =>
                [
                    report.Status ?? string.Empty,
                    report.ReportedBy,
                    FormatDate(report.SubmissionDate),
                    report.Machine?.MachineName ?? string.Empty,
                    report.Description ?? string.Empty,
                    JoinLinks(reportPhotos),
                    followUp?.ResponsiblePerson ?? string.Empty,
                    followUp?.MaterialsUsed ?? string.Empty,
                    followUp?.Description ?? string.Empty,
                    followUpPhotoLinks
                ],
                "Suggestion" =>
                [
                    report.Status ?? string.Empty,
                    report.ReportedBy,
                    FormatDate(report.SubmissionDate),
                    FirstNonBlank(report.About, report.Area),
                    report.Description ?? string.Empty,
                    JoinLinks(reportPhotos),
                    followUp?.ResponsiblePerson ?? string.Empty,
                    followUp?.Description ?? string.Empty,
                    followUpPhotoLinks
                ],
                "Kaizen" =>
                [
                    report.Status ?? string.Empty,
                    report.ReportedBy,
                    FormatDate(report.SubmissionDate),
                    report.Area ?? string.Empty,
                    report.Description ?? string.Empty,
                    JoinLinks(reportPhotos.Where(a => a.FileName.StartsWith("before_", StringComparison.OrdinalIgnoreCase))),
                    report.Description2 ?? string.Empty,
                    JoinLinks(reportPhotos.Where(a => a.FileName.StartsWith("after_", StringComparison.OrdinalIgnoreCase))),
                    followUp?.ResponsiblePerson ?? string.Empty,
                    followUp?.Description ?? string.Empty,
                    followUpPhotoLinks
                ],
                _ =>
                [
                    report.Status ?? string.Empty,
                    report.ReportedBy,
                    FormatDate(report.SubmissionDate),
                    report.Area ?? string.Empty,
                    report.Description ?? string.Empty,
                    JoinLinks(reportPhotos),
                    followUp?.ResponsiblePerson ?? string.Empty,
                    followUp?.Description ?? string.Empty,
                    followUpPhotoLinks
                ]
            };
        }

        private static string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd HH:mm");
        }

        private string JoinLinks(IEnumerable<Attachment> attachments)
        {
            return string.Join(", ", attachments
                .Select(a => _attachmentDelivery.Resolve(a.AttachmentId, a.FilePath).DeliveryPath)
                .Where(path => !string.IsNullOrWhiteSpace(path)));
        }

        private static string FirstNonBlank(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static bool IsImageAttachment(Attachment attachment)
        {
            return attachment.FileType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                   attachment.FilePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                   attachment.FilePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   attachment.FilePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   attachment.FilePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetReportTypeFileName(string reportType)
        {
            return reportType switch
            {
                "NearMiss" => "NearMiss",
                "Machine" => "Machine",
                "Suggestion" => "Suggestion",
                "Kaizen" => "Kaizen",
                _ => "Report"
            };
        }

        private static string CanonicalizeReportType(string reportType)
        {
            return reportType?.Trim().ToLowerInvariant() switch
            {
                "machine" => "Machine",
                "nearmiss" => "NearMiss",
                "suggestion" => "Suggestion",
                "kaizen" => "Kaizen",
                _ => reportType ?? string.Empty
            };
        }

        public class ReportRow
        {
            public int ReportId { get; set; }
            public string ProtectedReportId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string ReportType { get; set; } = string.Empty;
            public string ReportedBy { get; set; } = string.Empty;
            public string? About { get; set; } = string.Empty;
            public string Area { get; set; } = string.Empty;
            public string MachineName { get; set; } = string.Empty;
            public string SubmissionDateText { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string ShortDescription { get; set; } = string.Empty;
            /// <summary>Resolved context value: Area, MachineName, or About depending on report type.</summary>
            public string ContextValue { get; set; } = string.Empty;
        }
    }
}
