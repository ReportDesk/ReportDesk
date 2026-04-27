using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ReportSystem.Data;
using ReportSystem.Models;
using BCrypt.Net;

namespace ReportSystem.Data.Api
{
    [ApiController]
    [Route("api/user")]
    [Produces("application/json")]

    public class UserController : Controller
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
            public int? userId { get; set; }
            public string username { get; set; } = "";
            public string email { get; set; } = "";
            public string password { get; set; } = ""; // Required on insert only
            public string role { get; set; } = "";
            public string isActive { get; set; } = "1"; // 1 = Active, 0 = Inactive
        }

        // POST /api/user/AddEditUser
        public async Task<IActionResult> AddEditUser([FromBody] AddEditUserForm form)
        {
            if (form == null || 
                string.IsNullOrWhiteSpace(form.username) ||
                string.IsNullOrWhiteSpace(form.email) ||
                string.IsNullOrWhiteSpace(form.role))
            {
                return BadRequest(new { message = "Please fill all required fields before submitting." });
            }

            var isNew = form.userId == null || form.userId == 0;
            var isActiveNormalized = form.isActive?.Trim() == "1" ? (byte)1 : (byte)0;
            var now = DateTime.Now;

            if (isNew && string.IsNullOrWhiteSpace(form.password))
                return BadRequest(new { message = "Password is required when adding a new user." });

            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                if (isNew)
                {
                    var duplicateExists = await _context.MasterUsers
                                                .AnyAsync(u => u.Username == form.username.Trim());

                    if (duplicateExists)
                        return BadRequest(new { message = "Username already exists" });

                    var user = new MasterUser
                    {
                        Username = form.username.Trim(),
                        Email = form.email.Trim(),
                        Password = BCrypt.Net.BCrypt.HashPassword(form.password),
                        Role = form.role.Trim(),
                        IsActive = isActiveNormalized,
                        CreatedDate = now
                    };

                    _context.MasterUsers.Add(user);
                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();

                    return Ok(new { message = "User successfully added." });
                }
                else
                {
                    var user = await _context.MasterUsers
                                    .FirstOrDefaultAsync(u => u.UserId == form.userId);

                    if (user == null)
                        return NotFound(new { message = "User not found." });

                    var duplicateExists = await _context.MasterUsers
                                                .AnyAsync(u => u.Username == form.username.Trim() && u.UserId != form.userId);

                    if (duplicateExists)
                        return BadRequest(new { message = "Username already exists." });

                    user.Username = form.username.Trim();
                    user.Email = form.email.Trim();
                    user.Role = form.role.Trim();
                    user.IsActive = isActiveNormalized;

                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();

                    return Ok(new { message = "User successfully updated." });
                }
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error saving user (username: {Username})", form.username);
                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
            }
        }

        private int GetCurrentUserId()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }
    }
}
