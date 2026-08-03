using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ReportSystem.Services;

namespace ReportSystem.ContractTests;

internal sealed class TestAppDbContextScopeFactory(AppDbContext context) : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new TestScope(context);

    private sealed class TestScope(AppDbContext context) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new TestServiceProvider(context);
        public void Dispose() { }
    }

    private sealed class TestServiceProvider(AppDbContext context) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(AppDbContext) ? context : null;
    }
}

internal sealed class TrackingStorageService : IR2StorageService
{
    private int _finalizeCalls;
    public int? FailFinalizeCall { get; init; }
    public List<string> UploadedKeys { get; } = [];
    public List<string> DeletedKeys { get; } = [];
    private readonly HashSet<string> _failNextDeleteKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _deleteAttempts = new(StringComparer.Ordinal);

    public Task<string> UploadTempAsync(
        Stream stream,
        string fileName,
        string userId,
        CancellationToken ct = default)
    {
        var key = $"temp/{userId}/temp-{UploadedKeys.Count + 1}.webp";
        UploadedKeys.Add(key);
        return Task.FromResult(key);
    }

    public Task<string> FinalizeTempAsync(
        string tempKey,
        string reportId,
        CancellationToken ct = default) =>
        Task.FromResult($"reports/{reportId}/final.webp");

    public Task<string> FinalizeFollowUpTempAsync(
        string tempKey,
        string reportId,
        CancellationToken ct = default)
    {
        _finalizeCalls++;
        if (_finalizeCalls == FailFinalizeCall)
            throw new IOException("storage detail");

        return Task.FromResult($"reports/{reportId}/followups/final-{_finalizeCalls}.webp");
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        _deleteAttempts[key] = _deleteAttempts.TryGetValue(key, out var attempts) ? attempts + 1 : 1;

        if (_failNextDeleteKeys.Remove(key))
            throw new IOException("injected delete failure");

        DeletedKeys.Add(key);
        return Task.CompletedTask;
    }

    public void FailNextDeleteFor(string key) => _failNextDeleteKeys.Add(key);

    public int DeleteAttemptsFor(string key) =>
        _deleteAttempts.TryGetValue(key, out var attempts) ? attempts : 0;

    public Task DeleteOldTempFilesAsync(TimeSpan olderThan, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));

    public string GetPublicUrl(string key) => throw new InvalidOperationException("Private uploads do not use public URLs.");
}

internal sealed class RejectingImageProcessor : IImageProcessingService
{
    public Task<ProcessedImageResult> ProcessAsync(Stream input, CancellationToken ct = default) =>
        throw new InvalidOperationException("decoder detail");
}
