using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ReportSystem.Pages
{
    public class ReportFormModel : PageModel
    {
        public IActionResult OnGet()
        {
            return RedirectToPage("/Report/Machine");
        }

        public IActionResult OnPost()
        {
            return RedirectToPage("/Report/Machine");
        }
    }
}
