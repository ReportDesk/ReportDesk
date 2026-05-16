using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using ReportSystem.Services;


namespace ReportSystem.Controllers.API
{
    [ApiController]
    [Route("api/email")]
    public class EmailController : ControllerBase
    {
        private readonly IEmailService _emailService;
        private readonly IConfiguration _config;

        public EmailController(IEmailService emailService, IConfiguration config)
        {
            _emailService = emailService;
            _config = config;
        }

        // POST api/email/reportNotification
        [HttpPost("reportNotification")]
        public async Task<IActionResult> ReportNotification([FromBody] ReportNotificationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ReportType) || string.IsNullOrWhiteSpace(request.ToEmail))
            {
                return BadRequest("ReportType and ToEmail are required");
            }

            var validTypes = new[]
            {
                EmailService.MachineReport,
                EmailService.NearmissReport,
                EmailService.Suggestion,
                EmailService.KaizenReport
            };

            if (!validTypes.Contains(request.ReportType))
            {
                return BadRequest($"Invalid report type. Valid types: {string.Join(", ", validTypes)}");
            }

            return Ok(new { message = $"Report notification sent for {request.ReportType}." });
        }
    }

    public class ForgotPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ReportNotificationRequest
    {
        public string ReportType { get; set; } = string.Empty;
        public string ToEmail { get; set; } = string.Empty;
        public object ReportData { get; set; } = new();
    }
}
