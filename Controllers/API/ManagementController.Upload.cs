using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ReportSystem.Models;
using System.Security.Cryptography;
using System.Text;

namespace ReportSystem.Controllers.API
{
    public partial class ManagementController
    {
        private const int MaxAttachmentCount = 5;
        private const long MaxAttachmentBytes = 10 * 1024 * 1024;
        private const long MaxRequestBytes = MaxAttachmentCount * MaxAttachmentBytes;

        public class SaveFollowUpForm
        {
            public int reportId { get; set; }
            public string status { get; set; } = string.Empty;
            public string responsiblePerson { get; set; } = string.Empty;
            public string description { get; set; } = string.Empty;
            public string materialsUsed { get; set; } = string.Empty;
            public List<IFormFile>? attachments { get; set; }
        }

        [HttpPost("followup/save")]
        [Consumes("multipart/form-data")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("managementUpload")]
        [RequestSizeLimit(MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
        public async Task<IActionResult> SaveFollowUp(
            [FromForm] SaveFollowUpForm form,
            CancellationToken ct = default)
        {
            if (form == null ||
                form.reportId == 0 ||
                string.IsNullOrWhiteSpace(form.status) ||
                string.IsNullOrWhiteSpace(form.responsiblePerson) ||
                string.IsNullOrWhiteSpace(form.description))
            {
                return BadRequest(new { message = "Please fill all required fields before submitting." });
            }

            string[] allowedStatuses = ["Open", "On Progress", "Close", "Done"];
            if (!allowedStatuses.Contains(form.status))
                return BadRequest(new { message = "Invalid status value." });

            var files = form.attachments ?? [];
            if (files.Count > MaxAttachmentCount ||
                files.Any(file => file.Length == 0 || file.Length > MaxAttachmentBytes))
            {
                return BadRequest(new { message = "One or more attachments exceeds the upload limits." });
            }

            var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
            if (idempotencyKey.Length > 128)
                return BadRequest(new { message = "The idempotency key is invalid." });

            var actor = HttpContext.Session.GetString("UserId") ?? "system";
            var idempotencyClaim = string.IsNullOrWhiteSpace(idempotencyKey)
                ? null
                : CreateIdempotencyClaim(actor, form.reportId, idempotencyKey);

            if (idempotencyClaim is not null &&
                await _context.MasterFollowUps.AsNoTracking().AnyAsync(
                    followUp => followUp.IdempotencyClaim == idempotencyClaim,
                    ct))
            {
                return Ok(new { message = "Follow-up saved successfully." });
            }

            var processedUploads = new List<ProcessedUpload>(files.Count);
            var tempKeys = new List<string>(files.Count);
            var finalKeys = new List<string>(files.Count);

            try
            {
                var report = await _context.MasterReports
                    .FirstOrDefaultAsync(candidate => candidate.ReportId == form.reportId, ct);
                if (report == null)
                    return NotFound(new { message = "Report not found." });

                foreach (var file in files)
                {
                    var validation = await _fileValidator.ValidateAsync(file, ct);
                    if (!validation.IsValid)
                        return BadRequest(new { message = "One or more attachments is not a valid image." });

                    await using var input = file.OpenReadStream();
                    var processed = await _imageProcessor.ProcessAsync(input, ct);
                    processedUploads.Add(new ProcessedUpload(file.FileName, processed.Stream));
                }

                var actorId = GetCurrentUserId();

                foreach (var upload in processedUploads)
                {
                    tempKeys.Add(await _r2.UploadTempAsync(
                        upload.Stream,
                        upload.OriginalFileName,
                        actor,
                        ct));
                }

                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                try
                {
                    var now = DateTime.Now;
                    report.Status = form.status;
                    report.UpdatedDate = now;
                    report.UpdatedBy = actorId;

                    var followUp = new MasterFollowUp
                    {
                        ReportId = form.reportId,
                        FollowUpDate = now,
                        ResponsiblePerson = form.responsiblePerson.Trim(),
                        MaterialsUsed = form.materialsUsed?.Trim() ?? string.Empty,
                        Description = form.description.Trim(),
                        UpdatedBy = actorId,
                        IdempotencyClaim = idempotencyClaim
                    };
                    _context.MasterFollowUps.Add(followUp);
                    await _context.SaveChangesAsync(ct);

                    for (var index = 0; index < tempKeys.Count; index++)
                    {
                        var finalKey = await _r2.FinalizeFollowUpTempAsync(
                            tempKeys[index],
                            form.reportId.ToString(),
                            ct);
                        finalKeys.Add(finalKey);
                        var upload = processedUploads[index];

                        _context.Attachments.Add(new Attachment
                        {
                            ReportId = form.reportId,
                            FollowUpId = followUp.FollowUpId,
                            FileName = Path.GetFileName(upload.OriginalFileName),
                            FilePath = $"r2://{finalKey}",
                            FileSize = checked((int)upload.Stream.Length),
                            FileType = "image/webp",
                            UploadDate = now,
                            UploadedBy = actor
                        });
                    }

                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch (DbUpdateException) when (idempotencyClaim is not null)
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    _context.ChangeTracker.Clear();

                    if (await _context.MasterFollowUps.AsNoTracking().AnyAsync(
                            followUp => followUp.IdempotencyClaim == idempotencyClaim,
                            CancellationToken.None))
                    {
                        await CompensateStorageAsync(tempKeys, finalKeys);
                        return Ok(new { message = "Follow-up saved successfully." });
                    }

                    throw;
                }
                catch
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    throw;
                }

                return Ok(new { message = "Follow-up saved successfully." });
            }
            catch (OperationCanceledException ex)
            {
                _context.ChangeTracker.Clear();
                await CompensateStorageAsync(tempKeys, finalKeys);
                _logger.LogWarning(ex, "Management follow-up upload canceled for report {ReportId}", form.reportId);
                return BadRequest(new { message = "The upload was canceled." });
            }
            catch (InvalidOperationException ex)
            {
                _context.ChangeTracker.Clear();
                await CompensateStorageAsync(tempKeys, finalKeys);
                _logger.LogWarning(ex, "Management image processing rejected input for report {ReportId}", form.reportId);
                return UnprocessableEntity(new { message = "One or more attachments could not be processed." });
            }
            catch (Exception ex)
            {
                _context.ChangeTracker.Clear();
                await CompensateStorageAsync(tempKeys, finalKeys);
                _logger.LogError(ex, "Error saving follow-up for report {ReportId}", form.reportId);
                return StatusCode(500, new { message = "An error occurred while saving follow-up." });
            }
            finally
            {
                foreach (var upload in processedUploads)
                    await upload.Stream.DisposeAsync();

            }
        }

        private static string CreateIdempotencyClaim(string actor, int reportId, string idempotencyKey)
        {
            var input = $"{actor.Length}:{actor}:{reportId}:{idempotencyKey.Length}:{idempotencyKey}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        }

        private async Task CompensateStorageAsync(
            IReadOnlyCollection<string> tempKeys,
            IReadOnlyCollection<string> finalKeys)
        {
            foreach (var key in finalKeys.Concat(tempKeys).Distinct(StringComparer.Ordinal))
            {
                try
                {
                    await _r2.DeleteAsync(key, CancellationToken.None);
                }
                catch (Exception cleanupError)
                {
                    _logger.LogWarning(cleanupError, "Failed to compensate R2 object {ObjectKey}", key);
                    await _orphanedObjectCleanup.RecordFailedDeleteAsync(key, CancellationToken.None);
                }
            }
        }

        private sealed record ProcessedUpload(string OriginalFileName, MemoryStream Stream);

    }
}
