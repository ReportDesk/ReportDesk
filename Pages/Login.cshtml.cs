using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;



using ReportSystem.Controllers;
using ReportSystem.Models;
using System.Security.Claims;


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

        [BindProperty]
        public string ErrorSource { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }

        public IActionResult OnGet()
        {
            if (HttpContext.Session.GetString("UserId") != null)
            {
                return Redirect(GetRedirectUrl(HttpContext.Session.GetString("Role") ?? ""));
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await AuthenticateUserAsync(Username, Password);

            if (user != null)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Username ?? ""),
                    new Claim(ClaimTypes.Email, user.Email ?? ""),
                    new Claim(ClaimTypes.NameIdentifier, user.Fullname ?? ""),
                    new Claim(ClaimTypes.Role, user.Role ?? "")
                };

                // Store Session
                HttpContext.Session.SetString("UserId", user.UserId.ToString());
                HttpContext.Session.SetString("Username", user.Username ?? "");
                HttpContext.Session.SetString("Role", user.Role ?? "");

                var identity = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme
                );

                var principal = new ClaimsPrincipal(identity);

                var authProperties = new AuthenticationProperties
                {
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    authProperties
                );

                return Redirect(GetRedirectUrl(user.Role));
            }
            return Page();
        }

        protected async Task<MasterUser?> AuthenticateUserAsync(string username, string password)
        {
            ErrorSource = "login";

            try
            {
                var user = await CheckUserAsync(username);

                if (user == null)
                    return null;

                // Check active user
                if (user.IsActive != (byte)1)
                {
                    ErrorMessage = "User Inactive";
                    return null;
                }

                // Verify password
                if (BCrypt.Net.BCrypt.Verify(password, user.Password))
                {
                    return user;
                }

                ErrorMessage = "Invalid Password";
                return null;
            }
            catch (Exception)
            {
                ErrorMessage = "Invalid username or password";
                return null;
            }
        }

        protected async Task<MasterUser?> CheckUserAsync(string username)
        {
            try
            {
                var user = await _context.MasterUsers
                    .FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                {
                    ErrorMessage = "User Not Found";
                    return null;
                }

                return user;
            }
            catch (Exception)
            {
                ErrorMessage = "Error in check user";
                return null;
            }
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