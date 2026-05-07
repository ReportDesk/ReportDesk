using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Data;
using ReportSystem.Models;

namespace ReportSystem.Data.Api
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
            public string Username { get; set; } = "";
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
            public string Role { get; set; } = "";
            public string IsActive { get; set; } = "1";
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

            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                if (isNew)
                {
                    var duplicateExists = await _context.MasterUsers
                        .AnyAsync(u => u.Username == form.Username.Trim());

                    if (duplicateExists)
                        return BadRequest(new { message = "Username already exists." });

                    var user = new MasterUser
                    {
                        Username = form.Username.Trim(),
                        Email = form.Email.Trim(),
                        Password = BCrypt.Net.BCrypt.HashPassword(form.Password),
                        Role = form.Role.Trim(),
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
                        .FirstOrDefaultAsync(u => u.UserId == form.UserId);

                    if (user == null)
                        return NotFound(new { message = "User not found." });

                    var duplicateExists = await _context.MasterUsers
                        .AnyAsync(u => u.Username == form.Username.Trim() && u.UserId != form.UserId);

                    if (duplicateExists)
                        return BadRequest(new { message = "Username already exists." });

                    user.Username = form.Username.Trim();
                    user.Email = form.Email.Trim();
                    user.Role = form.Role.Trim();
                    user.IsActive = isActiveNormalized;

                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();

                    return Ok(new { message = "User successfully updated." });
                }
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error saving user (username: {Username})", form.Username);
                return StatusCode(500, new { message = "An error occurred.", error = ex.Message });
            }
        }

        // DELETE /api/user/DeleteUser/{id}
        [HttpDelete("DeleteUser/{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var user = await _context.MasterUsers.FirstOrDefaultAsync(u => u.UserId == id);

                if (user == null)
                    return NotFound(new { message = "User not found." });

                _context.MasterUsers.Remove(user);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { message = "User successfully deleted." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error deleting user {Id}", id);
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