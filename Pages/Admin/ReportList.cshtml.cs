using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;

namespace ReportSystem.Pages.Admin
{
    public class ReportListModel : PageModel
    {
        private readonly AppDbContext _db;

        public ReportListModel(AppDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public string ReportType { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)] public DateTime? FromDate { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? ToDate { get; set; }
        [BindProperty(SupportsGet = true)] public bool StatusOpen { get; set; }
        [BindProperty(SupportsGet = true)] public bool StatusOnProgress { get; set; }
        [BindProperty(SupportsGet = true)] public bool StatusClose { get; set; }
        [BindProperty(SupportsGet = true)] public bool StatusDone { get; set; }

        // Machine-only filter
        [BindProperty(SupportsGet = true)] public int? MachineId { get; set; }
        public List<SelectListItem> MachineOptions { get; set; } = [];

        public bool IsMachineType => ReportType == "Machine";

        public List<ReportRow> ReportRows { get; set; } = [];
        public string LoadError { get; set; } = string.Empty;

        public async Task OnGetAsync()
        {
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

                var query = _db.MasterReports.AsQueryable();

                if (!string.IsNullOrWhiteSpace(ReportType))
                    query = query.Where(r => r.ReportType == ReportType);

                if (FromDate.HasValue)
                    query = query.Where(r => r.SubmissionDate >= FromDate.Value);

                if (ToDate.HasValue)
                    query = query.Where(r => r.SubmissionDate <= ToDate.Value.AddDays(1).AddTicks(-1));

                var selectedStatuses = new List<string>();
                if (StatusOpen) selectedStatuses.Add("Open");
                if (StatusOnProgress) selectedStatuses.Add("On Progress");
                if (StatusClose) selectedStatuses.Add("Close");
                if (StatusDone) selectedStatuses.Add("Done");

                if (selectedStatuses.Count > 0)
                    query = query.Where(r => selectedStatuses.Contains(r.Status));

                if (IsMachineType && MachineId.HasValue)
                    query = query.Where(r => r.MachineId == MachineId.Value);

                var reports = await query
                    .OrderByDescending(r => r.SubmissionDate)
                    .ToListAsync();

                ReportRows = reports.Select(r => new ReportRow
                {
                    ReportId = r.ReportId,
                    Title = r.ReportTitle,
                    ReportType = r.ReportType,
                    ReportedBy = r.ReportedBy,
                    Area = r.Area,
                    SubmissionDateText = r.SubmissionDate.ToString("dd MMM yyyy"),
                    Status = r.Status
                }).ToList();
            }
            catch (Exception ex)
            {
                LoadError = $"Failed to load reports: {ex.Message}";
            }
        }

        public class ReportRow
        {
            public int ReportId { get; set; }
            public string Title { get; set; } = string.Empty;
            public string ReportType { get; set; } = string.Empty;
            public string ReportedBy { get; set; } = string.Empty;
            public string Area { get; set; } = string.Empty;
            public string SubmissionDateText { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }
    }
}