using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using static Microsoft.IO.RecyclableMemoryStreamManager;

namespace ReportSystem.Services
{
    public interface IR2StorageService
    {
        Task<string> UploadTempAsync(
            Stream stream,
            string fileName,
            string userId,
            bool isValidatedReportPhoto = false,
            string? photoValidationMethod = null,
            DateTimeOffset? photoCapturedAt = null,
            CancellationToken ct = default);
        Task<bool> IsValidatedReportPhotoAsync(string tempKey, CancellationToken ct = default);
        Task<bool> IsUnverifiedReportPhotoAsync(string tempKey, CancellationToken ct = default);
        Task<PhotoValidationMetadata?> GetPhotoValidationAsync(string key, CancellationToken ct = default);
        Task<string> FinalizeTempAsync(string tempKey, string reportId, CancellationToken ct = default);
        Task DeleteAsync(string key, CancellationToken ct = default);
        Task DeleteOldTempFilesAsync(TimeSpan olderThan, CancellationToken ct = default);
        string GetPublicUrl(string key);
    }

    public class R2StorageService : IR2StorageService
    {
        private readonly IAmazonS3 _s3;
        private readonly R2Options _options;
        private readonly ILogger<R2StorageService> _logger;

        public R2StorageService(IAmazonS3 s3, IOptions<R2Options> options, ILogger<R2StorageService> logger )
        {
            _s3 = s3;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<string> UploadTempAsync(
            Stream stream,
            string fileName,
            string userId,
            bool isValidatedReportPhoto = false,
            string? photoValidationMethod = null,
            DateTimeOffset? photoCapturedAt = null,
            CancellationToken ct = default)
        {
            var key = $"temp/{userId}/{Guid.NewGuid():N}.webp";

            if (!stream.CanSeek)
                throw new InvalidOperationException("Upload stream must support seeking.");

            stream.Position = 0;

            var request = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key,
                InputStream = stream,
                AutoCloseStream = false,
                ContentType = "image/webp",
                UseChunkEncoding = false,
                DisablePayloadSigning = true,
                Metadata =
                {
                    ["uploaded-at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                    ["original-name"] = Path.GetFileName(fileName),
                    ["report-photo-validated"] = isValidatedReportPhoto.ToString().ToLowerInvariant(),
                    ["photo-validation-method"] = photoValidationMethod ?? string.Empty,
                    ["photo-captured-at"] = photoCapturedAt?.ToUnixTimeSeconds().ToString() ?? string.Empty
                }
            };

            await _s3.PutObjectAsync(request, ct);
            _logger.LogInformation("Temp upload success: {key}", key);
            return key;
        }

        public async Task<bool> IsValidatedReportPhotoAsync(string tempKey, CancellationToken ct = default)
        {
            try
            {
                var response = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _options.BucketName,
                    Key = tempKey
                }, ct);

                return string.Equals(
                    response.Metadata["report-photo-validated"],
                    "true",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogWarning(ex, "Could not verify report photo validation for temp key {TempKey}", tempKey);
                return false;
            }
        }

        public async Task<bool> IsUnverifiedReportPhotoAsync(string tempKey, CancellationToken ct = default)
        {
            try
            {
                var response = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _options.BucketName,
                    Key = tempKey
                }, ct);

                return string.Equals(
                    response.Metadata["photo-validation-method"],
                    "unverified",
                    StringComparison.Ordinal);
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogWarning(ex, "Could not read report photo validation metadata for {Key}", tempKey);
                return false;
            }
        }

        public async Task<PhotoValidationMetadata?> GetPhotoValidationAsync(string key, CancellationToken ct = default)
        {
            try
            {
                var response = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _options.BucketName,
                    Key = key
                }, ct);

                var method = response.Metadata["photo-validation-method"];
                if (string.IsNullOrWhiteSpace(method))
                    return null;

                DateTimeOffset? capturedAt = null;
                if (long.TryParse(response.Metadata["photo-captured-at"], out var capturedAtSeconds))
                    capturedAt = DateTimeOffset.FromUnixTimeSeconds(capturedAtSeconds);

                return new PhotoValidationMetadata(
                    IsValidated: string.Equals(response.Metadata["report-photo-validated"], "true", StringComparison.OrdinalIgnoreCase),
                    Method: method,
                    CapturedAt: capturedAt);
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogWarning(ex, "Could not read report photo validation metadata for {Key}", key);
                return null;
            }
        }

        public async Task<string> FinalizeTempAsync(string tempKey, string reportId, CancellationToken ct = default)
        {
            if (!tempKey.StartsWith("temp/"))
                throw new InvalidOperationException($"Key is not a temp file: {tempKey}");

            var finalKey = $"reports/{reportId}/{Guid.NewGuid():N}.webp";

            var copyRequest = new CopyObjectRequest
            {
                SourceBucket = _options.BucketName,
                SourceKey = tempKey,
                DestinationBucket = _options.BucketName,
                DestinationKey = finalKey
            };

            await _s3.CopyObjectAsync(copyRequest, ct);
            await DeleteAsync(tempKey, ct);

            _logger.LogInformation("Finalized {TempKey} -> {FinalKey}", tempKey, finalKey);
            return finalKey;
        }

        public async Task DeleteAsync(string key, CancellationToken ct = default)
        {
            await _s3.DeleteObjectAsync(_options.BucketName, key, ct);
            _logger.LogInformation("Deleted R2 object: {Key}", key);
        }

        public async Task DeleteOldTempFilesAsync(TimeSpan olderThan, CancellationToken ct = default)
        {
            var cutoff = DateTimeOffset.UtcNow - olderThan;

            var listRequest = new ListObjectsV2Request
            {
                BucketName = _options.BucketName,
                Prefix = "temp/"
            };

            int totalDeleted = 0;

            ListObjectsV2Response response;
            do
            {
                response = await _s3.ListObjectsV2Async(listRequest, ct);

                var toDelete = response.S3Objects
                    .Where(o => o.LastModified < cutoff.UtcDateTime)
                    .Select(o => new KeyVersion { Key = o.Key })
                    .ToList();

                if (toDelete.Count > 0)
                {
                    await _s3.DeleteObjectsAsync(new DeleteObjectsRequest
                    {
                        BucketName = _options.BucketName,
                        Objects = toDelete
                    }, ct);

                    totalDeleted += toDelete.Count;
                }

                listRequest.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated == true);

            _logger.LogInformation("Cleanup job removed {Count} stale temp files", totalDeleted);
        }

        public string GetPublicUrl(string key) => $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}";
    }
}

public sealed record PhotoValidationMetadata(bool IsValidated, string Method, DateTimeOffset? CapturedAt);
