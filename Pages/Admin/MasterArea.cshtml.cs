using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;
using System.ComponentModel.DataAnnotations;

namespace ReportSystem.Pages.Admin;

public sealed class MasterAreaModel(AppDbContext db) : PageModel
{
    public List<AreaRowViewModel> Areas { get; private set; } = [];
    [BindProperty] public AreaFormInput Input { get; set; } = new();
    [BindProperty] public int? EditId { get; set; }
    [TempData] public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) { if (!IsAllowed()) return RedirectToPage("/AccessDenied"); await LoadAsync(ct); return Page(); }
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!IsAllowed()) return RedirectToPage("/AccessDenied");
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        var name = Input.AreaName.Trim();
        if (await db.MasterAreas.AnyAsync(x => x.AreaName == name && (!EditId.HasValue || x.AreaId != EditId), ct)) { ModelState.AddModelError("Input.AreaName", "An area with this name already exists."); await LoadAsync(ct); return Page(); }
        if (EditId.HasValue)
        {
            var area = await db.MasterAreas.FindAsync([EditId.Value], ct);
            if (area is null) return NotFound();
            area.AreaName = name; area.IsActive = Input.IsActive; SuccessMessage = "Area updated.";
        }
        else { db.MasterAreas.Add(new MasterArea { AreaName = name, IsActive = Input.IsActive, CreatedDate = DateTime.UtcNow, CreatedBy = CurrentUserId() }); SuccessMessage = "Area added."; }
        await db.SaveChangesAsync(ct); return RedirectToPage();
    }
    public async Task<IActionResult> OnPostDeleteAsync(int areaId, CancellationToken ct) { if (!IsAllowed()) return RedirectToPage("/AccessDenied"); var area = await db.MasterAreas.FindAsync([areaId], ct); if (area is not null) { area.IsActive = false; await db.SaveChangesAsync(ct); } SuccessMessage = "Area deactivated."; return RedirectToPage(); }
    private async Task LoadAsync(CancellationToken ct) => Areas = await db.MasterAreas.AsNoTracking().OrderBy(x => x.AreaName).Select(x => new AreaRowViewModel { AreaId = x.AreaId, AreaName = x.AreaName, IsActive = x.IsActive }).ToListAsync(ct);
    private int CurrentUserId() => int.TryParse(HttpContext.Session.GetString("UserId"), out var id) ? id : 0;
    private bool IsAllowed() => string.Equals(HttpContext.Session.GetString("Role"), "Admin", StringComparison.OrdinalIgnoreCase);
    public sealed class AreaRowViewModel { public int AreaId { get; set; } public string AreaName { get; set; } = string.Empty; public bool IsActive { get; set; } }
    public sealed class AreaFormInput { [Required] public string AreaName { get; set; } = string.Empty; public bool IsActive { get; set; } = true; }
}
