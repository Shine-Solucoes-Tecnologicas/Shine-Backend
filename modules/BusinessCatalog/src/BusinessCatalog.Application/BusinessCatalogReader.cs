namespace BusinessCatalog.Application;

public sealed record ProfessionalCatalogEntry(Guid Id, string Name, bool IsActive);

public sealed record ServiceCatalogEntry(
    Guid Id,
    string Name,
    bool IsActive,
    int DurationMinutes);

public interface IBusinessCatalogReader
{
    Task<ProfessionalCatalogEntry?> FindProfessionalAsync(Guid tenantId, Guid professionalId, CancellationToken cancellationToken = default);
    Task<ProfessionalCatalogEntry?> FindProfessionalByUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceCatalogEntry?> FindServiceAsync(Guid tenantId, Guid serviceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ProfessionalCatalogEntry>> ListActiveProfessionalsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ProfessionalCatalogEntry>> FindProfessionalsAsync(Guid tenantId, IReadOnlyCollection<Guid> professionalIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ServiceCatalogEntry>> FindServicesAsync(Guid tenantId, IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken = default);
    Task<bool> IsActiveAssociationAsync(Guid tenantId, Guid professionalId, Guid serviceId, CancellationToken cancellationToken = default);
}
