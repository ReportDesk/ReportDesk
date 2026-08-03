using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReportSystem.Services;
using Xunit;

namespace ReportSystem.ContractTests;

public sealed class AttachmentDeliveryCompatibilityServiceTests
{
    [Fact]
    public void HistoricalFollowUpPath_IsPassedThroughAndEmitsStructuredLegacyUsage()
    {
        var logger = new CapturingLogger<AttachmentDeliveryCompatibilityService>();
        var service = CreateService(logger: logger);

        var result = service.Resolve(17, "/uploads/followups/historical-photo.webp");

        Assert.Equal(AttachmentStorageLocation.LegacyLocalFollowUp, result.Location);
        Assert.Equal("/uploads/followups/historical-photo.webp", result.DeliveryPath);
        Assert.Null(result.PrivateObjectKey);
        Assert.Contains(logger.Messages, message =>
            message.Level == LogLevel.Warning &&
            message.Text.Contains("Legacy follow-up attachment path resolved", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("r2://reports/42/followups/4f884ce0.webp")]
    [InlineData("private:///reports/42/followups/4f884ce0.webp")]
    public void PrivateReference_UsesControlledDeliveryPathWithoutExposingObjectKey(string storedPath)
    {
        var service = CreateService();

        var result = service.Resolve(42, storedPath);

        Assert.Equal(AttachmentStorageLocation.PrivateObject, result.Location);
        Assert.Equal("/api/attachments/42", result.DeliveryPath);
        Assert.Equal("reports/42/followups/4f884ce0.webp", result.PrivateObjectKey);
        Assert.DoesNotContain("reports/42", result.DeliveryPath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/uploads/followups/../../appsettings.json")]
    [InlineData("/uploads/followups/legacy.png?download=1")]
    [InlineData("r2://reports/42/../../secret.webp")]
    [InlineData("private://reports/42/followups/image.webp?signature=leak")]
    public void UnsafeLegacyOrPrivateReference_IsNotDeliverable(string storedPath)
    {
        var service = CreateService();

        var result = service.Resolve(42, storedPath);

        Assert.Equal(AttachmentStorageLocation.Invalid, result.Location);
        Assert.False(result.IsDeliverable);
        Assert.Null(result.DeliveryPath);
    }

    [Fact]
    public void ExistingPublicReference_RemainsUntouchedForUnrelatedUploadFlows()
    {
        var service = CreateService();

        var result = service.Resolve(42, "https://legacy.example.test/reports/42/photo.webp");

        Assert.Equal(AttachmentStorageLocation.ExistingPublic, result.Location);
        Assert.Equal("https://legacy.example.test/reports/42/photo.webp", result.DeliveryPath);
    }

    [Fact]
    public void DisabledLegacyCompatibility_StopsResolvingLegacyPath()
    {
        var service = CreateService(new AttachmentDeliveryCompatibilityOptions
        {
            EnableLegacyFollowUpReads = false
        });

        var result = service.Resolve(17, "/uploads/followups/historical-photo.webp");

        Assert.Equal(AttachmentStorageLocation.Invalid, result.Location);
    }

    private static AttachmentDeliveryCompatibilityService CreateService(
        AttachmentDeliveryCompatibilityOptions? options = null,
        CapturingLogger<AttachmentDeliveryCompatibilityService>? logger = null) =>
        new(
            Options.Create(options ?? new AttachmentDeliveryCompatibilityOptions()),
            logger ?? new CapturingLogger<AttachmentDeliveryCompatibilityService>());

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Text)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }
}
