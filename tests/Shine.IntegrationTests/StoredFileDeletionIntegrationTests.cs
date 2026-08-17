using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shine.Api.Controllers;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class StoredFileDeletionIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Background_batch_discovers_global_units_but_processes_each_file_in_explicit_tenant_scope()
    {
        Guid tenantId;
        await using (var setup = fixture.CreateDb())
        {
            var tenant = new Tenant($"File worker {Guid.NewGuid():N}");
            setup.Tenants.Add(tenant);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
        }
        var metadataId = await CreatePendingMetadataAsync(tenantId, "background-worker");
        var storage = new ControlledStorage { Exists = true };
        var background = new BackgroundTenant();
        var execution = new TenantExecutionContext(background);
        await using var db = fixture.CreateDb(background, execution);

        Assert.True(await new StoredFileDeletionProcessor(db, storage, execution).ProcessDueAsync(500) >= 1);
        Assert.Null(execution.EffectiveTenantId);
        Assert.False(await db.StoredFiles.AnyAsync());
        await using var verification = fixture.CreateDb();
        Assert.False(await verification.StoredFiles.AnyAsync(x => x.Id == metadataId));
    }

    [Fact]
    public async Task Database_failure_while_requesting_deletion_does_not_touch_storage()
    {
        var tenantId = Guid.NewGuid();
        var metadataId = await CreateMetadataAsync(tenantId, "request-database-failure", pending: false);
        var storage = new ControlledStorage { Exists = true };
        var tenant = new TestTenant(tenantId);
        var execution = new TenantExecutionContext(tenant);
        await using (var failingDb = fixture.CreateDbWithInterceptors(tenant, execution, new FailDeletionRequestOnceInterceptor()))
        {
            var controller = new FilesController(storage, failingDb, new TestUser(), tenant, new AllowPermissions(),
                new StoredFileDeletionProcessor(failingDb, storage, execution));
            await Assert.ThrowsAsync<InjectedDatabaseFailure>(() => controller.Delete(metadataId.ToString(), CancellationToken.None));
        }

        Assert.True(storage.Exists);
        Assert.Equal(0, storage.DeleteCalls);
        await using var verification = fixture.CreateDb(tenant);
        Assert.Equal(StoredFileDeletionStatus.Active,
            (await verification.StoredFiles.SingleAsync(x => x.Id == metadataId)).DeletionStatus);
    }

    [Fact]
    public async Task Storage_failure_leaves_pending_metadata_and_retry_completes_idempotently()
    {
        var tenantId = Guid.NewGuid();
        var metadataId = await CreatePendingMetadataAsync(tenantId, "storage-failure");
        var storage = new ControlledStorage { Exists = true, Failure = new IOException("Storage unavailable.") };
        var tenant = new TestTenant(tenantId);
        var execution = new TenantExecutionContext(tenant);
        await using var db = fixture.CreateDb(tenant, execution);
        var processor = new StoredFileDeletionProcessor(db, storage, execution);

        Assert.Equal(StoredFileDeletionResult.Pending, await processor.ProcessAsync(tenantId, metadataId));
        var pending = await db.StoredFiles.SingleAsync(x => x.Id == metadataId);
        Assert.Equal(StoredFileDeletionStatus.Pending, pending.DeletionStatus);
        Assert.Equal(1, pending.DeletionAttempts);
        Assert.True(storage.Exists);

        storage.Failure = null;
        Assert.Equal(StoredFileDeletionResult.Completed, await processor.ProcessAsync(tenantId, metadataId));
        Assert.Equal(StoredFileDeletionResult.NotFound, await processor.ProcessAsync(tenantId, metadataId));
        Assert.False(storage.Exists);
        Assert.False(await db.StoredFiles.AnyAsync(x => x.Id == metadataId));
    }

    [Fact]
    public async Task Already_missing_storage_object_is_a_successful_deletion()
    {
        var tenantId = Guid.NewGuid();
        var metadataId = await CreatePendingMetadataAsync(tenantId, "already-missing");
        var storage = new ControlledStorage { Exists = false };
        var tenant = new TestTenant(tenantId);
        var execution = new TenantExecutionContext(tenant);
        await using var db = fixture.CreateDb(tenant, execution);

        Assert.Equal(StoredFileDeletionResult.Completed,
            await new StoredFileDeletionProcessor(db, storage, execution).ProcessAsync(tenantId, metadataId));
        Assert.False(await db.StoredFiles.AnyAsync(x => x.Id == metadataId));
    }

    [Fact]
    public async Task Database_failure_after_storage_deletion_is_recovered_on_retry()
    {
        var tenantId = Guid.NewGuid();
        var metadataId = await CreatePendingMetadataAsync(tenantId, "database-failure");
        var storage = new ControlledStorage { Exists = true };
        var tenant = new TestTenant(tenantId);
        var execution = new TenantExecutionContext(tenant);
        await using (var failingDb = fixture.CreateDbWithInterceptors(tenant, execution, new FailSoftDeleteOnceInterceptor()))
        {
            await Assert.ThrowsAsync<InjectedDatabaseFailure>(() =>
                new StoredFileDeletionProcessor(failingDb, storage, execution).ProcessAsync(tenantId, metadataId));
        }
        Assert.False(storage.Exists);

        var retryTenant = new TestTenant(tenantId);
        var retryExecution = new TenantExecutionContext(retryTenant);
        await using var retryDb = fixture.CreateDb(retryTenant, retryExecution);
        var pending = await retryDb.StoredFiles.SingleAsync(x => x.Id == metadataId);
        Assert.Equal(StoredFileDeletionStatus.Pending, pending.DeletionStatus);
        Assert.Equal(StoredFileDeletionResult.Completed,
            await new StoredFileDeletionProcessor(retryDb, storage, retryExecution).ProcessAsync(tenantId, metadataId));
        Assert.False(await retryDb.StoredFiles.AnyAsync(x => x.Id == metadataId));
    }

    [Fact]
    public async Task Cross_tenant_processing_cannot_delete_pending_file()
    {
        var ownerTenantId = Guid.NewGuid();
        var metadataId = await CreatePendingMetadataAsync(ownerTenantId, "cross-tenant");
        var storage = new ControlledStorage { Exists = true };
        var otherTenant = new TestTenant(Guid.NewGuid());
        var execution = new TenantExecutionContext(otherTenant);
        await using var db = fixture.CreateDb(otherTenant, execution);

        Assert.Equal(StoredFileDeletionResult.NotFound,
            await new StoredFileDeletionProcessor(db, storage, execution).ProcessAsync(otherTenant.TenantId!.Value, metadataId));
        Assert.True(storage.Exists);
    }

    private async Task<Guid> CreatePendingMetadataAsync(Guid tenantId, string storageId)
        => await CreateMetadataAsync(tenantId, storageId, pending: true);

    private async Task<Guid> CreateMetadataAsync(Guid tenantId, string storageId, bool pending)
    {
        var tenant = new TestTenant(tenantId);
        await using var db = fixture.CreateDb(tenant);
        var uniqueStorageId = $"{storageId}-{Guid.NewGuid():N}";
        var metadata = new StoredFileMetadata(tenantId, Guid.NewGuid(), uniqueStorageId, "document.pdf", "application/pdf", 10,
            "GENERAL", "files.read", "files.manage");
        if (pending) metadata.RequestDeletion(DateTime.UtcNow);
        db.StoredFiles.Add(metadata);
        await db.SaveChangesAsync();
        return metadata.Id;
    }

    private sealed class ControlledStorage : IFileStorage
    {
        public bool Exists { get; set; }
        public Exception? Failure { get; set; }
        public int DeleteCalls { get; private set; }
        public Task<StoredFile> SaveAsync(Stream content, string originalFileName, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(Exists ? new MemoryStream() : null);
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            if (Failure is not null) return Task.FromException<bool>(Failure);
            var existed = Exists;
            Exists = false;
            return Task.FromResult(existed);
        }
    }

    private sealed class FailSoftDeleteOnceInterceptor : SaveChangesInterceptor
    {
        private bool failed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!failed && eventData.Context!.ChangeTracker.Entries<StoredFileMetadata>()
                    .Any(x => x.State == EntityState.Modified && x.Entity.IsDeleted))
            {
                failed = true;
                return ValueTask.FromException<InterceptionResult<int>>(new InjectedDatabaseFailure());
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FailDeletionRequestOnceInterceptor : SaveChangesInterceptor
    {
        private bool failed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!failed && eventData.Context!.ChangeTracker.Entries<StoredFileMetadata>()
                    .Any(x => x.State == EntityState.Modified && !x.Entity.IsDeleted &&
                        x.Entity.DeletionStatus == StoredFileDeletionStatus.Pending))
            {
                failed = true;
                return ValueTask.FromException<InterceptionResult<int>>(new InjectedDatabaseFailure());
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class InjectedDatabaseFailure : Exception;
    private sealed class TestTenant(Guid id) : ICurrentTenant
    {
        public Guid? TenantId => id;
        public Guid? UserTenantId => Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => true;
    }
    private sealed class BackgroundTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public Guid? UserTenantId => null;
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => false;
    }
    private sealed class TestUser : ICurrentUser { public Guid? UserId => Guid.NewGuid(); public bool IsAuthenticated => true; }
    private sealed class AllowPermissions : IPermissionAuthorization
    {
        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
