using Microsoft.Extensions.Options;
using ReportSystem.Models;
using System.Text.Json;
using WebPush;

namespace ReportSystem.Services;

public sealed class WebPushService : IWebPushService
{
    private readonly WebPushOptions _options;
    private readonly ILogger<WebPushService> _logger;

    public WebPushService(IOptions<WebPushOptions> options, ILogger<WebPushService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;
    public string PublicKey => _options.PublicKey;

    public async Task SendAsync(WebPushSubscription subscription, UserNotification notification, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return;

        try
        {
            var pushSubscription = new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);
            var vapid = new VapidDetails(_options.Subject, _options.PublicKey, _options.PrivateKey);
            var payload = JsonSerializer.Serialize(new
            {
                notification.Title,
                message = notification.Message,
                notification.Priority,
                reportUrl = notification.ReportUrl
            });

            await new WebPushClient().SendNotificationAsync(pushSubscription, payload, vapid);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to send web push notification to subscription {SubscriptionId}.", subscription.SubscriptionId);
        }
    }
}
