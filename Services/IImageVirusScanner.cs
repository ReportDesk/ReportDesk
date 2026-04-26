using Microsoft.AspNetCore.Http;

namespace ReportSystem.Services;

public interface IImageVirusScanner
{
    Task<ImageScanResult> ScanAsync(IFormFile file, CancellationToken cancellationToken);
}
