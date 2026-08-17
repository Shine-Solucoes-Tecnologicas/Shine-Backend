using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Shine.Api.Controllers;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Shine.UnitTests;

public sealed class FilesControllerTests
{
    [Fact]
    public void Upload_requires_file_management_permission()
    {
        var attribute = typeof(FilesController).GetMethod(nameof(FilesController.Upload))!
            .GetCustomAttributes(typeof(Shine.Api.RequiresPermissionAttribute), inherit: true)
            .Cast<Shine.Api.RequiresPermissionAttribute>()
            .Single();
        Assert.Equal("files.manage", attribute.PermissionCode);
    }

    [Fact]
    public async Task Upload_rejects_missing_file()
    {
        await using var db = new ShineDbContext(new DbContextOptionsBuilder<ShineDbContext>().Options);
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions()));
        var tenant = new TestTenant();
        var controller = new FilesController(storage, db,
            new TestUser(), tenant, new TestPermissions(), new StoredFileDeletionProcessor(db, storage, new TenantExecutionContext(tenant)));
        var result = await controller.Upload(null, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private sealed class TestUser : ICurrentUser { public Guid? UserId => Guid.NewGuid(); public bool IsAuthenticated => true; }
    private sealed class TestTenant : ICurrentTenant { public Guid? TenantId => Guid.NewGuid(); public Guid? UserTenantId => Guid.NewGuid(); public IReadOnlyCollection<string> Roles => []; public bool HasCompleteContext => true; }
    private sealed class TestPermissions : IPermissionAuthorization { public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(true); }
}
