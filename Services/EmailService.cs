using Microsoft.AspNetCore.Cors;
using Microsoft.Extensions.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ReportSystem.Services
{
    public interface IEmailService
    {
        Task SendForgotPasswordAsync(string toEmail, string resetLink);
        Task SendReportNotificationAsync(string reportType, string toEmail, object reportData);
    }
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;
        private readonly string _fromEmail;
        private readonly string _fromName;

        // Report type contants
        public const string MachineReport = "Machine";
        public const string NearmissReport = "Nearmiss";
        public const string Suggestion = "Suggestion";
        public const string KaizenReport = "Kaizen";

        // Each report type has its own dedicated sender email
        private readonly Dictionary<string, string> _reportSenderEmails;

        public EmailService(IConfiguration config)
        {
            _config = config;
            _host = config["Email:Smtp:Host"]!;
            _port = int.Parse(config["Email:Smtp:Port"]!);
            _username = config["Email:Smtp:Username"]!;
            _password = config["Email:Smtp:Password"]!;
            _fromEmail = config["Email:From:Address"]!;
            _fromName = config["Email:From:Name"];

            _reportSenderEmails = new Dictionary<string, string>
            {
                { MachineReport, config["Email:ReportSenders:Machine"]! },
                { NearmissReport, config["Email:ReportSenders:Nearmiss"]! },
                { Suggestion, config["Email:ReportSenders:Suggestion"]! },
                { KaizenReport, config["Email:ReportSenders:Kaizen"]! },
            };
        }

        public async Task SendForgotPasswordAsync(string toEmail, string resetLink)
        {
            var subject = "Reset Your Password";
            var body = $@"
                <h2>Password Reset Request</h2>
                <p>Click the link below to reset your password. This link expires in 1 hour.</p>
                <p><a href='{resetLink}'>Reset Password</a></p>
                <p>If you did not request this, please ignore this email.</p>
                ";

            await SendEmailAsync(_fromEmail, _fromName, toEmail, subject, body);
        }

        public async Task SendReportNotificationAsync(string reportType, string toEmail, object reportData)
        {
            if (!_reportSenderEmails.TryGetValue(reportType, out var senderEmails))
                throw new ArgumentException($"Unkown report type: {reportType}");

            var subject = $"New Incoming Report: {reportType}";
            var body = $@"
                <h2>Incoming Report Notification</h2>
                <p>A new <strong>{reportType}</strong> report has been submitted.</p>
                <p><strong>Details:</strong></p>
                <pre>{System.Text.Json.JsonSerializer.Serialize(reportData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}</pre>
                ";

            await SendEmailAsync(senderEmails, reportType, toEmail, subject, body);
        }
        
        private async Task SendEmailAsync(string fromAddress, string fromName, string toEmail, string subject, string htmlBody)
        {
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(fromName, fromAddress));
            email.To.Add(MailboxAddress.Parse(toEmail));
            email.Subject = subject;
            email.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_host, _port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_username, _password);
            await client.SendAsync(email);
            await client.DisconnectAsync(true);
        }

    }
}
