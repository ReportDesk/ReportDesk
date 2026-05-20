using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReportSystem.Services;
using SixLabors.ImageSharp;

namespace YourProject.Controllers;

[ApiController]
[Route("api/upload")]
[Authorize]
public class UploadController : ControllerBase
{
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

    // ── POST /api/upload/temp ─────────────────────────────────────────────────
    /// <summary>
    /// Called immediately when the user selects a photo.
    /// Validates → processes (resize + WebP + strip EXIF) → uploads to R2 temp/.
    /// Returns a tempKey the client holds until form submission.
    /// </summary>
    [HttpPost("temp")]
    [EnableRateLimiting("upload")]          // Defined in Program.cs
    [RequestSizeLimit(10 * 1024 * 1024)]    // 10 MB hard limit at infrastructure level
    public async Task<IActionResult> UploadTemp(IFormFile file, CancellationToken ct)
    {
        // 1. Validate
        var validation = await _validator.ValidateAsync(file, ct);
        if (!validation.IsValid)
            return BadRequest(new { error = validation.Error });

        var userId = User.FindFirst("sub")?.Value ?? "anonymous";

        // 2. Process (resize + WebP + strip EXIF)
        MemoryStream processedStream;
        int width, height;

        try
        {
            using var inputStream = file.OpenReadStream();
            processedStream = await _processor.ProcessAsync(inputStream, ct);

            processedStream.Position = 0;
            var info = await Image.IdentifyAsync(processedStream, ct);
            (width, height) = (info.Width, info.Height);
            processedStream.Position = 0;
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image processing failed for file {Name}", file.FileName);
            return StatusCode(500, new { error = "Image processing failed." });
        }

        // 3. Upload to R2 temp/
        string tempKey;
        try
        {
            await using (processedStream)
            {
                tempKey = await _r2.UploadTempAsync(processedStream, file.FileName, userId, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "R2 upload failed");
            return StatusCode(500, new { error = "Storage upload failed." });
        }

        return Ok(new TempUploadResponse(
            TempKey: tempKey,
            PreviewUrl: _r2.GetPublicUrl(tempKey),
            FileSizeBytes: processedStream.Length,
            Width: width,
            Height: height
        ));
    }

    // ── DELETE /api/upload/temp ───────────────────────────────────────────────
    /// <summary>
    /// Called when user clicks X on an image preview before submitting.
    /// Immediately removes the temp file from R2.
    /// </summary>
    [HttpDelete("temp")]
    public async Task<IActionResult> DeleteTemp([FromBody] DeleteTempRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TempKey) || !request.TempKey.StartsWith("temp/"))
            return BadRequest(new { error = "Invalid temp key." });

        var userId = User.FindFirst("sub")?.Value ?? "anonymous";
        if (!request.TempKey.StartsWith($"temp/{userId}/"))
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
        string PreviewUrl,
        long FileSizeBytes,
        int Width,
        int Height
    );

    public record DeleteTempRequest(string TempKey);
}