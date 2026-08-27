using Microsoft.EntityFrameworkCore;
using BusinessCatalog.Application;

namespace BusinessCatalog.Infrastructure;

public sealed class BusinessCatalogReader(BusinessCatalogDbContext db) : IBusinessCatalogReader
{
    public Task<ProfessionalCatalogEntry?> FindProfessionalAsync(Guid tenantId, Guid professionalId, CancellationToken cancellationToken = default) =>
        db.Professionals.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == professionalId)
            .Select(x => new ProfessionalCatalogEntry(x.Id, x.Name, x.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ProfessionalCatalogEntry?> FindProfessionalByUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
        db.Professionals.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.UserId == userId)
            .Select(x => new ProfessionalCatalogEntry(x.Id, x.Name, x.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ServiceCatalogEntry?> FindServiceAsync(Guid tenantId, Guid serviceId, CancellationToken cancellationToken = default) =>
        db.Services.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == serviceId)
            .Select(x => new ServiceCatalogEntry(x.Id, x.Name, x.IsActive, x.DurationMinutes))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<ProfessionalCatalogEntry>> ListActiveProfessionalsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await db.Professionals.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new ProfessionalCatalogEntry(x.Id, x.Name, x.IsActive))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<ProfessionalCatalogEntry>> FindProfessionalsAsync(Guid tenantId, IReadOnlyCollection<Guid> professionalIds, CancellationToken cancellationToken = default) =>
        await db.Professionals.AsNoTracking().Where(x => x.TenantId == tenantId && professionalIds.Contains(x.Id))
            .Select(x => new ProfessionalCatalogEntry(x.Id, x.Name, x.IsActive))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<ServiceCatalogEntry>> FindServicesAsync(Guid tenantId, IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken = default) =>
        await db.Services.AsNoTracking().Where(x => x.TenantId == tenantId && serviceIds.Contains(x.Id))
            .Select(x => new ServiceCatalogEntry(x.Id, x.Name, x.IsActive, x.DurationMinutes))
            .ToArrayAsync(cancellationToken);

    public Task<bool> IsActiveAssociationAsync(Guid tenantId, Guid professionalId, Guid serviceId, CancellationToken cancellationToken = default) =>
        db.ProfessionalServices.AsNoTracking().AnyAsync(
            x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.ServiceId == serviceId && x.IsActive,
            cancellationToken);
}
