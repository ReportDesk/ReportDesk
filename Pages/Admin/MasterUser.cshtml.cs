using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace ReportSystem.Pages.Admin
{
    public class MasterUserModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public MasterUserModel(AppDbContext db, IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
        }

        public List<UserRowViewModel> Users { get; private set; } = new();

        [TempData]
        public string? SuccessMessage { get; set; }

        // -- Form Models

        public sealed class AddUserForm
        {
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string Role { get; set; } = "";
            public bool IsActive { get; set; } = true; // In database, isactive is set to int where 0 inactive and 1 isactive
        }

        public sealed class EditUserForm
        {
            public string Username { get; set; } = "";
            public string Password { get; set; } = ""; // Optional
            public string Role { get; set; } = "";
            public bool IsActive { get; set; } = true; // In database, isactive is set to int where 0 inactive and 1 isactive
        }

        public sealed class ResetPasswordForm
        {
            public string Username { get; set; } = "";
        }

        public sealed class DeleteUserForm
        {
            public int UserId { get; set; }
        }

        // -- View Models

        public sealed class UserRowViewModel
        {
            public int UserId { get; set; }
            public string Username { get; set; } = string.Empty;
            public string Fullname { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }

        // -- Handlers

        public async Task<IActionResult> OnGetAsync(CancellationToken ct)
        {
            if (!IsAdmin()) return RedirectToPage("/AccessDenied");
            await LoadUsersAsync(ct);
            return Page();
        }

        public async Task<IActionResult> OnPostAddUserAsync([FromBody] AddUserForm form, CancellationToken ct)
        {
            if (!IsAdmin()) return Forbid();

            if (form == null ||
                string.IsNullOrWhiteSpace(form.Username) ||
                string.IsNullOrWhiteSpace(form.Password) ||
                string.IsNullOrWhiteSpace(form.Role))
            {
                return BadRequest(new { message = "All fields are required." });
            }

            var username = form.Username.Trim();

            // Validate username format
            if (!IsValidUsername(username))
            {
                return BadRequest(new { message = "Invalid username. Must start with a letter, lowercase only, no spaces." });
            }

            // Validate role
            if (!IsValidRole(form.Role))
            {
                return BadRequest(new { message = "Invalid role selected" });
            }

            // Check duplicate username
            bool exists = await _db.MasterUsers.AnyAsync(u => u.Username == username, ct);
            if (exists)
            {
                return BadRequest(new { message = $"Username {username} already exists." });
            }

            _db.MasterUsers.Add(new MasterUser
            {
                Username = username,
                Password = BCrypt.Net.BCrypt.HashPassword(form.Password),
                Role = form.Role,
                IsActive = (byte)(form.IsActive ? 1 : 0)
            });

            await _db.SaveChangesAsync(ct);
            return new OkObjectResult(new { message = "User added successfully" });
        }

        public async Task<IActionResult> OnPostEditUserAsync([FromBody] EditUserForm form, CancellationToken ct)
        {
            if (!IsAdmin()) return Forbid();

            if (form == null || 
                string.IsNullOrWhiteSpace(form.Username) ||
                string.IsNullOrWhiteSpace(form.Role))
            {
                return BadRequest(new { message = "Username and role are required" });
            }

            var username = form.Username.Trim();

            // Validate Role
            if (!IsValidRole(form.Role))
            {
                return BadRequest(new { message = "Invalid role selected" });
            }

            var user = await _db.MasterUsers
                .FirstOrDefaultAsync(u => u.Username == username, ct);

            if (user == null)
                return NotFound(new { message = "User not found" });

            user.Role = form.Role;
            user.IsActive = (byte)(form.IsActive ? 1 : 0);

            if (!string.IsNullOrWhiteSpace(form.Password))
            {
                user.Password = BCrypt.Net.BCrypt.HashPassword(form.Password);
            }

            await _db.SaveChangesAsync(ct);
            return new OkObjectResult(new { message = "User updated successfully" });
        }

        public async Task<IActionResult> OnPostDeleteUserAsync([FromBody] DeleteUserForm form, CancellationToken ct)
        {
            if (!IsAdmin()) return Forbid();

            if (form == null || form.UserId <= 0)
            {
                return BadRequest(new { message = "Invalid User." });
            }

            var user = await _db.MasterUsers.FindAsync(form.UserId, ct);

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            _db.MasterUsers.Remove(user);
            await _db.SaveChangesAsync(ct);
            return new OkObjectResult(new { message = "User deleted successfully" });
        }

        public async Task<IActionResult> OnPostResetPasswordAsync([FromBody] ResetPasswordForm form, CancellationToken ct)
        {
            if (!IsAdmin()) return Forbid();

            if (string.IsNullOrWhiteSpace(form?.Username))
                return BadRequest(new { message = "Username is required." });

            var user = await _db.MasterUsers.FirstOrDefaultAsync(u => u.Username == form.Username.Trim(), ct);

            if (user == null)
                return NotFound(new { message = "User not found." });

            user.Password = BCrypt.Net.BCrypt.HashPassword("ReportDesk123!");

            await _db.SaveChangesAsync(ct);
            return new OkObjectResult(new { message = $"Password reset successfully for '{user.Username}'." });
        }


        // -- Helpers

        private async Task LoadUsersAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            Users = await _db.MasterUsers
                .AsNoTracking()
                .OrderBy(u => u.Username)
                .Select(u => new UserRowViewModel
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    Fullname = u.Fullname,
                    Role = u.Role,
                    IsActive = u.IsActive == 1
                })
                .ToListAsync(ct);

            Console.WriteLine(_db.Database.GetConnectionString());
            Console.WriteLine($"LoadUsersAsync took {sw.ElapsedMilliseconds}ms");
        }

        private bool IsAdmin() =>
            HttpContext.Session.GetString("Role") == "Admin";

        private static bool IsValidUsername(string username)
        {
            var regex = new Regex(@"^[a-z][a-z0-9._]*[a-z0-9]$|^[a-z]$");
            return regex.IsMatch(username);
        }

        private static bool IsValidRole(string role)
        {
            var allowed = new[] { "Admin", "Machine", "Nearmiss", "Suggestion", "Kaizen" };
            return allowed.Contains(role);
        }

    }
}