namespace Shine.Domain;

public sealed class ModuleAccess : AuditableEntity, IMultiTenantEntity
{
    private ModuleAccess() { }

    public ModuleAccess(Guid tenantId, string moduleCode, bool enabled = true)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        TenantId = tenantId;
        ModuleCode = new ModuleCode(moduleCode).Value;
        Enabled = enabled;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string ModuleCode { get; private set; } = null!;
    public bool Enabled { get; private set; }

    public void SetEnabled(bool enabled) => Enabled = enabled;
}
