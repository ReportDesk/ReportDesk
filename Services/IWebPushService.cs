using ReportSystem.Models;

namespace ReportSystem.Services;

public interface IWebPushService
{
    bool IsConfigured { get; }
    string PublicKey { get; }
    Task SendAsync(WebPushSubscription subscription, UserNotification notification, CancellationToken cancellationToken);
}
