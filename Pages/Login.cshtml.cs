using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;



using ReportSystem.Controllers;
using ReportSystem.Models;
using ReportSystem.Services;
using System.Security.Claims;


namespace ReportSystem.Pages
{
    public class LoginModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;

        public LoginModel(AppDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        [BindProperty]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        public string Password { get; set; } = string.Empty;

        [BindProperty]
        public string ErrorSource { get; set; } = string.Empty;

        [BindProperty]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        public string Token { get; set; } = string.Empty;

        [BindProperty]
        public string NewPassword { get; set; } = string.Empty;

        [BindProperty]
        public string ConfirmPassword { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }

        public string? ResetToken { get; set; }

        public IActionResult OnGet()
        {
            if (HttpContext.Session.GetString("UserId") != null)
            {
                return Redirect(GetRedirectUrl(HttpContext.Session.GetString("Role") ?? ""));
            }

            ResetToken = Request.Query["token"];
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

        public async Task<IActionResult> OnPostForgotPasswordAsync()
        {
            ErrorSource = "forgot";

            var user = await _context.MasterUsers
                .FirstOrDefaultAsync(u => u.Email == Email);

            if (user == null)
            {
                ErrorMessage = "Email not found.";
                return Page();
            }

            var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                               .Replace("+", "-").Replace("/", "_").TrimEnd('=');
            user.PasswordResetToken = token;
            user.PasswordResetExpiry = DateTime.UtcNow.AddHours(1);
            await _context.SaveChangesAsync();

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var resetLink = $"{baseUrl}/Login?token={token}";

            await _emailService.SendForgotPasswordAsync(user.Email!, resetLink);

            TempData["ForgotSuccess"] = "Reset link sent! Check your email.";
            return RedirectToPage("/Login");
        }

        public async Task<IActionResult> OnPostResetPasswordAsync()
        {
            ErrorSource = "reset";

            if (NewPassword != ConfirmPassword)
            {
                ErrorMessage = "Password do not match.";
                return Page();
            }

            var user = await _context.MasterUsers
                .FirstOrDefaultAsync(u => u.PasswordResetToken == Token
                                       && u.PasswordResetExpiry > DateTime.UtcNow);

            if (user == null)
            {
                ErrorMessage = "Invalid or expired reset link.";
                return Page();
            }

            user.Password = BCrypt.Net.BCrypt.HashPassword(NewPassword);
            user.PasswordResetToken = null;
            user.PasswordResetExpiry = null;
            await _context.SaveChangesAsync();

            TempData["ResetSuccess"] = "Password reset successful! Redirecting to login...";
            return RedirectToPage("/Login");
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