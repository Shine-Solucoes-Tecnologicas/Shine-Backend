using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public enum StoredFileDeletionResult { Completed, Pending, NotFound }

public sealed class StoredFileDeletionProcessor(
    ShineDbContext db,
    IFileStorage storage,
    ITenantExecutionContext tenantExecutionContext)
{
    public async Task<StoredFileDeletionResult> ProcessAsync(Guid tenantId, Guid metadataId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantExecutionContext.EnterTenant(tenantId);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"stored-file-delete:{metadataId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        var metadata = await db.StoredFiles.SingleOrDefaultAsync(
            x => x.Id == metadataId && x.DeletionStatus == StoredFileDeletionStatus.Pending,
            cancellationToken);
        if (metadata is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return StoredFileDeletionResult.NotFound;
        }

        try
        {
            // A missing object means a previous attempt already completed the external side.
            await storage.DeleteAsync(metadata.StorageId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var pending = await db.StoredFiles.SingleOrDefaultAsync(x => x.Id == metadataId, CancellationToken.None);
            if (pending is not null)
            {
                pending.RecordDeletionFailure(DateTime.UtcNow, exception.Message);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            return StoredFileDeletionResult.Pending;
        }

        db.StoredFiles.Remove(metadata);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return StoredFileDeletionResult.Completed;
    }

    public async Task<int> ProcessDueAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        var now = DateTime.UtcNow;
        var completed = 0;
        var tenantIds = await db.Tenants.AsNoTracking().Select(x => x.Id).ToArrayAsync(cancellationToken);
        foreach (var tenantId in tenantIds)
        {
            if (completed >= limit) break;
            using var tenantScope = tenantExecutionContext.EnterTenant(tenantId);
            var dueIds = await db.StoredFiles.AsNoTracking()
                .Where(x => x.DeletionStatus == StoredFileDeletionStatus.Pending &&
                    (x.NextDeletionAttemptAtUtc == null || x.NextDeletionAttemptAtUtc <= now))
                .OrderBy(x => x.DeletionRequestedAtUtc)
                .Select(x => x.Id)
                .Take(limit - completed)
                .ToArrayAsync(cancellationToken);
            foreach (var metadataId in dueIds)
                if (await ProcessAsync(tenantId, metadataId, cancellationToken) == StoredFileDeletionResult.Completed)
                    completed++;
        }
        return completed;
    }
}
