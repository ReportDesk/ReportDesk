using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ReportSystem.Pages.Admin
{
    public class ReportDetailModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public int ReportId { get; set; }

        public string ReportDateText { get; private set; } = string.Empty;
        public string ReportDescription { get; private set; } = string.Empty;

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
        public IFormFile? FollowUpPhoto { get; set; }

        [TempData]
        public string? SuccessMessage { get; set; }

        public IActionResult OnGet()
        {
            var report = FindMockReport(ReportId);
            if (report is null)
            {
                return NotFound();
            }

            ReportDateText = report.DateText;
            ReportDescription = report.Description;
            SelectedStatus = report.Status;
            return Page();
        }

        public IActionResult OnPost()
        {
            var report = FindMockReport(ReportId);
            if (report is null)
            {
                return NotFound();
            }

            ReportDateText = report.DateText;
            ReportDescription = report.Description;

            if (!ModelState.IsValid)
            {
                return Page();
            }

            SuccessMessage = "Follow up saved (mock only, no database write).";
            return RedirectToPage("/Admin/ReportDetail", new { reportId = ReportId });
        }

        private static MockReport? FindMockReport(int reportId)
        {
            return GetMockReports().FirstOrDefault(x => x.ReportId == reportId);
        }

        private static List<MockReport> GetMockReports()
        {
            return new List<MockReport>
            {
                new()
                {
                    ReportId = 101,
                    DateText = "2026-03-25",
                    Status = "Open",
                    Description = "Conveyor alarm triggered repeatedly during shift handover."
                },
                new()
                {
                    ReportId = 102,
                    DateText = "2026-03-24",
                    Status = "On Progress",
                    Description = "Oil leak observed near pump seal on Machine A2."
                },
                new()
                {
                    ReportId = 103,
                    DateText = "2026-03-22",
                    Status = "Close",
                    Description = "Safety guard improvement proposal submitted by assembly team."
                }
            };
        }

        private sealed class MockReport
        {
            public int ReportId { get; set; }
            public string DateText { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }
    }
}
