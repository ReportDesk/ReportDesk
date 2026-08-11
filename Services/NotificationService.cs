using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Helpers;
using ReportSystem.Hubs;
using ReportSystem.Models;

namespace ReportSystem.Services;

public sealed class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<NotificationService> _logger;
    private readonly IWebPushService _webPushService;

    public NotificationService(
        AppDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        IHubContext<NotificationHub> hubContext,
        ILogger<NotificationService> logger,
        IWebPushService webPushService)
    {
        _db = db;
        _dataProtectionProvider = dataProtectionProvider;
        _hubContext = hubContext;
        _logger = logger;
        _webPushService = webPushService;
    }

    public async Task NotifyNewReportAsync(MasterReport report, CancellationToken cancellationToken)
    {
        var recipientRole = GetNotificationRole(report.ReportType);
        if (recipientRole is null)
            return;

        var recipients = await _db.MasterUsers
            .AsNoTracking()
            .Where(user => user.IsActive == 1 && (user.Role == recipientRole || user.Role == "Admin"))
            .Select(user => user.UserId)
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
        {
            _logger.LogWarning("No active {Role} recipient exists for report {ReportId}.", recipientRole, report.ReportId);
            return;
        }

        var detailId = ReportIdProtector.Protect(_dataProtectionProvider, report.ReportId);
        var reportUrl = $"/Admin/ReportDetail/{Uri.EscapeDataString(report.ReportType)}/detail/{Uri.EscapeDataString(detailId)}";
        var isUrgent = string.Equals(report.Priority, "Critical", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(report.Priority, "High", StringComparison.OrdinalIgnoreCase);
        var title = isUrgent
            ? $"URGENT: {report.ReportType} report #{report.ReportId}"
            : $"New {report.ReportType} report #{report.ReportId}";
        var message = $"{report.ReportedBy} reported an issue in {report.Area}.";
        var now = DateTime.UtcNow;

        var notifications = recipients.Select(userId => new UserNotification
        {
            UserId = userId,
            ReportId = report.ReportId,
            Title = title,
            Message = message,
            Priority = report.Priority,
            ReportUrl = reportUrl,
            CreatedDate = now
        }).ToList();

        await _db.UserNotifications.AddRangeAsync(notifications, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            await _hubContext.Clients.Group($"user:{notification.UserId}")
                .SendAsync("NotificationReceived", ToClientNotification(notification), cancellationToken);

            if (_webPushService.IsConfigured)
            {
                var subscriptions = await _db.WebPushSubscriptions
                    .Where(subscription => subscription.UserId == notification.UserId)
                    .ToListAsync(cancellationToken);
                foreach (var subscription in subscriptions)
                    await _webPushService.SendAsync(subscription, notification, cancellationToken);
            }
        }
    }

    private static object ToClientNotification(UserNotification notification) => new
    {
        notificationId = notification.NotificationId,
        title = notification.Title,
        message = notification.Message,
        priority = notification.Priority,
        reportUrl = notification.ReportUrl,
        createdDate = notification.CreatedDate
    };

    private static string? GetNotificationRole(string reportType) => reportType.Trim().ToLowerInvariant() switch
    {
        "machine" => "Machine",
        "nearmiss" => "Nearmiss",
        "suggestion" => "Suggestion",
        "kaizen" => "Kaizen",
        _ => null
    };
}
