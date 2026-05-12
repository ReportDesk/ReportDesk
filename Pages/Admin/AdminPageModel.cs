using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ReportSystem.Pages.Admin;

public abstract class AdminPageModel : PageModel
{
    protected IActionResult? RedirectIfNotAdmin()
    {
        var userId = HttpContext.Session.GetString("UserId");
        var role = HttpContext.Session.GetString("Role");

        if (string.IsNullOrWhiteSpace(userId) || !string.Equals(role, "Admin", StringComparison.Ordinal))
        {
            return RedirectToPage("/Login");
        }

        return null;
    }
}
