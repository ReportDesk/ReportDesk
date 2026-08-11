using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;
using System.ComponentModel.DataAnnotations;

namespace ReportSystem.Pages.Admin;

public sealed class MasterReporterModel(AppDbContext db) : PageModel
{
    public List<ReporterRowViewModel> Reporters { get; private set; } = [];
    [BindProperty] public ReporterFormInput Input { get; set; } = new();
    [BindProperty] public int? EditId { get; set; }
    [TempData] public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) { if (!IsAllowed()) return RedirectToPage("/AccessDenied"); await LoadAsync(ct); return Page(); }
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!IsAllowed()) return RedirectToPage("/AccessDenied");
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        var name = Input.ReporterName.Trim();
        if (await db.MasterReporters.AnyAsync(x => x.ReporterName == name && (!EditId.HasValue || x.ReporterId != EditId), ct)) { ModelState.AddModelError("Input.ReporterName", "A reporter with this name already exists."); await LoadAsync(ct); return Page(); }
        if (EditId.HasValue)
        {
            var reporter = await db.MasterReporters.FindAsync([EditId.Value], ct);
            if (reporter is null) return NotFound();
            reporter.ReporterName = name; reporter.IsActive = Input.IsActive; SuccessMessage = "Reporter updated.";
        }
        else { db.MasterReporters.Add(new MasterReporter { ReporterName = name, IsActive = Input.IsActive, CreatedDate = DateTime.UtcNow, CreatedBy = CurrentUserId() }); SuccessMessage = "Reporter added."; }
        await db.SaveChangesAsync(ct); return RedirectToPage();
    }
    public async Task<IActionResult> OnPostDeleteAsync(int reporterId, CancellationToken ct) { if (!IsAllowed()) return RedirectToPage("/AccessDenied"); var reporter = await db.MasterReporters.FindAsync([reporterId], ct); if (reporter is not null) { reporter.IsActive = false; await db.SaveChangesAsync(ct); } SuccessMessage = "Reporter deactivated."; return RedirectToPage(); }
    private async Task LoadAsync(CancellationToken ct) => Reporters = await db.MasterReporters.AsNoTracking().OrderBy(x => x.ReporterName).Select(x => new ReporterRowViewModel { ReporterId = x.ReporterId, ReporterName = x.ReporterName, IsActive = x.IsActive }).ToListAsync(ct);
    private int CurrentUserId() => int.TryParse(HttpContext.Session.GetString("UserId"), out var id) ? id : 0;
    private bool IsAllowed() => string.Equals(HttpContext.Session.GetString("Role"), "Admin", StringComparison.OrdinalIgnoreCase);
    public sealed class ReporterRowViewModel { public int ReporterId { get; set; } public string ReporterName { get; set; } = string.Empty; public bool IsActive { get; set; } }
    public sealed class ReporterFormInput { [Required] public string ReporterName { get; set; } = string.Empty; public bool IsActive { get; set; } = true; }
}
