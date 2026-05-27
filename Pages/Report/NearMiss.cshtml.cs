using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ReportSystem.Helpers;
using ReportSystem.Services;

namespace ReportSystem.Pages.Report
{
    public sealed class NearMissModel : PageModel
    {
        private readonly AppDbContext _db;

        public NearMissModel(AppDbContext db)
        {
            _db = db;
        }

        public string ReportType = "NearMiss";
        public string PageHeading => "Near-Miss Report";
        public string PageDescription => "Melaporkan temuan kondisi yang berpotensi bahaya.";

        [TempData]
        public string? SuccessMessage { get; set; }

        public string CurrentTimeDisplay => IndonesianTime.Now.ToString("dd MMM yyyy, HH:mm");
    }

}
