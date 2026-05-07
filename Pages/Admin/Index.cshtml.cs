using Microsoft.AspNetCore.Mvc;

namespace ReportSystem.Pages.Admin
{
    public class IndexModel : AdminPageModel
    {
        public IActionResult OnGet()
        {
            return RedirectIfNotAdmin() ?? Page();
        }
    }
}
