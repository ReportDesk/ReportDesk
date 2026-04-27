using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

// TODO: Add your DbContext using statement, e.g.:
// using ReportSystem.Data;
// using Microsoft.EntityFrameworkCore;

namespace ReportSystem.Pages.Admin
{
    public class ReportListModel : PageModel
    {
        private readonly AppDbContext _db;

        public ReportListModel(AppDbContext db)
        {
            _db = db;
        }

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
        public string? LoadError { get; private set; }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            try
            {
                var query = _db.MasterReports.AsNoTracking();

                if (FromDate.HasValue)
                {
                    query = query.Where(x => x.SubmissionDate >= FromDate.Value.Date);
                }

                if (ToDate.HasValue)
                {
                    var toExclusive = ToDate.Value.Date.AddDays(1);
                    query = query.Where(x => x.SubmissionDate < toExclusive);
                }

                var selectedStatuses = GetSelectedStatuses();
                if (selectedStatuses.Count > 0)
                {
                    query = query.Where(x => selectedStatuses.Contains(x.Status));
                }

                ReportRows = await query
                    .OrderByDescending(x => x.SubmissionDate)
                    .Select(x => new ReportRowViewModel
                    {
                        ReportId = x.ReportId,
                        Title = x.ReportTitle,
                        ReportType = x.ReportType,
                        ReportedBy = x.ReportedBy,
                        Area = x.Area,
                        SubmissionDateText = x.SubmissionDate.ToString("yyyy-MM-dd"),
                        Status = x.Status
                    })
                    .ToListAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                LoadError = $"Report data is unavailable: {ex.Message}";
                ReportRows = new();
            }
        }

        private List<string> GetSelectedStatuses()
        {
            var statuses = new List<string>();

            if (StatusOpen)
            {
                statuses.Add("Open");
            }

            if (StatusOnProgress)
            {
                statuses.Add("On Progress");
            }

            if (StatusClose)
            {
                statuses.Add("Close");
            }

            if (StatusDone)
            {
                statuses.Add("Done");
            }

            return statuses;
        }

        // ── View Model ────────────────────────────────────────────────────────

        public sealed class ReportRowViewModel
        {
            public int ReportId { get; set; }
            public string ReportType { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string ReportType { get; set; } = string.Empty;
            public string ReportedBy { get; set; } = string.Empty;
            public string Area { get; set; } = string.Empty;
            public string SubmissionDateText { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }
    }
}