using Microsoft.EntityFrameworkCore;
using Shine.Domain.Authorization;
using Shine.Domain.Identity;

namespace Shine.Infrastructure.Persistence.Seed;

public static class AuthorizationSeed
{
    public static readonly IReadOnlyDictionary<string, string> InitialPermissions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["tenant.read"] = "Visualizar dados da empresa",
        ["tenant.manage"] = "Gerenciar dados da empresa",
        ["users.read"] = "Visualizar usuários da empresa",
        ["users.manage"] = "Gerenciar usuários da empresa",
        ["roles.read"] = "Visualizar roles e permissões",
        ["roles.manage"] = "Gerenciar roles e permissões",
        ["modules.manage"] = "Gerenciar acesso aos módulos",
        ["scheduling.read"] = "Visualizar agenda e disponibilidade",
        ["scheduling.manage"] = "Gerenciar agenda, disponibilidade e agendamentos",
        ["business-catalog.read"] = "Visualizar profissionais e serviços",
        ["business-catalog.manage"] = "Gerenciar profissionais, serviços e seus vínculos",
        ["admin.read"] = "Consultar a plataforma administrativamente",
        ["admin.manage"] = "Gerenciar a plataforma administrativamente",
        ["admin.audit"] = "Consultar auditoria administrativa",
        ["dashboard.read"] = "Visualizar widgets e layout do dashboard",
        ["dashboard.manage"] = "Personalizar o layout do dashboard",
        ["files.read"] = "Visualizar arquivos da organização",
        ["files.manage"] = "Enviar e excluir arquivos da organização",
        ["customers.read"] = "Visualizar clientes da unidade",
        ["customers.manage"] = "Criar e gerenciar clientes da unidade",
        ["notifications.manage"] = "Criar notificações internas da organização",
        ["account.read"] = "Visualizar a organização",
        ["account.manage"] = "Gerenciar a organização",
        ["account.access.manage"] = "Gerenciar usuários, papéis e escopos da organização",
        ["billing.read"] = "Visualizar cobranças e faturas",
        ["billing.manage"] = "Gerenciar cobrança da organização",
        ["subscriptions.read"] = "Visualizar assinaturas e planos contratados",
        ["subscriptions.manage"] = "Gerenciar assinaturas e planos contratados",
        ["billing.commercial.read"] = "Consultar contratos e condições comerciais da plataforma",
        ["billing.commercial.manage"] = "Gerenciar contratos e condições comerciais da plataforma"
    };

    public static async Task SeedCustomerAccountDefaultsAsync(ShineDbContext db, Guid accountId, Guid administratorUserId, CancellationToken cancellationToken = default)
    {
        var permissions = (await EnsureInitialPermissionsAsync(db, cancellationToken)).ToDictionary(x => x.Code, StringComparer.Ordinal);
        var definitions = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [CustomerAccountRole.AdministratorName] = ["account.read", "account.manage", "account.access.manage", "billing.read", "billing.manage", "subscriptions.read", "subscriptions.manage"],
            [CustomerAccountRole.FinancialName] = ["billing.read", "billing.manage", "subscriptions.read", "subscriptions.manage"],
            [CustomerAccountRole.ViewerName] = ["tenant.read", "business-catalog.read", "scheduling.read", "dashboard.read", "files.read", "customers.read"],
            [CustomerAccountRole.EditorName] = ["tenant.read", "business-catalog.read", "business-catalog.manage", "scheduling.read", "scheduling.manage", "dashboard.read", "dashboard.manage", "files.read", "files.manage", "customers.read", "customers.manage"]
        };

        var existing = await db.CustomerAccountRoles.Where(x => x.AccountId == accountId && definitions.Keys.Contains(x.Name)).ToDictionaryAsync(x => x.Name, StringComparer.Ordinal, cancellationToken);
        foreach (var name in definitions.Keys)
            if (!existing.ContainsKey(name)) { var role = new CustomerAccountRole(accountId, name, isSystem: true); db.CustomerAccountRoles.Add(role); existing[name] = role; }
        await db.SaveChangesAsync(cancellationToken);

        foreach (var (roleName, codes) in definitions)
        foreach (var code in codes)
        {
            var role = existing[roleName]; var permission = permissions[code];
            if (!await db.CustomerAccountRolePermissions.AnyAsync(x => x.RoleId == role.Id && x.PermissionId == permission.Id, cancellationToken))
                db.CustomerAccountRolePermissions.Add(new CustomerAccountRolePermission(role.Id, permission.Id));
        }

        var administrator = existing[CustomerAccountRole.AdministratorName];
        if (!await db.CustomerAccountUserRoles.AnyAsync(x => x.AccountId == accountId && x.UserId == administratorUserId && x.RoleId == administrator.Id, cancellationToken))
            db.CustomerAccountUserRoles.Add(new CustomerAccountUserRole(accountId, administratorUserId, administrator.Id, allUnits: true, allModules: true));
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task EnsureGlobalRolesAsync(ShineDbContext db, CancellationToken cancellationToken = default)
    {
        var permissions = await EnsureInitialPermissionsAsync(db, cancellationToken);
        var permissionByCode = permissions.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var roleNames = new[] { GlobalRole.PlatformAdminName, GlobalRole.SupportName, GlobalRole.AuditorName, GlobalRole.CommercialManagerName };
        var roles = await db.GlobalRoles.Where(item => roleNames.Contains(item.Name)).ToDictionaryAsync(item => item.Name, StringComparer.Ordinal, cancellationToken);
        foreach (var name in roleNames)
        {
            if (roles.ContainsKey(name)) continue;
            var role = new GlobalRole(name);
            db.GlobalRoles.Add(role);
            roles[name] = role;
        }
        await db.SaveChangesAsync(cancellationToken);

        var assignments = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [GlobalRole.PlatformAdminName] = ["admin.read", "admin.manage", "admin.audit", "billing.commercial.read", "billing.commercial.manage"],
            [GlobalRole.SupportName] = ["admin.read", "admin.manage"],
            [GlobalRole.AuditorName] = ["admin.read", "admin.audit", "billing.commercial.read"],
            [GlobalRole.CommercialManagerName] = ["billing.commercial.read", "billing.commercial.manage"]
        };
        foreach (var (roleName, codes) in assignments)
        foreach (var code in codes)
        {
            var role = roles[roleName];
            var permission = permissionByCode[code];
            if (!await db.GlobalRolePermissions.AnyAsync(item => item.RoleId == role.Id && item.PermissionId == permission.Id, cancellationToken))
                db.GlobalRolePermissions.Add(new GlobalRolePermission(role.Id, permission.Id));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task<IReadOnlyCollection<Permission>> EnsureInitialPermissionsAsync(ShineDbContext db, CancellationToken cancellationToken = default)
    {
        var codes = InitialPermissions.Keys.ToArray();
        var existing = await db.Permissions.Where(item => codes.Contains(item.Code)).ToDictionaryAsync(item => item.Code, StringComparer.Ordinal, cancellationToken);
        var created = new List<Permission>();
        foreach (var (code, description) in InitialPermissions)
        {
            if (existing.ContainsKey(code)) continue;
            var permission = new Permission(code, description);
            db.Permissions.Add(permission);
            created.Add(permission);
        }

        if (created.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return existing.Values.Concat(created).OrderBy(item => item.Code).ToArray();
    }

    public static async Task SeedTenantDefaultsAsync(ShineDbContext db, Guid tenantId, Guid ownerUserId, Guid administratorUserId, CancellationToken cancellationToken = default)
    {
        var permissions = await EnsureInitialPermissionsAsync(db, cancellationToken);
        var owner = await EnsureOwnerRoleAsync(db, tenantId, ownerUserId, cancellationToken);
        var administrator = await EnsureAdministratorRoleAsync(db, tenantId, administratorUserId, cancellationToken);
        await EnsureMemberRoleAsync(db, tenantId, cancellationToken);
        var permissionIds = permissions.Select(item => item.Id).ToHashSet();

        var existingLinks = await db.RolePermissions
            .Where(item => (item.RoleId == owner.Id || item.RoleId == administrator.Id) && permissionIds.Contains(item.PermissionId))
            .Select(item => new { item.RoleId, item.PermissionId })
            .ToListAsync(cancellationToken);
        var existingSet = existingLinks.Select(item => (item.RoleId, item.PermissionId)).ToHashSet();
        foreach (var role in new[] { owner, administrator })
        foreach (var permission in permissions)
            if (!existingSet.Contains((role.Id, permission.Id)))
                db.RolePermissions.Add(new RolePermission(role.Id, permission.Id));

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task<Role> EnsureMemberRoleAsync(ShineDbContext db, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var role = await db.Roles.SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Name == Role.MemberName, cancellationToken);
        if (role is null)
        {
            role = new Role(tenantId, Role.MemberName, isSystem: true);
            db.Roles.Add(role);
            await db.SaveChangesAsync(cancellationToken);
        }
        return role;
    }

    public static async Task<Role> EnsureOwnerRoleAsync(ShineDbContext db, Guid tenantId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var role = await db.Roles.SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Name == Role.OwnerName, cancellationToken);
        if (role is null)
        {
            role = new Role(tenantId, Role.OwnerName, isSystem: true);
            db.Roles.Add(role);
            await db.SaveChangesAsync(cancellationToken);
        }

        var membershipExists = await db.UserTenants.AnyAsync(item => item.UserId == ownerUserId && item.TenantId == tenantId && item.IsActive, cancellationToken);
        if (!membershipExists)
            throw new InvalidOperationException("The owner must belong to the tenant before receiving the owner role.");

        var assigned = await db.UserTenantRoles.AnyAsync(item => item.UserId == ownerUserId && item.TenantId == tenantId && item.RoleId == role.Id, cancellationToken);
        if (!assigned)
        {
            db.UserTenantRoles.Add(new UserTenantRole(ownerUserId, tenantId, role.Id));
            await db.SaveChangesAsync(cancellationToken);
        }

        return role;
    }

    public static async Task<Role> EnsureAdministratorRoleAsync(ShineDbContext db, Guid tenantId, Guid administratorUserId, CancellationToken cancellationToken = default)
    {
        var role = await db.Roles.SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Name == Role.AdministratorName, cancellationToken);
        if (role is null)
        {
            role = new Role(tenantId, Role.AdministratorName, isSystem: true);
            db.Roles.Add(role);
            await db.SaveChangesAsync(cancellationToken);
        }

        var membershipExists = await db.UserTenants.AnyAsync(item => item.UserId == administratorUserId && item.TenantId == tenantId && item.IsActive, cancellationToken);
        if (!membershipExists)
            throw new InvalidOperationException("The administrator must belong to the tenant before receiving the administrator role.");

        var assigned = await db.UserTenantRoles.AnyAsync(item => item.UserId == administratorUserId && item.TenantId == tenantId && item.RoleId == role.Id, cancellationToken);
        if (!assigned)
        {
            db.UserTenantRoles.Add(new UserTenantRole(administratorUserId, tenantId, role.Id));
            await db.SaveChangesAsync(cancellationToken);
        }

        return role;
    }
}
