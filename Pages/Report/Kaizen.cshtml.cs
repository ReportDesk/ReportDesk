using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ReportSystem.Helpers;
using ReportSystem.Services;

namespace ReportSystem.Pages.Report
{
    public sealed class KaizenModel : PageModel
    {
        private readonly AppDbContext _db;

        public KaizenModel(AppDbContext db)
        {
            _db = db;
        }

        public string ReportType = "Kaizen";
        public string PageHeading => "Kaizen Report";
        public string PageDescription => "Melaporkan kaizen yang sudah selesai dilakukan.";

        [TempData]
        public string? SuccessMessage { get; set; }

        public string CurrentTimeDisplay => IndonesianTime.Now.ToString("dd MMM yyyy, HH:mm");
    }

}
