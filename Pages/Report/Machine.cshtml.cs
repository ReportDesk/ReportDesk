using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;
using ReportSystem.Services;

namespace ReportSystem.Pages.Report
{
    public sealed class MachineModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly ILogger<MachineModel> _logger;

        public MachineModel(AppDbContext db, ILogger<MachineModel> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ── Page metadata ─────────────────────────────────────────────────────
        public string ReportType => "Machine";
        public string PageHeading => "Machine Report";
        public string PageDescription => "Melaporkan mesin atau peralatan yang rusak.";

        // ── Temp data ─────────────────────────────────────────────────────────
        [TempData]
        public string? SuccessMessage { get; set; }

        [TempData]
        public string? MachineLoadError { get; set; }

        // ── Bound data ────────────────────────────────────────────────────────
        public DateTime CurrentTimeDisplay =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("Asia/Jakarta"));
        public List<MasterMachine> Machines { get; set; } = new();

        // ── GET ───────────────────────────────────────────────────────────────
        public async Task OnGetAsync()
        {
            try
            {
                Machines = await _db.Set<MasterMachine>()
                    .AsNoTracking()
                    .Where(m => m.IsActive)
                    .OrderBy(m => m.MachineName)
                    .ThenBy(m => m.MachineArea)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load machine list for Machine Report page.");
                MachineLoadError = "Could not load machine list. Please refresh or contact support.";
                Machines = new List<MasterMachine>();
            }
        }
    }
}
