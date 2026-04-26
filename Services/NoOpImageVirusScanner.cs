using Microsoft.AspNetCore.Http;

namespace ReportSystem.Services;

public sealed class NoOpImageVirusScanner : IImageVirusScanner
{
    public Task<ImageScanResult> ScanAsync(IFormFile file, CancellationToken cancellationToken)
    {
        return Task.FromResult(ImageScanResult.Clean());
    }
}
