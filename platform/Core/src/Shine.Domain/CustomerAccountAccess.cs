namespace Shine.Domain.Identity;

public sealed class CustomerAccountUser
{
    private CustomerAccountUser() { }
    public CustomerAccountUser(Guid accountId, Guid userId)
    {
        if (accountId == Guid.Empty) throw new ArgumentException("Account is required.", nameof(accountId));
        if (userId == Guid.Empty) throw new ArgumentException("User is required.", nameof(userId));
        AccountId = accountId; UserId = userId; IsActive = true; CreatedAtUtc = DateTime.UtcNow;
    }
    public Guid AccountId { get; private set; }
    public Guid UserId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public CustomerAccount Account { get; private set; } = null!;
    public User User { get; private set; } = null!;
    public ICollection<CustomerAccountUserRole> Roles { get; private set; } = new List<CustomerAccountUserRole>();
}

public sealed class CustomerAccountRole
{
    public const string AdministratorName = "AccountAdmin";
    public const string ViewerName = "Viewer";
    public const string EditorName = "Editor";
    public const string FinancialName = "Financial";
    public const string SchedulingProfessionalName = "SchedulingProfessional";
    public const string SchedulingReceptionName = "SchedulingReception";
    public const string SchedulingManagerName = "SchedulingManager";

    private CustomerAccountRole() { }
    public CustomerAccountRole(Guid accountId, string name, bool isSystem = false)
    {
        if (accountId == Guid.Empty) throw new ArgumentException("Account is required.", nameof(accountId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Role name is required.", nameof(name));
        Id = Guid.NewGuid(); AccountId = accountId; Name = name.Trim(); IsSystem = isSystem;
    }
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsSystem { get; private set; }
    public CustomerAccount Account { get; private set; } = null!;
    public ICollection<CustomerAccountRolePermission> Permissions { get; private set; } = new List<CustomerAccountRolePermission>();
}

public sealed class CustomerAccountRolePermission
{
    private CustomerAccountRolePermission() { }
    public CustomerAccountRolePermission(Guid roleId, Guid permissionId,
        Shine.Domain.Authorization.PermissionScope scope = Shine.Domain.Authorization.PermissionScope.All)
    { RoleId = roleId; PermissionId = permissionId; Scope = scope; }
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }
    public Shine.Domain.Authorization.PermissionScope Scope { get; private set; } = Shine.Domain.Authorization.PermissionScope.All;
    public CustomerAccountRole Role { get; private set; } = null!;
    public Shine.Domain.Authorization.Permission Permission { get; private set; } = null!;
    public void SetScope(Shine.Domain.Authorization.PermissionScope scope) => Scope = scope;
}

public sealed class CustomerAccountUserRole
{
    private CustomerAccountUserRole() { }
    public CustomerAccountUserRole(Guid accountId, Guid userId, Guid roleId, bool allUnits = false, bool allModules = false)
    {
        if (accountId == Guid.Empty) throw new ArgumentException("Account is required.", nameof(accountId));
        if (userId == Guid.Empty) throw new ArgumentException("User is required.", nameof(userId));
        if (roleId == Guid.Empty) throw new ArgumentException("Role is required.", nameof(roleId));
        AccountId = accountId; UserId = userId; RoleId = roleId; AllUnits = allUnits; AllModules = allModules;
    }
    public Guid AccountId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public bool AllUnits { get; private set; }
    public bool AllModules { get; private set; }
    public CustomerAccountUser Membership { get; private set; } = null!;
    public CustomerAccountRole Role { get; private set; } = null!;
    public ICollection<CustomerAccountUserRoleUnit> Units { get; private set; } = new List<CustomerAccountUserRoleUnit>();
    public ICollection<CustomerAccountUserRoleModule> Modules { get; private set; } = new List<CustomerAccountUserRoleModule>();

    public void ReplaceScope(bool allUnits, bool allModules, IEnumerable<Guid> unitIds, IEnumerable<string> moduleCodes)
    {
        AllUnits = allUnits; AllModules = allModules; Units.Clear(); Modules.Clear();
        foreach (var unitId in unitIds.Distinct()) Units.Add(new CustomerAccountUserRoleUnit(AccountId, UserId, RoleId, unitId));
        foreach (var moduleCode in moduleCodes.Select(x => new Shine.Domain.ModuleCode(x).Value).Distinct()) Modules.Add(new CustomerAccountUserRoleModule(AccountId, UserId, RoleId, moduleCode));
    }
}

public sealed class CustomerAccountUserRoleUnit
{
    private CustomerAccountUserRoleUnit() { }
    public CustomerAccountUserRoleUnit(Guid accountId, Guid userId, Guid roleId, Guid unitId)
    {
        if (unitId == Guid.Empty) throw new ArgumentException("Unit is required.", nameof(unitId));
        AccountId = accountId; UserId = userId; RoleId = roleId; UnitId = unitId;
    }
    public Guid AccountId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid UnitId { get; private set; }
    public CustomerAccountUserRole Assignment { get; private set; } = null!;
    public Tenant Unit { get; private set; } = null!;
}

public sealed class CustomerAccountUserRoleModule
{
    private CustomerAccountUserRoleModule() { }
    public CustomerAccountUserRoleModule(Guid accountId, Guid userId, Guid roleId, string moduleCode)
    {
        AccountId = accountId; UserId = userId; RoleId = roleId; ModuleCode = new Shine.Domain.ModuleCode(moduleCode).Value;
    }
    public Guid AccountId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public string ModuleCode { get; private set; } = null!;
    public CustomerAccountUserRole Assignment { get; private set; } = null!;
}
