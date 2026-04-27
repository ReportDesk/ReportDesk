using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Services;

namespace ReportSystem.Pages.Report;

public abstract class ReportPageModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ReportService _reportService;

    protected ReportPageModel(AppDbContext db, ReportService reportService)
    {
        _db = db;
        _reportService = reportService;
    }

    public abstract string ReportType { get; }
    public abstract string PageHeading { get; }
    public abstract string PageDescription { get; }

    public string CurrentTimeDisplay { get; private set; } = string.Empty;
    public List<MachineOption> Machines { get; private set; } = new();
    public string? MachineLoadError { get; private set; }

    [BindProperty]
    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(100)]
    public string? Area { get; set; }

    [BindProperty]
    public int? MachineId { get; set; }

    [BindProperty]
    [Required]
    [StringLength(4000)]
    public string Description { get; set; } = string.Empty;

    [BindProperty]
    public IFormFile? Photo { get; set; }

    [BindProperty]
    public IFormFile? BeforePhoto { get; set; }

    [BindProperty]
    public IFormFile? AfterPhoto { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PreparePageAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await PreparePageAsync(cancellationToken);

        if (ReportType == ReportTypes.Machine && MachineId is null)
        {
            ModelState.AddModelError(nameof(MachineId), "Machine is required.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var reportId = await _reportService.CreateReportAsync(new ReportSubmissionRequest
            {
                ReportType = ReportType,
                Title = Title,
                Name = Name,
                Area = Area,
                MachineId = MachineId,
                Description = Description,
                Photo = Photo,
                BeforePhoto = BeforePhoto,
                AfterPhoto = AfterPhoto
            }, cancellationToken);

            SuccessMessage = $"Report #{reportId} submitted.";
            return RedirectToPage();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    private async Task PreparePageAsync(CancellationToken cancellationToken)
    {
        CurrentTimeDisplay = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        try
        {
            Machines = await _db.MasterMachines
                .OrderBy(x => x.MachineName)
                .Select(x => new MachineOption(x.MachineId, x.MachineName))
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            MachineLoadError = $"Machine list is unavailable: {ex.Message}";
            Machines = new();
        }
    }

    public sealed record MachineOption(int MachineId, string MachineName);
}
