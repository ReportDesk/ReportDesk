using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using static Microsoft.IO.RecyclableMemoryStreamManager;

namespace ReportSystem.Services
{
    public interface IR2StorageService
    {
        Task<string> UploadTempAsync(Stream stream, string fileName, string userId, CancellationToken ct = default);
        Task<string> FinalizeTempAsync(string tempKey, string reportId, CancellationToken ct = default);
        Task<string> FinalizeFollowUpTempAsync(string tempKey, string reportId, CancellationToken ct = default);
        Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);
        Task DeleteAsync(string key, CancellationToken ct = default);
        Task DeleteOldTempFilesAsync(TimeSpan olderThan, CancellationToken ct = default);
        string GetPublicUrl(string key);
    }

    public class R2StorageService : IR2StorageService
    {
        private readonly IAmazonS3 _s3;
        private readonly R2Options _options;
        private readonly ILogger<R2StorageService> _logger;
        private readonly IOrphanedObjectCleanupService _orphanedObjectCleanup;

        public R2StorageService(
            IAmazonS3 s3,
            IOptions<R2Options> options,
            ILogger<R2StorageService> logger,
            IOrphanedObjectCleanupService orphanedObjectCleanup)
        {
            _s3 = s3;
            _options = options.Value;
            _logger = logger;
            _orphanedObjectCleanup = orphanedObjectCleanup;
        }

        public async Task<string> UploadTempAsync(Stream stream, string fileName, string userId, CancellationToken ct = default)
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
                    ["original-name"] = Path.GetFileName(fileName)
                }
            };

            await _s3.PutObjectAsync(request, ct);
            _logger.LogInformation("Temp upload success: {key}", key);
            return key;
        }

        public async Task<string> FinalizeTempAsync(string tempKey, string reportId, CancellationToken ct = default)
        {
            var finalKey = $"reports/{reportId}/{Guid.NewGuid():N}.webp";
            return await FinalizeTempAsAsync(tempKey, finalKey, ct);
        }

        public async Task<string> FinalizeFollowUpTempAsync(
            string tempKey,
            string reportId,
            CancellationToken ct = default)
        {
            var finalKey = $"reports/{reportId}/followups/{Guid.NewGuid():N}.webp";
            return await FinalizeTempAsAsync(tempKey, finalKey, ct);
        }

        private async Task<string> FinalizeTempAsAsync(
            string tempKey,
            string finalKey,
            CancellationToken ct)
        {
            if (!tempKey.StartsWith("temp/", StringComparison.Ordinal))
                throw new InvalidOperationException("Only temporary objects can be finalized.");

            await _s3.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = _options.BucketName,
                SourceKey = tempKey,
                DestinationBucket = _options.BucketName,
                DestinationKey = finalKey
            }, ct);

            try
            {
                await DeleteAsync(tempKey, ct);
            }
            catch
            {
                try
                {
                    await DeleteAsync(finalKey, CancellationToken.None);
                }
                catch (Exception cleanupError)
                {
                    _logger.LogError(cleanupError, "Failed to remove copied R2 object {FinalKey}", finalKey);
                    await _orphanedObjectCleanup.RecordFailedDeleteAsync(finalKey, CancellationToken.None);
                }

                throw;
            }

            _logger.LogInformation("Finalized {TempKey} to {FinalKey}", tempKey, finalKey);
            return finalKey;
        }

        public async Task DeleteAsync(string key, CancellationToken ct = default)
        {
            await _s3.DeleteObjectAsync(_options.BucketName, key, ct);
            _logger.LogInformation("Deleted R2 object: {Key}", key);
        }

        public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
        {
            using var response = await _s3.GetObjectAsync(_options.BucketName, key, ct);
            var stream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(stream, ct);
            stream.Position = 0;
            return stream;
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
