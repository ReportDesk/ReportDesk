using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Formats;
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
        private const long MaxPixelCount = 20_000_000;
        private const int MaxInputBytes = 10 * 1024 * 1024;

        private const int WebPQuality = 82;
        private readonly ILogger<ImageProcessingService> _logger;

        public ImageProcessingService(ILogger<ImageProcessingService> logger)
        {
            _logger = logger;
        }

        public async Task<ProcessedImageResult> ProcessAsync(Stream input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            await using var bufferedInput = input.CanSeek ? null : new MemoryStream();
            var decodeStream = input;
            if (bufferedInput is not null)
            {
                await CopyToBoundedBufferAsync(input, bufferedInput, ct);
                bufferedInput.Position = 0;
                decodeStream = bufferedInput;
            }

            var initialPosition = decodeStream.Position;
            EnsureCompressedSizeIsBounded(decodeStream, initialPosition);

            var decoderOptions = new DecoderOptions
            {
                SkipMetadata = true,
                MaxFrames = 1
            };

            ImageInfo imageInfo;
            try
            {
                imageInfo = await Image.IdentifyAsync(decoderOptions, decodeStream, ct);
                EnsureResolutionIsBounded(imageInfo.Width, imageInfo.Height);
                decodeStream.Position = initialPosition;
            }
            catch (InvalidImageContentException ex)
            {
                throw new InvalidOperationException("Image content is invalid.", ex);
            }
            catch (UnknownImageFormatException ex)
            {
                throw new InvalidOperationException("Image content is invalid.", ex);
            }

            using var image = await LoadImageAsync(decoderOptions, decodeStream, ct);

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

        private static async Task<Image> LoadImageAsync(
            DecoderOptions decoderOptions,
            Stream input,
            CancellationToken ct)
        {
            try
            {
                return await Image.LoadAsync(decoderOptions, input, ct);
            }
            catch (InvalidImageContentException ex)
            {
                throw new InvalidOperationException("Image content is invalid.", ex);
            }
            catch (UnknownImageFormatException ex)
            {
                throw new InvalidOperationException("Image content is invalid.", ex);
            }
        }

        private static void EnsureResolutionIsBounded(int width, int height)
        {
            var pixelCount = checked((long)width * height);
            if (pixelCount > MaxPixelCount)
            {
                throw new InvalidOperationException(
                    $"Image resolution too large ({width}x{height}px). Maximum is {MaxPixelCount:N0} pixels.");
            }
        }

        private static void EnsureCompressedSizeIsBounded(Stream input, long initialPosition)
        {
            if (input.Length - initialPosition > MaxInputBytes)
            {
                throw new InvalidOperationException("Image file exceeds the maximum size of 10MB.");
            }
        }

        private static async Task CopyToBoundedBufferAsync(
            Stream input,
            MemoryStream destination,
            CancellationToken ct)
        {
            var buffer = new byte[81920];
            while (true)
            {
                var bytesRead = await input.ReadAsync(buffer, ct);
                if (bytesRead == 0)
                {
                    return;
                }

                if (destination.Length + bytesRead > MaxInputBytes)
                {
                    throw new InvalidOperationException("Image file exceeds the maximum size of 10MB.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            }
        }
    }
}
