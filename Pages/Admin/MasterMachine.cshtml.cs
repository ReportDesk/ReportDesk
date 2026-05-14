using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;
using System.ComponentModel.DataAnnotations;

namespace ReportSystem.Pages.Admin
{
    public class MasterMachineModel : PageModel
    {
        private readonly AppDbContext _db;

        public MasterMachineModel(AppDbContext db)
        {
            _db = db;
        }

        public List<MachineRowViewModel> Machines { get; private set; } = new();

        [BindProperty]
        public MachineFormInput Input { get; set; } = new();

        [BindProperty]
        public int? EditId { get; set; }

        [TempData]
        public string? SuccessMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (!IsAllowed()) return RedirectToPage("/AccessDenied");
            await LoadMachinesAsync(ct);
            return Page();
        }

        public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
        {
            if (!IsAllowed()) return RedirectToPage("/AccessDenied");

            if (!ModelState.IsValid)
            {
                await LoadMachinesAsync(ct);
                return Page();
            }

            if (EditId.HasValue)
            {
                var machine = await _db.MasterMachines.FindAsync(new object[] { EditId.Value }, ct);
                if (machine is null) return NotFound();

                machine.MachineName = Input.MachineName.Trim();
                machine.MachineArea = Input.Area.Trim();
                machine.MachineType = Input.Type.Trim();
                SuccessMessage = "Machine updated.";
            }
            else
            {
                _db.MasterMachines.Add(new MasterMachine
                {
                    MachineName = Input.MachineName.Trim(),
                    MachineArea = Input.Area.Trim(),
                    MachineType = Input.Type.Trim(),
                    CreatedDate = DateTime.Now,
                    CreatedBy = int.Parse(HttpContext.Session.GetString("UserId") ?? "0")
                });
                SuccessMessage = "Machine added.";
            }

            await _db.SaveChangesAsync(ct);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int machineId, CancellationToken ct)
        {
            if (!IsAllowed()) return RedirectToPage("/AccessDenied");

            var machine = await _db.MasterMachines.FindAsync(new object[] { machineId }, ct);
            if (machine is not null)
            {
                _db.MasterMachines.Remove(machine);
                await _db.SaveChangesAsync(ct);
            }

            SuccessMessage = "Machine deleted.";
            return RedirectToPage();
        }

        private async Task LoadMachinesAsync(CancellationToken ct)
        {
            Machines = await _db.MasterMachines
                .AsNoTracking()
                .OrderBy(m => m.MachineName)
                .Select(m => new MachineRowViewModel
                {
                    MachineId = m.MachineId,
                    MachineName = m.MachineName,
                    Area = m.MachineArea,
                    Type = m.MachineType
                })
                .ToListAsync(ct);
        }

        private bool IsAllowed()
        {
            var role = HttpContext.Session.GetString("Role") ?? "";
            return role == "Admin" || role == "Machine";
        }

        public sealed class MachineRowViewModel
        {
            public int MachineId { get; set; }
            public string MachineName { get; set; } = string.Empty;
            public string Area { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
        }

        public sealed class MachineFormInput
        {
            [Required]
            public string MachineName { get; set; } = string.Empty;

            [Required]
            public string Area { get; set; } = string.Empty;

            [Required]
            public string Type { get; set; } = string.Empty;
        }
    }
}