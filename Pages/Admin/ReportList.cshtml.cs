using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ReportSystem.Pages.Admin
{
    public class ReportListModel : PageModel
    {
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

        public List<ReportRowViewModel> ReportRows { get; private set; } = new();

        public void OnGet()
        {
            ReportRows = new List<ReportRowViewModel>
            {
                new()
                {
                    ReportId = 101,
                    Title = "Conveyor Alarm",
                    ReportedBy = "Rizky",
                    Area = "Packing Line 1",
                    SubmissionDateText = "2026-03-25",
                    Status = "Open"
                },
                new()
                {
                    ReportId = 102,
                    Title = "Oil Leak",
                    ReportedBy = "Sinta",
                    Area = "Machine A2",
                    SubmissionDateText = "2026-03-24",
                    Status = "On Progress"
                },
                new()
                {
                    ReportId = 103,
                    Title = "Safety Guard Improvement",
                    ReportedBy = "Bagus",
                    Area = "Assembly",
                    SubmissionDateText = "2026-03-22",
                    Status = "Close"
                }
            };
        }

        public sealed class ReportRowViewModel
        {
            public int ReportId { get; set; }
            public string Title { get; set; } = string.Empty;
            public string ReportedBy { get; set; } = string.Empty;
            public string Area { get; set; } = string.Empty;
            public string SubmissionDateText { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }
    }
}
