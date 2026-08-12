namespace Shine.Domain;

public static class CoreContractValidation
{
    public static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The value must be expressed in UTC.", parameterName);
        return value;
    }

    public static void EnsureTenant(Guid tenantId, string parameterName = "tenantId")
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant is required.", parameterName);
    }

    public static void EnsureEntity(Guid entityId, string parameterName = "entityId")
    {
        if (entityId == Guid.Empty)
            throw new ArgumentException("Entity is required.", parameterName);
    }
}
