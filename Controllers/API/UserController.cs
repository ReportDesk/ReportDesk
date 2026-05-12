using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;

namespace ReportSystem.Controllers.API
{
    [ApiController]
    [Route("api/user")]
    [Produces("application/json")]
    public class UserController : ControllerBase 
    {
        private readonly AppDbContext _context;
        private readonly ILogger<UserController> _logger;

        public UserController(AppDbContext context, ILogger<UserController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public class AddEditUserForm
        {
            public int? UserId { get; set; }
            public string Fullname { get; set; } = "";
            public string Username { get; set; } = "";
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
            public string Role { get; set; } = "";
            public string IsActive { get; set; } = "1";
        }

        public class ChangePasswordForm
        {
            public string OldPassword { get; set; } = "";
            public string NewPassword { get; set; } = "";
            public string ConfirmPassword { get; set; } = "";
        }

        // GET /api/user/GetUsers
        [HttpGet("GetUsers")]
        public async Task<IActionResult> GetUsers()
        {
            try
            {
                var users = await _context.MasterUsers
                    .Select(u => new
                    {
                        u.UserId,
                        u.Username,
                        u.Email,
                        u.Role,
                        u.IsActive,
                        u.CreatedDate
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

        // GET /api/user/GetUser/{id}
        [HttpGet("GetUser/{id}")]
        public async Task<IActionResult> GetUser(int id)
        {
            try
            {
                var user = await _context.MasterUsers
                    .Where(u => u.UserId == id)
                    .Select(u => new
                    {
                        u.UserId,
                        u.Username,
                        u.Email,
                        u.Role,
                        u.IsActive,
                        u.CreatedDate
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

        // POST /api/user/AddEditUser
        [HttpPost("AddEditUser")]
        public async Task<IActionResult> AddEditUser([FromBody] AddEditUserForm form)
        {
            if (form == null ||
                string.IsNullOrWhiteSpace(form.Username) ||
                string.IsNullOrWhiteSpace(form.Email) ||
                string.IsNullOrWhiteSpace(form.Role))
            {
                return BadRequest(new { message = "Please fill all required fields before submitting." });
            }

            var isNew = form.UserId == null || form.UserId == 0;
            var isActiveNormalized = form.IsActive?.Trim() == "1" ? (byte)1 : (byte)0;
            var now = DateTime.Now;

            if (isNew && string.IsNullOrWhiteSpace(form.Password))
                return BadRequest(new { message = "Password is required when adding a new user." });

            try
            {
                if (isNew)
                {
                    var duplicateExists = await _context.MasterUsers
                        .AnyAsync(u => u.Username == form.Username.Trim());

                    if (duplicateExists)
                        return BadRequest(new { message = "Username already exists." });

                    var defaultPass = "ReportDesk123!";

                    var user = new MasterUser
                    {
                        Username = form.Username.Trim(),
                        Email = form.Email.Trim(),
                        Password = BCrypt.Net.BCrypt.HashPassword(defaultPass),
                        Role = form.Role.Trim(),
                        IsActive = isActiveNormalized,
                        CreatedDate = now
                    };

                    _context.MasterUsers.Add(user);
                    await _context.SaveChangesAsync();

                    return Ok(new { message = "User successfully added." });
                }
                else
                {
                    var user = await _context.MasterUsers
                        .FirstOrDefaultAsync(u => u.UserId == form.UserId);

                    if (user == null)
                        return NotFound(new { message = "User not found." });

                    var duplicateExists = await _context.MasterUsers
                        .AnyAsync(u => u.Username == form.Username.Trim() && u.UserId != form.UserId);

                    if (duplicateExists)
                        return BadRequest(new { message = "Username already exists." });

                    user.Fullname = form.Fullname.Trim();
                    user.Username = form.Username.Trim();
                    user.Email = form.Email.Trim();
                    user.Role = form.Role.Trim();
                    user.IsActive = isActiveNormalized;

                    await _context.SaveChangesAsync();

                    return Ok(new { message = "User successfully updated." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving user (username: {Username})", form.Username);
                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
            }
        }

        // DELETE /api/user/DeleteUser/{id}
        [HttpDelete("DeleteUser/{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            try
            {
                var user = await _context.MasterUsers.FirstOrDefaultAsync(u => u.UserId == id);

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

        // CHANGE PASSWORD /api/user/ChangePassword
        [HttpPost("ChangePassword")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordForm form)
        {
            if (string.IsNullOrWhiteSpace(form.OldPassword))
                return BadRequest(new { message = "Old password cannot be empty" });

            if (string.IsNullOrWhiteSpace(form.NewPassword))
                return BadRequest(new { message = "New password cannot be empty" });

            if (form.NewPassword.Length < 6)
                return BadRequest(new { message = "Password must be at least 6 characters long" });

            if (form.NewPassword.Length > 100)
                return BadRequest(new { message = "Password is too long" });

            if (form.NewPassword != form.ConfirmPassword)
                return BadRequest(new { message = "Password confirmation does not match" });

            var userId = GetCurrentUserId();
            if (userId == 0)
                return Unauthorized(new { message = "You are not logged in.", redirect = "/Login" });

            try
            {
                var user = await _context.MasterUsers.FindAsync(userId);

                if (user == null)
                    return NotFound(new { message = "User not found." });

                if (!BCrypt.Net.BCrypt.Verify(form.OldPassword, user.Password))
                    return BadRequest(new { message = "Old password is incorrect" });

                if (BCrypt.Net.BCrypt.Verify(form.NewPassword, user.Password) || form.NewPassword == form.OldPassword)
                    return BadRequest(new { message = "New password must be different from your current password" });

                user.Password = BCrypt.Net.BCrypt.HashPassword(form.NewPassword);
                await _context.SaveChangesAsync();

                HttpContext.Session.Clear();
                return Ok(new { message = "Password change successfully. Please log in again", redirect = "/Login" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password for user {id}, userId");
                return StatusCode(500, new { message = "An error occured.", error = ex.Message });
            }
        }

        private int GetCurrentUserId()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }
    }
}