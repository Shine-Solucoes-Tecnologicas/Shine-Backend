using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Shine.Infrastructure;

namespace Shine.Api;

public sealed class PermissionRequirement(string permissionCode) : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute : AuthorizeAttribute
{
    public const string Prefix = "permission:";
    public string PermissionCode { get; }
    public RequiresPermissionAttribute(string permissionCode)
    {
        PermissionCode = permissionCode;
        Policy = $"{Prefix}{PermissionCode}";
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresGlobalPermissionAttribute : AuthorizeAttribute
{
    public const string Prefix = "global-permission:";
    public string PermissionCode { get; }
    public RequiresGlobalPermissionAttribute(string permissionCode)
    {
        PermissionCode = permissionCode;
        Policy = $"{Prefix}{PermissionCode}";
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresModuleAttribute : AuthorizeAttribute
{
    public const string Prefix = "module:";
    public string ModuleCode { get; }
    public RequiresModuleAttribute(string moduleCode)
    {
        ModuleCode = moduleCode;
        Policy = $"{Prefix}{ModuleCode}";
    }
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider fallback = new(options);
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => fallback.GetFallbackPolicyAsync();
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(RequiresPermissionAttribute.Prefix, StringComparison.Ordinal))
        {
            var permission = policyName[RequiresPermissionAttribute.Prefix.Length..];
            var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)).Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        if (policyName.StartsWith(RequiresGlobalPermissionAttribute.Prefix, StringComparison.Ordinal))
        {
            var permission = policyName[RequiresGlobalPermissionAttribute.Prefix.Length..];
            var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new GlobalPermissionRequirement(permission)).Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        if (policyName.StartsWith(RequiresModuleAttribute.Prefix, StringComparison.Ordinal))
        {
            var module = policyName[RequiresModuleAttribute.Prefix.Length..];
            var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new ModuleRequirement(module)).Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        return fallback.GetPolicyAsync(policyName);
    }
}

public sealed class GlobalPermissionRequirement(string permissionCode) : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}

public sealed class GlobalPermissionHandler(ICurrentUser currentUser, IPermissionAuthorization permissions) : AuthorizationHandler<GlobalPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, GlobalPermissionRequirement requirement)
    {
        if (currentUser.UserId is Guid userId && await permissions.HasGlobalPermissionAsync(userId, requirement.PermissionCode))
            context.Succeed(requirement);
    }
}

public sealed class ModuleRequirement(string moduleCode) : IAuthorizationRequirement
{
    public string ModuleCode { get; } = moduleCode;
}

public sealed class ModuleHandler(ICurrentTenant currentTenant, IModuleAccess moduleAccess) : AuthorizationHandler<ModuleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ModuleRequirement requirement)
    {
        if (currentTenant.TenantId is Guid tenantId && await moduleAccess.HasAccessAsync(tenantId, requirement.ModuleCode)) context.Succeed(requirement);
    }
}

public sealed class PermissionHandler(ICurrentUser currentUser, ICurrentTenant currentTenant, IPermissionAuthorization permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (currentUser.UserId is Guid userId && currentTenant.TenantId is Guid tenantId && currentTenant.HasCompleteContext &&
            await permissions.HasPermissionAsync(userId, tenantId, requirement.PermissionCode)) context.Succeed(requirement);
    }
}
