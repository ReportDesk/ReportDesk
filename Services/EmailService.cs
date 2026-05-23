using Microsoft.Extensions.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Net;

namespace ReportSystem.Services
{
    public interface IEmailService
    {
        Task SendForgotPasswordAsync(string toEmail, string resetLink);
        Task SendReportNotificationAsync(string reportType, string toEmail, ReportNotificationData reportData, string? reportDetailUrl = null);
    }

    public sealed class ReportNotificationData
    {
        public string ReportedBy { get; set; } = string.Empty;
        public DateTime SubmissionDate { get; set; }
        public string Area { get; set; } = string.Empty;
        public string About { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Description2 { get; set; } = string.Empty;
    }

    public class EmailService : IEmailService
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;
        private readonly string _fromEmail;
        private readonly string _fromName;

        // Report type constants
        public const string MachineReport = "Machine";
        public const string NearmissReport = "Nearmiss";
        public const string Suggestion = "Suggestion";
        public const string KaizenReport = "Kaizen";

        public EmailService(IConfiguration config)
        {
            _host = config["Email:Smtp:Host"]!;
            _port = int.Parse(config["Email:Smtp:Port"]!);
            _username = config["Email:Smtp:Username"]!;
            _password = config["Email:Smtp:Password"]!;
            _fromEmail = config["Email:From:Address"]!;
            _fromName = config["Email:From:Name"] ?? "Report Desk";
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

        public async Task SendReportNotificationAsync(string reportType, string toEmail, ReportNotificationData reportData, string? reportDetailUrl = null)
        {
            var normalizedReportType = NormalizeReportType(reportType);
            if (normalizedReportType == null)
                throw new ArgumentException($"Unknown report type: {reportType}");

            var subject = $"New Incoming Report: {normalizedReportType}";
            var detailLink = string.IsNullOrWhiteSpace(reportDetailUrl)
                ? string.Empty
                : $@"<p><a href='{reportDetailUrl}'>Open Report Detail</a></p>";
            var submittedDataRows = BuildSubmittedDataRows(normalizedReportType, reportData);

            var body = $@"
                <h2>Incoming Report Notification</h2>
                <p>A new <strong>{normalizedReportType}</strong> report has been submitted.</p>
                <table cellpadding='6' cellspacing='0' style='border-collapse:collapse;'>
                    {submittedDataRows}
                </table>
                {detailLink}
                ";

            await SendEmailAsync(_fromEmail, _fromName, toEmail, subject, body);
        }

        private static string BuildSubmittedDataRows(string reportType, ReportNotificationData reportData)
        {
            var rows = new List<string>
            {
                BuildRow("Pelapor", reportData.ReportedBy)
            };

            if (reportType == MachineReport)
                rows.Add(BuildRow("Name Mesin", reportData.MachineName));

            if (reportType == NearmissReport || reportType == KaizenReport)
                rows.Add(BuildRow("Area", reportData.Area));

            if (reportType == Suggestion)
                rows.Add(BuildRow("Perihal", reportData.About));

            if (reportType == KaizenReport)
            {
                rows.Add(BuildRow("Kondisi Sebelum", reportData.Description));
                rows.Add(BuildRow("Kondisi Sesudah", reportData.Description2));
            }
            else if (reportType == MachineReport)
            {
                rows.Add(BuildRow("Kondisi Mesin", reportData.Description));
            }
            else if (reportType == Suggestion)
            {
                rows.Add(BuildRow("Saran", reportData.Description));
            }
            else if (reportType == NearmissReport)
            {
                rows.Add(BuildRow("Kondisi Berbahaya", reportData.Description));
            }

                rows.Add(BuildRow("Submission Date", reportData.SubmissionDate.ToString("dd MMM yyyy, HH:mm")));
            return string.Join(Environment.NewLine, rows);
        }

        private static string BuildRow(string label, string? value)
        {
            var safeLabel = WebUtility.HtmlEncode(label);
            var safeValue = WebUtility.HtmlEncode(value ?? string.Empty);

            return $@"
                    <tr>
                        <td style='font-weight:bold; vertical-align:top;'>{safeLabel}</td>
                        <td style='vertical-align:top;'>{safeValue}</td>
                    </tr>";
        }

        private static string? NormalizeReportType(string reportType)
        {
            return reportType.Trim().ToLowerInvariant() switch
            {
                "machine" => MachineReport,
                "nearmiss" => NearmissReport,
                "suggestion" => Suggestion,
                "kaizen" => KaizenReport,
                _ => null
            };
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
