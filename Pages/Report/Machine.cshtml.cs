using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ReportSystem.Models;
using ReportSystem.Services;

namespace ReportSystem.Pages.Report
{
    public sealed class MachineModel : PageModel
    {
        private readonly AppDbContext _db;

        public MachineModel(AppDbContext db)
        {
            _db = db;
        }

        public string ReportType = "Machine";
        public string PageHeading => "Machine Report";
        public string PageDescription => "Melaporkan mesin atau peralatan yang rusak.";

        [TempData]
        public string? SuccessMessage { get; set; }

        public DateTime CurrentTimeDisplay = DateTime.Now;

        [TempData]
        public string? MachineLoadError { get; set; }
        
        public List<MasterMachine> Machines { get; set; }
    }

}
