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

        public async Task<string> UploadTempAsync(Stream stream, string fileName, string userId, CancellationToken ct = default)
        {
            var key = $"temp/{userId}/{Guid.NewGuid():N}.webp";

            var request = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key,
                InputStream = stream,
                ContentType = "image/webp",
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
