using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace ReportSystem.Pages.Admin
{
    [Authorize(Roles = "Admin,Machine")]
    public class MainModel : PageModel
    {
        public string UserRole { get; set; }

        public void OnGet()
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Machine"))
            {
                var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                Response.Redirect(GetRedirectUrl(userRole ?? ""));
                return;
            }

            UserRole = User.FindFirst(ClaimTypes.Role)?.Value;
        }

        private string GetRedirectUrl(string role) => role switch
        {
            "Admin" => "/Admin/Main",
            "Machine" => "/Admin/Main",
            "Nearmiss" => "/Admin/ReportList?ReportType=NearMiss",
            "Suggestion" => "/Admin/ReportList?ReportType=Suggestion",
            "Kaizen" => "/Admin/ReportList?ReportType=Kaizen",
            _ => "/Index"
        };
    }
}
