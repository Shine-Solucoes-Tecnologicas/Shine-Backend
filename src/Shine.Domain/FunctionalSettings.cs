namespace Shine.Domain;

public sealed class FunctionalSetting : AuditableEntity
{
    private FunctionalSetting() { }

    public FunctionalSetting(string key, string value, Guid? tenantId = null)
    {
        Key = NormalizeKey(key);
        Value = ValidateValue(value);
        TenantId = tenantId;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? TenantId { get; private set; }
    public string Key { get; private set; } = null!;
    public string Value { get; private set; } = null!;

    public void UpdateValue(string value) => Value = ValidateValue(value);

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Setting key is required.", nameof(key));
        var normalized = key.Trim().ToUpperInvariant();
        if (normalized.Length > 120 || normalized.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new ArgumentException("Setting key has an invalid format.", nameof(key));
        if (normalized.Contains("SECRET") || normalized.Contains("PASSWORD") || normalized.Contains("TOKEN") ||
            normalized.Contains("PRIVATE_KEY") || normalized.Contains("CONNECTION_STRING"))
            throw new ArgumentException("Technical secrets cannot be stored as functional settings.", nameof(key));
        return normalized;
    }

    private static string ValidateValue(string value)
    {
        if (value is null || value.Length > 4000) throw new ArgumentException("Setting value is invalid.", nameof(value));
        return value;
    }
}
