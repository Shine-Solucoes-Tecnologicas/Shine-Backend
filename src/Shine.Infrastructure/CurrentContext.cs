using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Shine.Infrastructure;

public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
}

public interface ICurrentTenant
{
    Guid? TenantId { get; }
    Guid? UserTenantId { get; }
    IReadOnlyCollection<string> Roles { get; }
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId => ReadGuid(accessor.HttpContext?.User, "user_id") ?? ReadGuid(accessor.HttpContext?.User, ClaimTypes.NameIdentifier);
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true && UserId is not null;

    private static Guid? ReadGuid(ClaimsPrincipal? principal, string claimType) =>
        Guid.TryParse(principal?.FindFirst(claimType)?.Value, out var value) ? value : null;
}

public sealed class CurrentTenant(IHttpContextAccessor accessor) : ICurrentTenant
{
    public Guid? TenantId => ReadGuid("tenant_id");
    public Guid? UserTenantId => ReadGuid("user_tenant_id");
    public IReadOnlyCollection<string> Roles => accessor.HttpContext?.User.FindAll("role").Select(claim => claim.Value).ToArray() ?? [];

    private Guid? ReadGuid(string claimType) =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(claimType)?.Value, out var value) ? value : null;
}
