using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

// TODO: Add your DbContext using statement, e.g.:
// using ReportSystem.Data;
// using Microsoft.EntityFrameworkCore;

namespace ReportSystem.Pages.Admin
{
    public class ReportListModel : PageModel
    {
        // TODO: Inject your DbContext here, e.g.:
        // private readonly AppDbContext _db;
        // public ReportListModel(AppDbContext db) { _db = db; }

        // ── Query Parameters ──────────────────────────────────────────────────

        /// <summary>
        /// Report type filter. Accepted values: Machine | NearMiss | Suggestion | Kaizen.
        /// Null / empty means "all types".
        /// </summary>
        [BindProperty(SupportsGet = true)]
        public string? ReportType { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool StatusOpen { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool StatusOnProgress { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool StatusClose { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool StatusDone { get; set; }

        // ── View Data ─────────────────────────────────────────────────────────

        public List<ReportRowViewModel> ReportRows { get; private set; } = new();

        public string PageTitle => ReportType switch
        {
            "Machine" => "Machine Report",
            "NearMiss" => "Near-Miss Report",
            "Suggestion" => "Suggestion",
            "Kaizen" => "Kaizen Report",
            _ => "All Reports"
        };

        // ── Handler ───────────────────────────────────────────────────────────

        public void OnGet()
        {
            // Collect only the statuses that were checked. If none checked → no status filter.
            var selectedStatuses = new List<string>();
            if (StatusOpen) selectedStatuses.Add("Open");
            if (StatusOnProgress) selectedStatuses.Add("On Progress");
            if (StatusClose) selectedStatuses.Add("Close");
            if (StatusDone) selectedStatuses.Add("Done");

            // ── TODO: Replace mock data with a real DB query ──────────────────
            //
            // Example with EF Core:
            //
            //   var query = _db.MasterReports.AsQueryable();
            //
            //   if (!string.IsNullOrEmpty(ReportType))
            //       query = query.Where(r => r.ReportType == ReportType);
            //
            //   if (FromDate.HasValue)
            //       query = query.Where(r => r.SubmissionDate >= FromDate.Value);
            //
            //   if (ToDate.HasValue)
            //       query = query.Where(r => r.SubmissionDate <= ToDate.Value.Date.AddDays(1).AddTicks(-1));
            //
            //   if (selectedStatuses.Count > 0)
            //       query = query.Where(r => selectedStatuses.Contains(r.Status));
            //
            //   ReportRows = query
            //       .OrderByDescending(r => r.SubmissionDate)
            //       .Select(r => new ReportRowViewModel
            //       {
            //           ReportId           = r.ReportId,
            //           ReportType         = r.ReportType,
            //           Title              = r.ReportTitle,
            //           ReportedBy         = r.ReportedBy,
            //           Area               = r.Area,
            //           SubmissionDateText = r.SubmissionDate.ToString("yyyy-MM-dd"),
            //           Status             = r.Status
            //       })
            //       .ToList();
            //
            // ─────────────────────────────────────────────────────────────────

            // ── Placeholder mock data (remove once DB is wired up) ────────────
            var allReports = new List<ReportRowViewModel>
            {
                new() { ReportId = 101, ReportType = "Machine",    Title = "Conveyor Alarm",           ReportedBy = "Rizky",  Area = "Packing Line 1", SubmissionDateText = "2026-03-25", Status = "Open"        },
                new() { ReportId = 102, ReportType = "Machine",    Title = "Oil Leak",                 ReportedBy = "Sinta",  Area = "Machine A2",     SubmissionDateText = "2026-03-24", Status = "On Progress" },
                new() { ReportId = 103, ReportType = "NearMiss",   Title = "Safety Guard Improvement", ReportedBy = "Bagus",  Area = "Assembly",       SubmissionDateText = "2026-03-22", Status = "Close"       },
                new() { ReportId = 104, ReportType = "Suggestion", Title = "Better Lighting Proposal", ReportedBy = "Dewi",   Area = "Warehouse",      SubmissionDateText = "2026-03-20", Status = "Open"        },
                new() { ReportId = 105, ReportType = "Kaizen",     Title = "Reduce Changeover Time",   ReportedBy = "Hendra", Area = "Production",     SubmissionDateText = "2026-03-18", Status = "Done"        },
            };

            var filtered = allReports.AsEnumerable();

            if (!string.IsNullOrEmpty(ReportType))
                filtered = filtered.Where(r => r.ReportType == ReportType);

            if (FromDate.HasValue)
                filtered = filtered.Where(r => DateTime.Parse(r.SubmissionDateText) >= FromDate.Value);

            if (ToDate.HasValue)
                filtered = filtered.Where(r => DateTime.Parse(r.SubmissionDateText) <= ToDate.Value);

            if (selectedStatuses.Count > 0)
                filtered = filtered.Where(r => selectedStatuses.Contains(r.Status));

            ReportRows = filtered.ToList();
        }

        // ── View Model ────────────────────────────────────────────────────────

        public sealed class ReportRowViewModel
        {
            public int ReportId { get; set; }
            public string ReportType { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string ReportedBy { get; set; } = string.Empty;
            public string Area { get; set; } = string.Empty;
            public string SubmissionDateText { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }
    }
}