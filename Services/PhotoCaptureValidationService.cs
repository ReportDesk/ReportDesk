using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ReportSystem.Services;

public interface IPhotoCaptureValidationService
{
    Task<PhotoCaptureValidationResult> ValidateAsync(
        IFormFile file,
        DateTimeOffset uploadedAt,
        CancellationToken ct = default);
}

public sealed record PhotoCaptureValidationResult(
    bool IsValid,
    string? Error = null,
    DateTimeOffset? CapturedAt = null,
    TimeSpan? PhotoAge = null,
    bool CanBeUnverified = false);

public sealed class PhotoCaptureValidationService : IPhotoCaptureValidationService
{
    private static readonly TimeSpan MaximumPhotoAge = TimeSpan.FromHours(48);
    private static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromMinutes(10);
    private static readonly TimeZoneInfo DefaultTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Jakarta");

    private readonly ILogger<PhotoCaptureValidationService> _logger;

    public PhotoCaptureValidationService(ILogger<PhotoCaptureValidationService> logger)
    {
        _logger = logger;
    }

    public async Task<PhotoCaptureValidationResult> ValidateAsync(
        IFormFile file,
        DateTimeOffset uploadedAt,
        CancellationToken ct = default)
    {
        try
        {
            await using var stream = file.OpenReadStream();
            using var image = await Image.LoadAsync(stream, ct);
            var exif = image.Metadata.ExifProfile;

            if (exif is null ||
                !exif.TryGetValue(ExifTag.DateTimeOriginal, out IExifValue<string>? capturedValue) ||
                !DateTime.TryParseExact(
                    capturedValue.Value,
                    "yyyy:MM:dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var capturedLocalTime))
            {
                return new(false,
                    "This photo does not contain a valid capture date. Please upload the original JPG photo taken within the last 48 hours.",
                    CanBeUnverified: true);
            }

            var capturedAt = ToDateTimeOffset(capturedLocalTime, exif);
            var photoAge = uploadedAt - capturedAt;

            if (photoAge < -MaximumFutureClockSkew)
            {
                return new(false,
                    "This photo's capture time is in the future. Check your device date and time, then try again.",
                    capturedAt,
                    photoAge);
            }

            if (photoAge > MaximumPhotoAge)
            {
                return new(false,
                    "This photo was taken more than 48 hours ago. Please upload a recent machine photo.",
                    capturedAt,
                    photoAge);
            }

            _logger.LogInformation(
                "Machine photo capture time validated. CapturedAt={CapturedAt}, UploadedAt={UploadedAt}, AgeMinutes={AgeMinutes}",
                capturedAt,
                uploadedAt,
                Math.Round(photoAge.TotalMinutes, 2));

            return new(true, CapturedAt: capturedAt, PhotoAge: photoAge);
        }
        catch (UnknownImageFormatException)
        {
            return new(false, "The uploaded file could not be read as a valid image.");
        }
        catch (InvalidImageContentException)
        {
            return new(false, "The uploaded image metadata is corrupted. Please upload the original photo again.");
        }
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime capturedAt, ExifProfile exif)
    {
        var localCaptureTime = DateTime.SpecifyKind(capturedAt, DateTimeKind.Unspecified);

        if (exif.TryGetValue(ExifTag.OffsetTimeOriginal, out IExifValue<string>? offsetValue) &&
            DateTimeOffset.TryParse(offsetValue.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedOffset))
        {
            return new DateTimeOffset(localCaptureTime, parsedOffset.Offset);
        }

        return new DateTimeOffset(localCaptureTime, DefaultTimeZone.GetUtcOffset(localCaptureTime));
    }
}
