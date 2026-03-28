using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ReportSystem.Pages
{
    public class ReportFormModel : PageModel
    {
        public string CurrentTimeDisplay { get; private set; } = string.Empty;

        public void OnGet()
        {
            CurrentTimeDisplay = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }

        public void OnPost()
        {
            CurrentTimeDisplay = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }
    }
}
