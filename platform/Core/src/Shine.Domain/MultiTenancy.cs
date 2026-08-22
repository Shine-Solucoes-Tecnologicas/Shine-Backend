namespace Shine.Domain;

public interface IMultiTenantEntity
{
    Guid TenantId { get; }
}

public sealed class TenantIsolationException(string message) : DomainException(message);
