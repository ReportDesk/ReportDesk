using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Filters;
using ReportSystem.Models;
using ReportSystem.Services;

namespace ReportSystem.Controllers.API;

[ApiController]
[Route("api/notifications")]
[SessionAuthorize]
[Produces("application/json")]
public sealed class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebPushService _webPushService;

    public NotificationsController(AppDbContext db, IWebPushService webPushService)
    {
        _db = db;
        _webPushService = webPushService;
    }

    [HttpGet("push-public-key")]
    public IActionResult GetPushPublicKey() => _webPushService.IsConfigured
        ? Ok(new { publicKey = _webPushService.PublicKey })
        : NotFound(new { message = "Browser push notifications are not configured." });

    [HttpPost("push-subscriptions")]
    public async Task<IActionResult> SavePushSubscription([FromBody] PushSubscriptionRequest request, CancellationToken cancellationToken)
    {
        if (!_webPushService.IsConfigured)
            return BadRequest(new { message = "Browser push notifications are not configured." });
        if (string.IsNullOrWhiteSpace(request.Endpoint) || string.IsNullOrWhiteSpace(request.P256dh) || string.IsNullOrWhiteSpace(request.Auth))
            return BadRequest(new { message = "Invalid browser subscription." });

        var now = DateTime.UtcNow;
        var subscription = await _db.WebPushSubscriptions
            .FirstOrDefaultAsync(item => item.Endpoint == request.Endpoint, cancellationToken);
        if (subscription is null)
        {
            subscription = new WebPushSubscription { Endpoint = request.Endpoint, CreatedDate = now };
            _db.WebPushSubscriptions.Add(subscription);
        }

        subscription.UserId = GetCurrentUserId();
        subscription.P256dh = request.P256dh;
        subscription.Auth = request.Auth;
        subscription.UpdatedDate = now;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet]
    public async Task<IActionResult> GetLatest(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var notifications = await _db.UserNotifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedDate)
            .Take(30)
            .Select(notification => new
            {
                notification.NotificationId,
                notification.Title,
                message = notification.Message,
                notification.Priority,
                reportUrl = notification.ReportUrl,
                notification.IsRead,
                notification.AcknowledgedDate,
                notification.CreatedDate
            })
            .ToListAsync(cancellationToken);

        return Ok(new { unreadCount = notifications.Count(notification => !notification.IsRead), notifications });
    }

    [HttpPost("{notificationId:int}/read")]
    public async Task<IActionResult> MarkRead(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await FindOwnedNotificationAsync(notificationId, cancellationToken);
        if (notification is null)
            return NotFound();

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadDate = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    [HttpPost("{notificationId:int}/acknowledge")]
    public async Task<IActionResult> Acknowledge(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await FindOwnedNotificationAsync(notificationId, cancellationToken);
        if (notification is null)
            return NotFound();

        if (!notification.AcknowledgedDate.HasValue)
        {
            notification.IsRead = true;
            notification.ReadDate ??= DateTime.UtcNow;
            notification.AcknowledgedDate = DateTime.UtcNow;
            notification.AcknowledgedBy = GetCurrentUserId();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new { notificationId, notification.AcknowledgedDate });
    }

    private Task<ReportSystem.Models.UserNotification?> FindOwnedNotificationAsync(int notificationId, CancellationToken cancellationToken) =>
        _db.UserNotifications.FirstOrDefaultAsync(
            notification => notification.NotificationId == notificationId && notification.UserId == GetCurrentUserId(),
            cancellationToken);

    private int GetCurrentUserId() => int.TryParse(HttpContext.Session.GetString("UserId"), out var userId) ? userId : 0;

    public sealed class PushSubscriptionRequest
    {
        public string Endpoint { get; init; } = string.Empty;
        public string P256dh { get; init; } = string.Empty;
        public string Auth { get; init; } = string.Empty;
    }
}
