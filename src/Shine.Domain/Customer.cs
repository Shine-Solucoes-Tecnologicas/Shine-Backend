using System.Net.Mail;

namespace Shine.Domain;

/// <summary>An operational customer served by a single tenant (business unit).</summary>
public sealed class Customer : AuditableEntity, IMultiTenantEntity
{
    private Customer() { }

    public Customer(Guid tenantId, string name, string? email = null, string? phone = null, string? taxIdentifier = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        SetContactDetails(name, email, phone, taxIdentifier);
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? TaxIdentifier { get; private set; }
    public bool IsActive => !IsDeleted;

    public void Update(string name, string? email = null, string? phone = null, string? taxIdentifier = null) =>
        SetContactDetails(name, email, phone, taxIdentifier);

    public void Deactivate(DateTime nowUtc) => Delete(nowUtc);

    public void Reactivate() => Restore();

    private void SetContactDetails(string name, string? email, string? phone, string? taxIdentifier)
    {
        Name = Required(name, nameof(name), 200);
        NormalizedName = Name.ToUpperInvariant();
        Email = NormalizeEmail(email);
        Phone = DigitsOnly(phone, nameof(phone), 20);
        TaxIdentifier = NormalizeTaxIdentifier(taxIdentifier);
    }

    private static string Required(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Customer name is required.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentException($"Customer name cannot exceed {maxLength} characters.", parameterName);
        return normalized;
    }

    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 320 || !MailAddress.TryCreate(normalized, out var address) ||
            !address.Address.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Customer email is invalid.", nameof(value));
        return normalized;
    }

    private static string? DigitsOnly(string? value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(char.IsDigit).ToArray());
        if (normalized.Length == 0 || normalized.Length > maxLength)
            throw new ArgumentException("Customer phone is invalid.", parameterName);
        return normalized;
    }

    private static string? NormalizeTaxIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(char.IsDigit).ToArray());
        if (normalized.Length is not (11 or 14))
            throw new ArgumentException("Customer tax identifier must be a CPF or CNPJ.", nameof(value));
        return normalized;
    }
}
