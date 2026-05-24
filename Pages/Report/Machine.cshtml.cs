using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ReportSystem.Pages.Report
{
    public sealed class MachineModel : PageModel
    {
        public string ReportType => "Machine";
        public string PageHeading => "Machine Report";
        public string PageDescription => "Melaporkan mesin atau peralatan yang rusak.";

        [TempData]
        public string? SuccessMessage { get; set; }

        public string CurrentTimeDisplay => DateTime.Now.ToString("dd MMM yyyy, HH:mm");
    }
}
