using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Data;
using System.Net.NetworkInformation;

namespace ReportSystem.Pages
{
    public class LoginModel : PageModel
    {
        private readonly AppDbContext _context;

        public LoginModel(AppDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        public string Password { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            // Redirect already-logged-in users
            if (HttpContext.Session.GetString("UserId") != null)
                Response.Redirect(GetRedirectUrl(HttpContext.Session.GetString("Role") ?? ""));
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Please enter your username and password.";
                return Page();
            }

            var user = await _context.MasterUsers
                .FirstOrDefaultAsync(u => u.Username == Username.Trim() && u.IsActive == 1);

            if (user == null || !BCrypt.Net.BCrypt.Verify(Password, user.Password))
            {
                ErrorMessage = "Invalid username or password";
                return Page();
            }

            // Store Session
            HttpContext.Session.SetString("UserId", user.UserId.ToString());
            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("Role", user.Role);

            return Redirect(GetRedirectUrl(user.Role));
        }

        private static string GetRedirectUrl(string role) => role switch
        {
            "Admin" => "/Admin/Index",
            "Machine" => "/Admin/ReportList?ReportType=Machine",
            "NearMiss" => "/Admin/ReportList?ReportType=NearMiss",
            "Suggestion" => "/Admin/ReportList?ReportType=Suggestion",
            "Kaizen" => "/Admin/ReportList?ReportType=Kaizen"
        };
    }
}
