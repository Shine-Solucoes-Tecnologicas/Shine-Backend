namespace BusinessCatalog.Domain;

public sealed class Professional
{
    private Professional() { }

    public Professional(Guid tenantId, string name, Guid? userId = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Unit identifier is required.", nameof(tenantId));
        TenantId = tenantId;
        Name = Required(name, nameof(name));
        UserId = userId;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public void Rename(string name) => Name = Required(name, nameof(name));
    public void Update(string name, Guid? userId)
    {
        Rename(name);
        UserId = userId;
    }
    public void SetActive(bool active) => IsActive = active;

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}

public sealed class Service
{
    private Service() { }

    public Service(Guid tenantId, string name, int durationMinutes)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Unit identifier is required.", nameof(tenantId));
        TenantId = tenantId;
        Name = Required(name, nameof(name));
        SetDuration(durationMinutes);
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public int DurationMinutes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public void Rename(string name) => Name = Required(name, nameof(name));
    public void SetDuration(int durationMinutes)
    {
        if (durationMinutes <= 0 || durationMinutes > 1440) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        DurationMinutes = durationMinutes;
    }

    public void SetActive(bool active) => IsActive = active;

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}

public sealed class ProfessionalService
{
    private ProfessionalService() { }

    public ProfessionalService(Guid tenantId, Guid professionalId, Guid serviceId)
    {
        if (tenantId == Guid.Empty || professionalId == Guid.Empty || serviceId == Guid.Empty)
            throw new ArgumentException("Unit, professional and service identifiers are required.");
        TenantId = tenantId;
        ProfessionalId = professionalId;
        ServiceId = serviceId;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid ServiceId { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void SetActive(bool active) => IsActive = active;
}
