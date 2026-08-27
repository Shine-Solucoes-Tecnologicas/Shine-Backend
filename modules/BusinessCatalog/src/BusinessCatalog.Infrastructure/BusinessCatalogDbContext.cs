using BusinessCatalog.Domain;
using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure;

namespace BusinessCatalog.Infrastructure;

public sealed class BusinessCatalogDbContext(
    DbContextOptions<BusinessCatalogDbContext> options,
    ICurrentTenant? currentTenant = null,
    ITenantExecutionContext? tenantExecutionContext = null) : DbContext(options)
{
    private Guid? EffectiveTenantId => tenantExecutionContext?.EffectiveTenantId ?? currentTenant?.TenantId;

    public DbSet<Professional> Professionals => Set<Professional>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ProfessionalService> ProfessionalServices => Set<ProfessionalService>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantBoundary();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantBoundary();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceTenantBoundary()
    {
        if (tenantExecutionContext?.IsBypass == true) return;
        if (EffectiveTenantId is not Guid tenantId)
        {
            if (ChangeTracker.Entries().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
                throw new TenantIsolationException("An active unit or an explicit bypass is required for business catalog writes.");
            return;
        }

        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            if (TenantId(entry.Entity) is Guid entityTenant && entityTenant != tenantId)
                throw new TenantIsolationException("The business catalog entity belongs to another unit.");
    }

    private static Guid? TenantId(object entity) => entity switch
    {
        Professional professional => professional.TenantId,
        Service service => service.TenantId,
        ProfessionalService association => association.TenantId,
        _ => null
    };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Professional>(entity =>
        {
            entity.ToTable("Professionals");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique().HasFilter("\"UserId\" IS NOT NULL");
            entity.HasQueryFilter(x => tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId);
        });
        modelBuilder.Entity<Service>(entity =>
        {
            entity.ToTable("Services");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            entity.HasQueryFilter(x => tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId);
        });
        modelBuilder.Entity<ProfessionalService>(entity =>
        {
            entity.ToTable("ProfessionalServices");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.ProfessionalId, x.ServiceId }).IsUnique();
            entity.HasQueryFilter(x => tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId);
        });
    }
}
