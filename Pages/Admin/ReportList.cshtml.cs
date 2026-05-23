using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Helpers;
using ReportSystem.Models;

namespace ReportSystem.Pages.Admin
{
    public class ReportListModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly IDataProtectionProvider _dataProtectionProvider;

        public ReportListModel(AppDbContext db, IDataProtectionProvider dataProtectionProvider)
        {
            _db = db;
            _dataProtectionProvider = dataProtectionProvider;
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
        public bool IsNearMissOrKaizen => ReportType == "Nearmiss" || ReportType == "Kaizen";


        public string ContextColumnLabel => ReportType switch
        {
            "Machine" => "Machine",
            "Suggestion" => "About",
            _ => "Area"   // Nearmiss, Kaizen, unknown
        };

        public string PageTitle => ReportType switch
        {
            "Machine" => "Machine Report List",
            "Nearmiss" => "NearMiss Report List",
            "Suggestion" => "Suggestion",
            "Kaizen" => "Kaizen Report List",
            _ => "Report List"
        };

        public List<ReportRow> ReportRows { get; set; } = [];
        public string LoadError { get; set; } = string.Empty;

        public async Task OnGetAsync()
        {
            var role = HttpContext.Session.GetString("Role") ?? string.Empty;
            ReportType = CanonicalizeReportType(ReportType);

            var roleTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Machine"] = "Machine",
                ["Nearmiss"] = "Nearmiss",
                ["Suggestion"] = "Suggestion",
                ["Kaizen"] = "Kaizen"
            };

            if (roleTypeMap.TryGetValue(role, out var allowedType))
            {
                if (!string.Equals(ReportType, allowedType, StringComparison.OrdinalIgnoreCase))
                {
                    Response.Redirect($"/Admin/ReportList?ReportType={allowedType}");
                    return;
                }
            }
            else if (role != "Admin")
            {
                Response.Redirect("/Login");
                return;
            }

                try
                {
                    if (IsMachineType)
                    {
                        var machines = await _db.MasterMachines.OrderBy(m => m.MachineName).ToListAsync();
                        MachineOptions = machines.Select(m => new SelectListItem
                        {
                            Value = m.MachineId.ToString(),
                            Text = m.MachineName
                        }).ToList();
                    }

                    // Include Machine navigation so MachineName is available without a second query
                    var query = _db.MasterReports
                        .Include(r => r.Machine)
                        .AsQueryable();

                    if (!string.IsNullOrWhiteSpace(ReportType))
                        query = query.Where(r => r.ReportType == ReportType);

                    if (DateTime.TryParse(FromDate, out var fromDate))
                        query = query.Where(r => r.SubmissionDate >= fromDate);

                    if (DateTime.TryParse(ToDate, out var toDate))
                        query = query.Where(r => r.SubmissionDate <= toDate.AddDays(1).AddTicks(-1));

                    if (Status.Count > 0)
                    {
                        var normalizedStatuses = Status
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => s.Trim().ToLower())
                            .ToList();

                        query = query.Where(r => normalizedStatuses.Contains(r.Status.ToLower()));
                    }

                    if (IsMachineType && MachineId.HasValue)
                        query = query.Where(r => r.MachineId == MachineId.Value);

                    var reports = await query
                        .OrderByDescending(r => r.SubmissionDate)
                        .ToListAsync();

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
                        // ContextValue is the value shown in the dynamic "Area / Mesin / Tentang" column
                        ContextValue = r.ReportType switch
                        {
                            "Machine" => r.Machine?.MachineName ?? string.Empty,
                            "Suggestion" => r.About ?? string.Empty,
                            _ => r.Area ?? string.Empty          // Nearmiss, Kaizen
                        }
                    }).ToList();
                }
                catch (Exception ex)
                {
                    LoadError = $"Error loading report: {ex.Message}";
                }
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
            public string About { get; set; } = string.Empty;
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
