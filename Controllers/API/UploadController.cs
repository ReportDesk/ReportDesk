using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ReportSystem.Services;

namespace ReportSystem.Controllers.API;

[ApiController]
[Route("api/upload")]
public class UploadController : ControllerBase
{
    private readonly IFileValidationService _validator;
    private readonly IPhotoCaptureValidationService _photoCaptureValidator;
    private readonly IMachinePhotoCaptureSessionService _machinePhotoCaptureSessions;
    private readonly PhotoValidationOptions _photoValidationOptions;
    private readonly IImageProcessingService _processor;
    private readonly IR2StorageService _r2;
    private readonly ILogger<UploadController> _logger;

    public UploadController(
        IFileValidationService validator,
        IPhotoCaptureValidationService photoCaptureValidator,
        IMachinePhotoCaptureSessionService machinePhotoCaptureSessions,
        IOptions<PhotoValidationOptions> photoValidationOptions,
        IImageProcessingService processor,
        IR2StorageService r2,
        ILogger<UploadController> logger)
    {
        _validator = validator;
        _photoCaptureValidator = photoCaptureValidator;
        _machinePhotoCaptureSessions = machinePhotoCaptureSessions;
        _photoValidationOptions = photoValidationOptions.Value;
        _processor = processor;
        _r2 = r2;
        _logger = logger;
    }

    [HttpPost("temp")]
    [EnableRateLimiting("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadTemp(
        IFormFile file,
        [FromForm] string? purpose,
        [FromForm] string? captureSessionToken,
        CancellationToken ct)
    {
        if (file is null)
            return BadRequest(new { error = "No file was uploaded." });

        var processingToken = CancellationToken.None;

        var validation = await _validator.ValidateAsync(file, processingToken);
        if (!validation.IsValid)
            return BadRequest(new { error = validation.Error });

        var isMachinePhoto = string.Equals(purpose, "machine-report", StringComparison.Ordinal);
        var isReportSubmissionPhoto = isMachinePhoto ||
                                      string.Equals(purpose, "report-submission", StringComparison.Ordinal) ||
                                      string.Equals(purpose, "report-follow-up", StringComparison.Ordinal);
        var isValidatedReportPhoto = false;
        string? photoValidationMethod = null;
        DateTimeOffset? photoCapturedAt = null;

        if (isReportSubmissionPhoto)
        {
            if (!string.IsNullOrWhiteSpace(captureSessionToken))
            {
                if (!_machinePhotoCaptureSessions.TryConsume(captureSessionToken))
                    return BadRequest(new { error = "The camera capture session expired. Open the camera and take the photo again." });

                isValidatedReportPhoto = true;
                photoValidationMethod = "in-app-camera";
                photoCapturedAt = DateTimeOffset.UtcNow;
                _logger.LogInformation("Machine photo validated with an in-app camera session at {UploadedAt}", DateTimeOffset.UtcNow);
            }
            else
            {
                var captureValidation = await _photoCaptureValidator.ValidateAsync(file, DateTimeOffset.UtcNow, processingToken);
                if (!captureValidation.IsValid)
                {
                    if (!_photoValidationOptions.AllowUnverifiedUploads || !captureValidation.CanBeUnverified)
                        return BadRequest(new { error = captureValidation.Error });

                    photoValidationMethod = "unverified";
                    _logger.LogWarning("Report photo accepted as unverified because development mode permits missing capture metadata.");
                }
                else
                {
                    isValidatedReportPhoto = true;
                    photoValidationMethod = "exif";
                    photoCapturedAt = captureValidation.CapturedAt;
                }
            }
        }

        var ownerToken = Guid.NewGuid().ToString("N");

        MemoryStream processedStream;
        int width;
        int height;
        long fileSizeBytes;

        try
        {
            using var inputStream = file.OpenReadStream();
            var processedImage = await _processor.ProcessAsync(inputStream, processingToken);
            processedStream = processedImage.Stream;
            width = processedImage.Width;
            height = processedImage.Height;
            fileSizeBytes = processedStream.Length;
            processedStream.Position = 0;
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (OperationCanceledException)
        {
            return BadRequest(new { error = "Upload was canceled before processing completed." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image processing failed for file {Name}", file.FileName);
            return StatusCode(500, new { error = "Image processing failed." });
        }

        string tempKey;
        try
        {
            await using (processedStream)
            {
                tempKey = await _r2.UploadTempAsync(
                    processedStream,
                    file.FileName,
                    ownerToken,
                    isValidatedReportPhoto,
                    photoValidationMethod,
                    photoCapturedAt,
                    ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "R2 upload failed for owner token {OwnerToken}", ownerToken);
            return StatusCode(500, new { error = "Storage upload failed." });
        }

        return Ok(new TempUploadResponse(
            TempKey: tempKey,
            UploadOwnerToken: ownerToken,
            PreviewUrl: _r2.GetPublicUrl(tempKey),
            FileSizeBytes: fileSizeBytes,
            Width: width,
            Height: height,
            PhotoValidationStatus: photoValidationMethod == "unverified"
                ? "unverified"
                : isReportSubmissionPhoto ? "verified" : "not-applicable"
        ));
    }

    [HttpPost("capture-session")]
    [EnableRateLimiting("upload")]
    public IActionResult CreateMachinePhotoCaptureSession()
    {
        return Ok(_machinePhotoCaptureSessions.Create());
    }

    [HttpDelete("temp")]
    public async Task<IActionResult> DeleteTemp([FromBody] DeleteTempRequest request, CancellationToken ct)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.TempKey) ||
            string.IsNullOrWhiteSpace(request.UploadOwnerToken) ||
            !request.TempKey.StartsWith("temp/", StringComparison.Ordinal))
        {
            return BadRequest(new { error = "Invalid temp upload request." });
        }

        if (!request.TempKey.StartsWith($"temp/{request.UploadOwnerToken}/", StringComparison.Ordinal))
            return Forbid();

        try
        {
            await _r2.DeleteAsync(request.TempKey, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete temp key {Key}", request.TempKey);
            return StatusCode(500, new { error = "Could not delete file." });
        }
    }

    public record TempUploadResponse(
        string TempKey,
        string UploadOwnerToken,
        string PreviewUrl,
        long FileSizeBytes,
        int Width,
        int Height,
        string PhotoValidationStatus
    );

    public record DeleteTempRequest(string TempKey, string UploadOwnerToken);
}
