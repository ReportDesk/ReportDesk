using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReportSystem.Models;

namespace ReportSystem.Services;

public interface IOrphanedObjectCleanupService
{
    Task RecordFailedDeleteAsync(string objectKey, CancellationToken ct = default);
    Task RetryPendingDeletesAsync(IR2StorageService storage, CancellationToken ct = default);
}

public sealed class OrphanedObjectCleanupService : IOrphanedObjectCleanupService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrphanedObjectCleanupService> _logger;

    public OrphanedObjectCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<OrphanedObjectCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RecordFailedDeleteAsync(string objectKey, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RecordFailedDeleteAsync(context, objectKey, ct);
    }

    public async Task RetryPendingDeletesAsync(IR2StorageService storage, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RetryPendingDeletesAsync(context, storage, ct);
    }

    private async Task RecordFailedDeleteAsync(
        AppDbContext context,
        string objectKey,
        CancellationToken ct)
    {
        try
        {
            if (await context.PendingR2ObjectDeletions.AnyAsync(
                    candidate => candidate.ObjectKey == objectKey,
                    ct))
            {
                return;
            }

            context.PendingR2ObjectDeletions.Add(new PendingR2ObjectDeletion
            {
                ObjectKey = objectKey,
                RecordedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            context.ChangeTracker.Clear();
            _logger.LogError(ex, "Failed to persist cleanup eligibility for R2 object {ObjectKey}", objectKey);
        }
    }

    private async Task RetryPendingDeletesAsync(
        AppDbContext context,
        IR2StorageService storage,
        CancellationToken ct)
    {
        List<PendingR2ObjectDeletion> candidates;
        try
        {
            candidates = await context.PendingR2ObjectDeletions
                .OrderBy(candidate => candidate.PendingR2ObjectDeletionId)
                .ToListAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load R2 cleanup eligibility manifest.");
            return;
        }

        foreach (var candidate in candidates)
        {
            candidate.AttemptCount++;
            candidate.LastAttemptAt = DateTime.UtcNow;

            try
            {
                await storage.DeleteAsync(candidate.ObjectKey, ct);
                context.PendingR2ObjectDeletions.Remove(candidate);
                await context.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retry cleanup failed for R2 object {ObjectKey}", candidate.ObjectKey);

                try
                {
                    await context.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception persistenceError)
                {
                    context.ChangeTracker.Clear();
                    _logger.LogError(
                        persistenceError,
                        "Failed to update cleanup eligibility for R2 object {ObjectKey}",
                        candidate.ObjectKey);
                }
            }
        }
    }
}
