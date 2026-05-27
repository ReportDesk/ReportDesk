using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ReportSystem.Helpers;
using ReportSystem.Services;

namespace ReportSystem.Pages.Report
{
    public sealed class SuggestionModel : PageModel
    {
        private readonly AppDbContext _db;
        public SuggestionModel(AppDbContext db)
        {
            _db = db;
        }

        public string ReportType = "Suggestion";
        public string PageHeading => "Suggestion";
        public string PageDescription => "Melaporkan ide perbaikan di segala bidang.";

        [TempData]
        public string? SuccessMessage { get; set; }

        public string CurrentTimeDisplay => IndonesianTime.Now.ToString("dd MMM yyyy, HH:mm");
    }

}

