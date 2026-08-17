using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shine.Api.Controllers;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class FileAuthorizationIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task File_metadata_hides_cross_tenant_access_and_enforces_record_permission()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var root = Path.Combine(Path.GetTempPath(), "shine-file-auth", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = root }));
            Guid fileId;
            await using (var dbA = fixture.CreateDb(new TestTenant(tenantA)))
            {
                var tenant = new TestTenant(tenantA);
                var controller = new FilesController(storage, dbA, new TestUser(userA), tenant, new TestPermissions(true),
                    new StoredFileDeletionProcessor(dbA, storage, new TenantExecutionContext(tenant)));
                await using var stream = new MemoryStream("tenant-a"u8.ToArray());
                var file = new FormFile(stream, 0, stream.Length, "file", "document.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
                var created = Assert.IsType<CreatedAtActionResult>((await controller.Upload(file, default)).Result);
                fileId = Assert.IsType<FileUploadResponse>(created.Value).Id;
            }

            await using (var dbB = fixture.CreateDb(new TestTenant(tenantB)))
            {
                var tenant = new TestTenant(tenantB);
                var crossTenant = new FilesController(storage, dbB, new TestUser(Guid.NewGuid()), tenant, new TestPermissions(true),
                    new StoredFileDeletionProcessor(dbB, storage, new TenantExecutionContext(tenant)));
                Assert.IsType<NotFoundResult>(await crossTenant.Download(fileId.ToString(), default));
            }

            await using (var dbA = fixture.CreateDb(new TestTenant(tenantA)))
            {
                var tenant = new TestTenant(tenantA);
                var denied = new FilesController(storage, dbA, new TestUser(Guid.NewGuid()), tenant, new TestPermissions(false),
                    new StoredFileDeletionProcessor(dbA, storage, new TenantExecutionContext(tenant)));
                Assert.IsType<ForbidResult>(await denied.Download(fileId.ToString(), default));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestUser(Guid id) : ICurrentUser { public Guid? UserId => id; public bool IsAuthenticated => true; }
    private sealed class TestTenant(Guid id) : ICurrentTenant { public Guid? TenantId => id; public Guid? UserTenantId => Guid.NewGuid(); public IReadOnlyCollection<string> Roles => []; public bool HasCompleteContext => true; }
    private sealed class TestPermissions(bool allowed) : IPermissionAuthorization { public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(allowed); }
}
