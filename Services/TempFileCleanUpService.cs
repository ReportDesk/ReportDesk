namespace ReportSystem.Services
{
    public class TempFileCleanupService : BackgroundService
    {
        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan TempFileMaxAge = TimeSpan.FromHours(2);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TempFileCleanupService> _logger;

        public TempFileCleanupService(IServiceScopeFactory scopeFactory, ILogger<TempFileCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("TempFileCleanupService started.");

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunCleanupAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during temp file cleanup.");
                }

                await Task.Delay(RunInterval, stoppingToken);
            }

            _logger.LogInformation("TempFileCleanupService stopped.");
        }

        private async Task RunCleanupAsync(CancellationToken ct)
        {
            _logger.LogInformation("Running temp file cleanup at {Time}", DateTimeOffset.UtcNow);

            using var scope = _scopeFactory.CreateScope();
            var r2 = scope.ServiceProvider.GetRequiredService<IR2StorageService>();

            await r2.DeleteOldTempFilesAsync(TempFileMaxAge, ct);
        }
    }
}
