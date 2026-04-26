namespace ReportSystem.Services;

public sealed record ImageScanResult(bool IsClean, string? Message)
{
    public static ImageScanResult Clean() => new(true, null);
    public static ImageScanResult Rejected(string message) => new(false, message);
}
