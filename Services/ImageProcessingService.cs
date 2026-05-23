using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace ReportSystem.Services
{
    public interface IImageProcessingService
    {
        Task<ProcessedImageResult> ProcessAsync(Stream input, CancellationToken ct = default);
    }

    public record ProcessedImageResult(MemoryStream Stream, int Width, int Height);

    public class ImageProcessingService : IImageProcessingService
    {
        private const int MaxWidthPx = 4096;
        private const int MaxHeightPx = 4096;
        private const int MaxMegaPixels = 20;

        private const int WebPQuality = 82;
        private readonly ILogger<ImageProcessingService> _logger;

        public ImageProcessingService(ILogger<ImageProcessingService> logger)
        {
            _logger = logger;
        }

        public async Task<ProcessedImageResult> ProcessAsync(Stream input, CancellationToken ct = default)
        {
            using var image = await Image.LoadAsync(input, ct);

            long megapixels = (long)image.Width * image.Height / 1_000_000;
            if (megapixels > MaxMegaPixels)
            {
                throw new InvalidOperationException($"Image resolution too large ({megapixels}MP). Maximum is {MaxMegaPixels}MP.");
            }

            _logger.LogDebug("Processing image {W}x{H}px", image.Width, image.Height);

            if (image.Width > MaxWidthPx || image.Height > MaxHeightPx)
            {
                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(MaxWidthPx, MaxHeightPx),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3
                }));

                _logger.LogDebug("Resized to {W}x{H}px", image.Width, image.Height);
            }

            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            var output = new MemoryStream();
            var encoder = new WebpEncoder
            {
                Quality = WebPQuality,
                Method = WebpEncodingMethod.Default,
                FileFormat = WebpFileFormatType.Lossy
            };

            await image.SaveAsWebpAsync(output, encoder, ct);
            output.Position = 0;

            _logger.LogDebug("Output size: {Bytes:N0} bytes", output.Length);
            return new ProcessedImageResult(output, image.Width, image.Height);
        }
    }
}
