using ReportSystem.Models;

namespace ReportSystem.Services;

public interface INotificationService
{
    Task NotifyNewReportAsync(MasterReport report, CancellationToken cancellationToken);
}
