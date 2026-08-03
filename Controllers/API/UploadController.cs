using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReportSystem.Services;

namespace ReportSystem.Controllers.API;

[ApiController]
[Route("api/upload")]
public class UploadController : ControllerBase
{
    private const long MaxTempUploadBytes = 10 * 1024 * 1024;

    private readonly IFileValidationService _validator;
    private readonly IImageProcessingService _processor;
    private readonly IR2StorageService _r2;
    private readonly ILogger<UploadController> _logger;

    public UploadController(
        IFileValidationService validator,
        IImageProcessingService processor,
        IR2StorageService r2,
        ILogger<UploadController> logger)
    {
        _validator = validator;
        _processor = processor;
        _r2 = r2;
        _logger = logger;
    }

    [HttpPost("temp")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting("upload")]
    [RequestSizeLimit(MaxTempUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxTempUploadBytes)]
    public async Task<IActionResult> UploadTemp(IFormFile file, CancellationToken ct)
    {
        if (file is null)
            return BadRequest(new { error = "No file was uploaded." });

        var processingToken = CancellationToken.None;

        var validation = await _validator.ValidateAsync(file, processingToken);
        if (!validation.IsValid)
            return BadRequest(new { error = validation.Error });

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
            _logger.LogWarning(ex, "Image processing rejected upload for file {Name}", file.FileName);
            return UnprocessableEntity(new { error = "Image content could not be processed." });
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
                tempKey = await _r2.UploadTempAsync(processedStream, file.FileName, ownerToken, ct);
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
            Height: height
        ));
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
        int Height
    );

    public record DeleteTempRequest(string TempKey, string UploadOwnerToken);
}
