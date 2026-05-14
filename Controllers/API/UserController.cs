using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Filters;
using ReportSystem.Models;

namespace ReportSystem.Controllers.API
{
    [ApiController]
    [Route("api/user")]
    [Produces("application/json")]
    [SessionAuthorize] // ← All endpoints: must be logged in
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<UserController> _logger;

        public UserController(AppDbContext context, ILogger<UserController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ── DTOs ──────────────────────────────────────────────────────────────

        public class AddEditUserForm
        {
            public int? UserId { get; set; }
            public string Fullname { get; set; } = "";
            public string Username { get; set; } = "";
            public string Email { get; set; } = "";
            public string Role { get; set; } = "";
            public string IsActive { get; set; } = "1";
        }

        public class ResetPasswordForm
        {
            public string Username { get; set; } = "";
        }

        public class ChangePasswordForm
        {
            public string OldPassword { get; set; } = "";
            public string NewPassword { get; set; } = "";
            public string ConfirmPassword { get; set; } = "";
        }

        private static readonly string[] AllowedRoles =
            { "Admin", "Machine", "Nearmiss", "Suggestion", "Kaizen" };

        // ── GET /api/user/GetUsers  [Admin] ───────────────────────────────────

        [HttpGet("GetUsers")]
        [SessionAuthorize("Admin")]
        public async Task<IActionResult> GetUsers()
        {
            try
            {
                var users = await _context.MasterUsers
                    .AsNoTracking()
                    .OrderBy(u => u.Username)
                    .Select(u => new
                    {
                        u.UserId,
                        u.Fullname,
                        u.Username,
                        u.Email,
                        u.Role,
                        u.IsActive,
                        u.CreatedDate,
                        u.UpdatedDate
                    })
                    .ToListAsync();

                return Ok(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching users");
                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
            }
        }

        // ── GET /api/user/GetUser/{id}  [Admin] ───────────────────────────────

        [HttpGet("GetUser/{id:int}")]
        [SessionAuthorize("Admin")]
        public async Task<IActionResult> GetUser(int id)
        {
            try
            {
                var user = await _context.MasterUsers
                    .AsNoTracking()
                    .Where(u => u.UserId == id)
                    .Select(u => new
                    {
                        u.UserId,
                        u.Fullname,
                        u.Username,
                        u.Email,
                        u.Role,
                        u.IsActive,
                        u.CreatedDate,
                        u.UpdatedDate
                    })
                    .FirstOrDefaultAsync();

                if (user == null)
                    return NotFound(new { message = "User not found." });

                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user {Id}", id);
                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
            }
        }

        // ── POST /api/user/AddEditUser  [Admin] ───────────────────────────────

        [HttpPost("AddEditUser")]
        [SessionAuthorize("Admin")]
        public async Task<IActionResult> AddEditUser([FromBody] AddEditUserForm form)
        {
            if (form == null ||
                string.IsNullOrWhiteSpace(form.Username) ||
                string.IsNullOrWhiteSpace(form.Email) ||
                string.IsNullOrWhiteSpace(form.Role))
            {
                return BadRequest(new { message = "Please fill all required fields before submitting." });
            }

            if (!AllowedRoles.Contains(form.Role.Trim()))
                return BadRequest(new { message = "Invalid role selected." });

            var isNew = form.UserId is null or 0;
            var active = form.IsActive?.Trim() == "1" ? (byte)1 : (byte)0;
            var now = DateTime.Now;

            try
            {
                if (isNew)
                {
                    // ── Add ──────────────────────────────────────────────────
                    bool duplicateUsername = await _context.MasterUsers
                        .AnyAsync(u => u.Username == form.Username.Trim());

                    if (duplicateUsername)
                        return BadRequest(new { message = "Username already exists." });

                    bool duplicateEmail = await _context.MasterUsers
                        .AnyAsync(u => u.Email == form.Email.Trim());

                    if (duplicateEmail)
                    {
                        return BadRequest(new { message = "Email already exists." });
                    }

                    _context.MasterUsers.Add(new MasterUser
                    {
                        Fullname = form.Fullname.Trim(),
                        Username = form.Username.Trim(),
                        Email = form.Email.Trim(),
                        Password = BCrypt.Net.BCrypt.HashPassword("ReportDesk123!"),
                        Role = form.Role.Trim(),
                        IsActive = active,
                        CreatedDate = now,
                        UpdatedDate = now
                    });

                    await _context.SaveChangesAsync();
                    return Ok(new { message = "User successfully added." });
                }
                else
                {
                    // ── Edit ─────────────────────────────────────────────────
                    var user = await _context.MasterUsers
                        .FirstOrDefaultAsync(u => u.UserId == form.UserId);

                    if (user == null)
                        return NotFound(new { message = "User not found." });

                    bool duplicateUsername = await _context.MasterUsers
                        .AnyAsync(u => u.Username == form.Username.Trim()
                                    && u.UserId != form.UserId);

                    if (duplicateUsername)
                        return BadRequest(new { message = "Username already exists." });

                    bool duplicateEmail = await _context.MasterUsers
                        .AnyAsync(u => u.Email == form.Email.Trim()
                                    && u.UserId != form.UserId);

                    if (duplicateEmail)
                    {
                        return BadRequest(new { message = "Email already exists." });
                    }

                    user.Fullname = form.Fullname.Trim();
                    user.Username = form.Username.Trim();
                    user.Email = form.Email.Trim();
                    user.Role = form.Role.Trim();
                    user.IsActive = active;
                    user.UpdatedDate = now;

                    await _context.SaveChangesAsync();
                    return Ok(new { message = "User successfully updated." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving user {Username}", form.Username);
                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
            }
        }

        // ── DELETE /api/user/DeleteUser/{id}  [Admin] ─────────────────────────

        [HttpDelete("DeleteUser/{id:int}")]
        [SessionAuthorize("Admin")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            // Guard: admin cannot delete their own account
            if (id == GetCurrentUserId())
                return BadRequest(new { message = "You cannot delete your own account." });

            try
            {
                var user = await _context.MasterUsers.FindAsync(id);

                if (user == null)
                    return NotFound(new { message = "User not found." });

                _context.MasterUsers.Remove(user);
                await _context.SaveChangesAsync();

                return Ok(new { message = "User successfully deleted." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user {Id}", id);
                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
            }
        }

        // ── POST /api/user/ResetPassword  [Admin] ─────────────────────────────

        [HttpPost("ResetPassword")]
        [SessionAuthorize("Admin")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordForm form)
        {
            if (string.IsNullOrWhiteSpace(form?.Username))
                return BadRequest(new { message = "Username is required." });

            try
            {
                var user = await _context.MasterUsers
                    .FirstOrDefaultAsync(u => u.Username == form.Username.Trim());

                if (user == null)
                    return NotFound(new { message = "User not found." });

                user.Password = BCrypt.Net.BCrypt.HashPassword("ReportDesk123!");
                user.UpdatedDate = DateTime.Now;

                await _context.SaveChangesAsync();
                return Ok(new { message = $"Password for '{user.Username}' has been reset." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting password for {Username}", form.Username);
                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
            }
        }

        // ── POST /api/user/ChangePassword  [Any logged-in user] ──────────────

        [HttpPost("ChangePassword")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordForm form)
        {
            if (string.IsNullOrWhiteSpace(form.OldPassword))
                return BadRequest(new { message = "Old password cannot be empty." });

            if (string.IsNullOrWhiteSpace(form.NewPassword))
                return BadRequest(new { message = "New password cannot be empty." });

            if (form.NewPassword.Length < 6)
                return BadRequest(new { message = "Password must be at least 6 characters long." });

            if (form.NewPassword.Length > 100)
                return BadRequest(new { message = "Password is too long." });

            if (form.NewPassword != form.ConfirmPassword)
                return BadRequest(new { message = "Password confirmation does not match." });

            var userId = GetCurrentUserId();
            if (userId == 0)
                return Unauthorized(new { message = "Session expired. Please log in again.", redirect = "/Login" });

            try
            {
                var user = await _context.MasterUsers.FindAsync(userId);

                if (user == null)
                    return NotFound(new { message = "User not found." });

                if (!BCrypt.Net.BCrypt.Verify(form.OldPassword, user.Password))
                    return BadRequest(new { message = "Old password is incorrect." });

                if (BCrypt.Net.BCrypt.Verify(form.NewPassword, user.Password))
                    return BadRequest(new { message = "New password must be different from your current password." });

                user.Password = BCrypt.Net.BCrypt.HashPassword(form.NewPassword);
                user.UpdatedDate = DateTime.Now;

                await _context.SaveChangesAsync();

                HttpContext.Session.Clear();
                return Ok(new { message = "Password changed successfully. Please log in again.", redirect = "/Login" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password for user {UserId}", userId);
                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private int GetCurrentUserId()
        {
            var s = HttpContext.Session.GetString("UserId");
            return int.TryParse(s, out var id) ? id : 0;
        }
    }
}