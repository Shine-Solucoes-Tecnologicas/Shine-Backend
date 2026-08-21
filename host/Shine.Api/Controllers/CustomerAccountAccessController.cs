using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/account/access")]
public sealed class CustomerAccountAccessController(
    ShineDbContext db,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICustomerAccountAuthorization authorization,
    IModuleCatalog modules) : ControllerBase
{
    private const string ManagePermission = "account.access.manage";

    [HttpGet("roles")]
    public async Task<IActionResult> Roles(CancellationToken cancellationToken)
    {
        var context = await RequireManagementContextAsync(cancellationToken);
        if (context is null) return Forbid();

        var roles = await db.CustomerAccountRoles.AsNoTracking()
            .Where(x => x.AccountId == context.Value.AccountId)
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.IsSystem,
                Permissions = x.Permissions.Select(p => p.Permission.Code).OrderBy(code => code).ToArray()
            })
            .ToArrayAsync(cancellationToken);
        return Ok(roles);
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken)
    {
        var context = await RequireManagementContextAsync(cancellationToken);
        if (context is null) return Forbid();

        var users = await db.CustomerAccountUsers.AsNoTracking()
            .Where(x => x.AccountId == context.Value.AccountId)
            .OrderBy(x => x.User.Email)
            .Select(x => new
            {
                x.UserId,
                x.User.Email,
                x.IsActive,
                Roles = x.Roles.Select(role => new
                {
                    role.RoleId,
                    role.Role.Name,
                    role.AllUnits,
                    role.AllModules,
                    Units = role.Units.Select(unit => unit.UnitId).ToArray(),
                    Modules = role.Modules.Select(module => module.ModuleCode).ToArray()
                }).ToArray()
            })
            .ToArrayAsync(cancellationToken);
        return Ok(users);
    }

    [HttpPost("users")]
    public async Task<IActionResult> AddUser(AddCustomerAccountUserRequest request, CancellationToken cancellationToken)
    {
        var context = await RequireManagementContextAsync(cancellationToken);
        if (context is null) return Forbid();

        var normalizedEmail = string.IsNullOrWhiteSpace(request.Email) ? null : Shine.Domain.Identity.User.NormalizeEmail(request.Email);
        if (normalizedEmail is null)
            return Error(StatusCodes.Status400BadRequest, "EMAIL_REQUIRED", "Informe o e-mail do usuário.");

        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null)
            return Error(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "Usuário não encontrado.");
        if (!user.IsActive)
            return Error(StatusCodes.Status409Conflict, "USER_INACTIVE", "O usuário informado está inativo.");
        if (await db.UserGlobalRoles.AnyAsync(x => x.UserId == user.Id, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "PLATFORM_USER_NOT_ALLOWED", "Usuários internos da plataforma não podem operar a organização do cliente.");
        if (await db.CustomerAccountUsers.AnyAsync(x => x.AccountId == context.Value.AccountId && x.UserId == user.Id, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "USER_ALREADY_IN_ORGANIZATION", "O usuário já pertence a esta organização.");

        db.CustomerAccountUsers.Add(new CustomerAccountUser(context.Value.AccountId, user.Id));
        AddAudit(context.Value, user.Id, "USER_ADDED", new { userId = user.Id });
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/account/access/users/{user.Id}", new { user.Id, user.Email });
    }

    [HttpPut("users/{userId:guid}/roles/{roleId:guid}")]
    public async Task<IActionResult> Assign(Guid userId, Guid roleId, AssignCustomerRoleRequest request, CancellationToken cancellationToken)
    {
        var context = await RequireManagementContextAsync(cancellationToken);
        if (context is null) return Forbid();

        var scope = NormalizeScope(request);
        if (scope.Error is not null) return scope.Error;

        var accountId = context.Value.AccountId;
        if (!await db.CustomerAccountUsers.AnyAsync(x => x.AccountId == accountId && x.UserId == userId && x.IsActive, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "ORGANIZATION_USER_NOT_FOUND", "Usuário não encontrado nesta organização.");
        if (await db.UserGlobalRoles.AnyAsync(x => x.UserId == userId, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "PLATFORM_USER_NOT_ALLOWED", "Usuários internos da plataforma não podem operar a organização do cliente.");
        if (!await db.CustomerAccountRoles.AnyAsync(x => x.Id == roleId && x.AccountId == accountId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "ROLE_NOT_FOUND", "Papel não encontrado nesta organização.");

        var unitIds = scope.UnitIds!;
        var moduleCodes = scope.ModuleCodes!;
        if (!request.AllUnits && await db.Tenants.CountAsync(x => unitIds.Contains(x.Id) && x.CustomerAccountId == accountId, cancellationToken) != unitIds.Count)
            return Error(StatusCodes.Status400BadRequest, "UNIT_OUT_OF_SCOPE", "Uma ou mais unidades não pertencem à organização.");
        if (!await CanManageScopeAsync(context.Value.UserId, accountId, request.AllUnits, request.AllModules, unitIds, moduleCodes, cancellationToken))
            return Error(StatusCodes.Status403Forbidden, "SCOPE_ESCALATION_NOT_ALLOWED", "Não é permitido conceder um escopo maior que o seu.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await db.CustomerAccountUserRoles
            .Include(x => x.Units)
            .Include(x => x.Modules)
            .SingleOrDefaultAsync(x => x.AccountId == accountId && x.UserId == userId && x.RoleId == roleId, cancellationToken);
        if (existing is null)
        {
            existing = new CustomerAccountUserRole(accountId, userId, roleId);
            db.CustomerAccountUserRoles.Add(existing);
        }

        existing.ReplaceScope(request.AllUnits, request.AllModules, unitIds, moduleCodes);
        await db.SaveChangesAsync(cancellationToken);
        await SynchronizeUnitMembershipsAsync(accountId, userId, context.Value.UserId, cancellationToken);
        AddAudit(context.Value, userId, "ROLE_ASSIGNED", new { userId, roleId, request.AllUnits, request.AllModules, unitIds, moduleCodes });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("users/{userId:guid}/roles/{roleId:guid}")]
    public async Task<IActionResult> Revoke(Guid userId, Guid roleId, CancellationToken cancellationToken)
    {
        var context = await RequireManagementContextAsync(cancellationToken);
        if (context is null) return Forbid();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var assignment = await db.CustomerAccountUserRoles
            .Include(x => x.Role)
            .Include(x => x.Units)
            .Include(x => x.Modules)
            .SingleOrDefaultAsync(x => x.AccountId == context.Value.AccountId && x.UserId == userId && x.RoleId == roleId, cancellationToken);
        if (assignment is null)
            return Error(StatusCodes.Status404NotFound, "ROLE_ASSIGNMENT_NOT_FOUND", "A atribuição de papel não foi encontrada.");

        if (!await CanManageScopeAsync(context.Value.UserId, context.Value.AccountId, assignment.AllUnits, assignment.AllModules,
                assignment.Units.Select(x => x.UnitId).ToArray(), assignment.Modules.Select(x => x.ModuleCode).ToArray(), cancellationToken))
            return Error(StatusCodes.Status403Forbidden, "SCOPE_ESCALATION_NOT_ALLOWED", "Não é permitido revogar um escopo maior que o seu.");

        if (assignment.Role.Name == CustomerAccountRole.AdministratorName)
        {
            var administratorCount = await db.CustomerAccountUserRoles.CountAsync(x =>
                x.AccountId == context.Value.AccountId && x.Role.Name == CustomerAccountRole.AdministratorName && x.Membership.IsActive, cancellationToken);
            if (administratorCount <= 1)
                return Error(StatusCodes.Status409Conflict, "LAST_ADMINISTRATOR_REQUIRED", "A organização deve manter ao menos um administrador.");
        }

        db.CustomerAccountUserRoles.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
        await SynchronizeUnitMembershipsAsync(context.Value.AccountId, userId, context.Value.UserId, cancellationToken);
        AddAudit(context.Value, userId, "ROLE_REVOKED", new { userId, roleId });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private (IReadOnlyCollection<Guid>? UnitIds, IReadOnlyCollection<string>? ModuleCodes, IActionResult? Error) NormalizeScope(AssignCustomerRoleRequest request)
    {
        var requestedUnits = request.UnitIds ?? [];
        var requestedModules = request.ModuleCodes ?? [];
        if ((!request.AllUnits && requestedUnits.Count == 0) || (!request.AllModules && requestedModules.Count == 0))
            return (null, null, Error(StatusCodes.Status400BadRequest, "SCOPE_REQUIRED", "Informe as unidades e os módulos do escopo."));
        if ((request.AllUnits && requestedUnits.Count > 0) || (request.AllModules && requestedModules.Count > 0))
            return (null, null, Error(StatusCodes.Status400BadRequest, "AMBIGUOUS_SCOPE", "Não combine escopo total com itens específicos."));

        try
        {
            var unitIds = requestedUnits.Distinct().ToArray();
            var moduleCodes = requestedModules.Select(x => new ModuleCode(x).Value).Distinct().ToArray();
            if (!request.AllModules && moduleCodes.Any(code => modules.Modules.All(module => module.Code.Value != code)))
                return (null, null, Error(StatusCodes.Status400BadRequest, "MODULE_NOT_REGISTERED", "Um ou mais módulos não estão registrados."));
            return (unitIds, moduleCodes, null);
        }
        catch (ArgumentException)
        {
            return (null, null, Error(StatusCodes.Status400BadRequest, "INVALID_MODULE_CODE", "Um ou mais códigos de módulo são inválidos."));
        }
    }

    private async Task<bool> CanManageScopeAsync(Guid actorUserId, Guid accountId, bool allUnits, bool allModules,
        IReadOnlyCollection<Guid> unitIds, IReadOnlyCollection<string> moduleCodes, CancellationToken cancellationToken)
    {
        var grants = await db.CustomerAccountUserRoles.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.UserId == actorUserId && x.Membership.IsActive &&
                x.Role.Permissions.Any(permission => permission.Permission.Code == ManagePermission))
            .Include(x => x.Units)
            .Include(x => x.Modules)
            .ToArrayAsync(cancellationToken);
        if (grants.Length == 0) return false;
        if (allUnits && allModules) return grants.Any(x => x.AllUnits && x.AllModules);
        if (allUnits) return moduleCodes.All(module => grants.Any(x => x.AllUnits && (x.AllModules || x.Modules.Any(m => m.ModuleCode == module))));
        if (allModules) return unitIds.All(unit => grants.Any(x => x.AllModules && (x.AllUnits || x.Units.Any(u => u.UnitId == unit))));
        return unitIds.All(unit => moduleCodes.All(module => grants.Any(x =>
            (x.AllUnits || x.Units.Any(u => u.UnitId == unit)) &&
            (x.AllModules || x.Modules.Any(m => m.ModuleCode == module)))));
    }

    private async Task SynchronizeUnitMembershipsAsync(Guid accountId, Guid userId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var organizationUnitIds = await db.Tenants.Where(x => x.CustomerAccountId == accountId).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var assignments = await db.CustomerAccountUserRoles.Where(x => x.AccountId == accountId && x.UserId == userId)
            .Include(x => x.Units).ToArrayAsync(cancellationToken);
        var desiredUnitIds = assignments.Any(x => x.AllUnits)
            ? organizationUnitIds.ToHashSet()
            : assignments.SelectMany(x => x.Units).Select(x => x.UnitId).ToHashSet();
        var memberships = await db.UserTenants.Where(x => x.UserId == userId && organizationUnitIds.Contains(x.TenantId)).ToArrayAsync(cancellationToken);

        foreach (var membership in memberships)
        {
            if (desiredUnitIds.Contains(membership.TenantId)) membership.Reactivate();
            else membership.Deactivate();
        }
        var existingUnitIds = memberships.Select(x => x.TenantId).ToHashSet();
        foreach (var unitId in desiredUnitIds.Where(x => !existingUnitIds.Contains(x)))
            db.UserTenants.Add(new UserTenant(userId, unitId, actorUserId, isOwner: false));
    }

    private void AddAudit((Guid AccountId, Guid UserId, Guid UnitId) context, Guid targetUserId, string action, object values)
    {
        db.AuditEntries.Add(AuditEntry.Create(
            "CustomerAccountAccess", $"{context.AccountId}/{targetUserId}", action, context.UserId, context.UnitId, DateTime.UtcNow,
            HttpContext?.TraceIdentifier, HttpContext?.Connection.RemoteIpAddress?.ToString(), HttpContext?.Request.Headers.UserAgent.ToString(),
            newValuesJson: JsonSerializer.Serialize(values)));
    }

    private async Task<(Guid AccountId, Guid UserId, Guid UnitId)?> RequireManagementContextAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId || currentTenant.TenantId is not Guid unitId) return null;
        var accountId = await db.Tenants.AsNoTracking().Where(x => x.Id == unitId).Select(x => x.CustomerAccountId).SingleOrDefaultAsync(cancellationToken);
        if (accountId is not Guid value || !await authorization.HasPermissionAsync(userId, value, ManagePermission, unitId, cancellationToken: cancellationToken))
            return null;
        return (value, userId, unitId);
    }

    private ObjectResult Error(int statusCode, string code, string message) => StatusCode(statusCode, new AccountAccessError(code, message));
}

public sealed record AddCustomerAccountUserRequest(string Email);
public sealed record AssignCustomerRoleRequest(bool AllUnits, bool AllModules, IReadOnlyCollection<Guid>? UnitIds, IReadOnlyCollection<string>? ModuleCodes);
public sealed record AccountAccessError(string Code, string Message);
