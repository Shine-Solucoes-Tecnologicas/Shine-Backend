namespace Shine.Domain;

public sealed class FeatureFlag : AuditableEntity
{
    private FeatureFlag() { }

    public FeatureFlag(string key, bool enabled, Guid? tenantId = null)
    {
        Key = NormalizeKey(key);
        Enabled = enabled;
        TenantId = tenantId;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? TenantId { get; private set; }
    public string Key { get; private set; } = null!;
    public bool Enabled { get; private set; }

    public void SetEnabled(bool enabled) => Enabled = enabled;

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Feature flag key is required.", nameof(key));
        var normalized = key.Trim().ToUpperInvariant();
        if (normalized.Length > 120 || normalized.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new ArgumentException("Feature flag key has an invalid format.", nameof(key));
        return normalized;
    }
}
