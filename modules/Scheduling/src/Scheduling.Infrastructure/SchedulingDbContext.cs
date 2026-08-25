using Microsoft.EntityFrameworkCore;
using Scheduling.Domain;
using Shine.Domain;
using Shine.Infrastructure;

namespace Scheduling.Infrastructure;

public sealed class SchedulingDbContext(
    DbContextOptions<SchedulingDbContext> options,
    ICurrentTenant? currentTenant = null,
    ITenantExecutionContext? tenantExecutionContext = null) : DbContext(options)
{
    private Guid? EffectiveTenantId => tenantExecutionContext?.EffectiveTenantId ?? currentTenant?.TenantId;
    public DbSet<AvailabilityRule> AvailabilityRules => Set<AvailabilityRule>();
    public DbSet<AvailabilityException> AvailabilityExceptions => Set<AvailabilityException>();
    public DbSet<ScheduleBlock> ScheduleBlocks => Set<ScheduleBlock>();
    public DbSet<SchedulingSettings> SchedulingSettings => Set<SchedulingSettings>();
    public DbSet<ProfessionalSchedulingSettings> ProfessionalSettings => Set<ProfessionalSchedulingSettings>();
    public DbSet<ServiceSchedulingSettings> ServiceSettings => Set<ServiceSchedulingSettings>();
    public DbSet<ProfessionalServiceSchedulingSettings> ProfessionalServiceSettings => Set<ProfessionalServiceSchedulingSettings>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

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
            if (ChangeTracker.Entries().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && HasTenant(x.Entity)))
                throw new TenantIsolationException("An active tenant or an explicit bypass is required for scheduling writes.");
            return;
        }

        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            if (TryGetTenant(entry.Entity, out var entityTenant) && entityTenant != tenantId)
                throw new TenantIsolationException("The scheduling entity belongs to another tenant.");
    }

    private static bool HasTenant(object entity) => TryGetTenant(entity, out _);
    private static bool TryGetTenant(object entity, out Guid tenantId)
    {
        tenantId = entity switch
        {
            AvailabilityRule x => x.TenantId,
            AvailabilityException x => x.TenantId,
            ScheduleBlock x => x.TenantId,
            SchedulingSettings x => x.TenantId,
            ProfessionalSchedulingSettings x => x.TenantId,
            ServiceSchedulingSettings x => x.TenantId,
            ProfessionalServiceSchedulingSettings x => x.TenantId,
            Appointment x => x.TenantId,
            _ => Guid.Empty
        };
        return tenantId != Guid.Empty;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AvailabilityRule>(entity => { entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.TenantId, x.ProfessionalId, x.DayOfWeek, x.StartsAt, x.EndsAt }).IsUnique(); entity.HasQueryFilter(x => tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId); });
        modelBuilder.Entity<AvailabilityException>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.Reason).HasMaxLength(300).IsRequired(); entity.HasIndex(x => new { x.TenantId, x.ProfessionalId, x.Date }); entity.HasQueryFilter(x => tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId); });
        modelBuilder.Entity<ScheduleBlock>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.Reason).HasMaxLength(300).IsRequired(); entity.HasIndex(x => new { x.TenantId, x.ProfessionalId, x.StartsAtUtc, x.EndsAtUtc }); entity.HasQueryFilter(x => tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId); });
        modelBuilder.Entity<SchedulingSettings>(entity => { entity.HasKey(x => x.TenantId); entity.Property(x => x.TimeZoneId).HasMaxLength(80).IsRequired(); entity.Property(x => x.ConflictMode).HasConversion<int>(); entity.Property(x => x.DefaultMaxConcurrentAppointments).HasDefaultValue(1); entity.HasQueryFilter(x => tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId); });
        modelBuilder.Entity<ProfessionalSchedulingSettings>(entity => { entity.ToTable("ProfessionalSchedulingSettings"); entity.HasKey(x => new { x.TenantId, x.ProfessionalId }); entity.Property(x => x.MaxConcurrentAppointments).HasDefaultValue(1); entity.HasQueryFilter(x => tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId); });
        modelBuilder.Entity<ServiceSchedulingSettings>(entity => { entity.ToTable("ServiceSchedulingSettings"); entity.HasKey(x => new { x.TenantId, x.ServiceId }); entity.Property(x => x.DurationAttributeKey).HasMaxLength(120); entity.Property(x => x.DurationRuleVersion).HasMaxLength(80); entity.HasQueryFilter(x => tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId); });
        modelBuilder.Entity<ProfessionalServiceSchedulingSettings>(entity => { entity.ToTable("ProfessionalServiceSchedulingSettings"); entity.HasKey(x => new { x.TenantId, x.ProfessionalId, x.ServiceId }); entity.HasQueryFilter(x => tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId); });
        modelBuilder.Entity<Appointment>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.CustomerName).HasMaxLength(160).IsRequired(); entity.Property(x => x.CustomerContact).HasMaxLength(200).IsRequired(); entity.Property(x => x.Version).IsConcurrencyToken(); entity.HasIndex(x => new { x.TenantId, x.ProfessionalId, x.StartsAtUtc, x.EndsAtUtc }); entity.HasIndex(x => new { x.TenantId, x.CustomerId }); entity.HasQueryFilter(x => tenantExecutionContext != null && tenantExecutionContext.IsBypass || EffectiveTenantId != null && x.TenantId == EffectiveTenantId); });
        modelBuilder.Entity<OutboxMessage>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.EventType).HasMaxLength(200).IsRequired(); entity.Property(x => x.Payload).IsRequired(); entity.Property(x => x.IdempotencyKey).HasMaxLength(300); entity.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc }); entity.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL"); });
    }
}
