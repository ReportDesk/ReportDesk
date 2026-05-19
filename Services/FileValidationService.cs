namespace ReportSystem.Services
{
    public interface IFileValidationService
    {
        Task<FileValidationResult> ValidateAsync(IFormFile file, CancellationToken ct = default);
    }
    
    public record FileValidationResult(bool IsValid, string? Error = null);

    public class FileValidationService : IFileValidationService
    {
        private static readonly long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png"
        };

        private static readonly List<(byte[] Signature, string MimeType)> MagicBytes = new()
        {
            (new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg"),
            (new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")
        };

        public async Task<FileValidationResult> ValidateAsync(IFormFile file, CancellationToken ct = default)
        {
            if (file.Length == 0)
            {
                return new(false, "File is empty");
            }

            if (file.Length > MaxFileSizeBytes)
            {
                return new(false, $"File exceeds maximum size of {MaxFileSizeBytes / 1024 / 1024}MB ");
            }

            var ext = Path.GetExtension(file.FileName);
            if (!AllowedExtensions.Contains(ext))
            {
                return new(false, $"File type '{ext}' is not allowed. Use JPG or PNG.");
            }

            using var stream = file.OpenReadStream();
            var header = new byte[8];
            var bytesRead = await stream.ReadAsync(header, 0, header.Length, ct);

            if (bytesRead < 3)
                return new(false, "File is too small to be a valid image.");

            var matched = MagicBytes.Any(m =>
                header.Length >= m.Signature.Length &&
                header.Take(m.Signature.Length).SequenceEqual(m.Signature));

            if (!matched)
                return new(false, "File content does not match a valid JPG or PNG image.");

            return new(true);
        }
    }
}
