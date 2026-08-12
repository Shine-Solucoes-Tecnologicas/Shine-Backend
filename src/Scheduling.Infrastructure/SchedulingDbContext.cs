using Microsoft.EntityFrameworkCore;
using Scheduling.Domain;

namespace Scheduling.Infrastructure;

public sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : DbContext(options)
{
    public DbSet<Professional> Professionals => Set<Professional>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ProfessionalService> ProfessionalServices => Set<ProfessionalService>();
    public DbSet<AvailabilityRule> AvailabilityRules => Set<AvailabilityRule>();
    public DbSet<AvailabilityException> AvailabilityExceptions => Set<AvailabilityException>();
    public DbSet<ScheduleBlock> ScheduleBlocks => Set<ScheduleBlock>();
    public DbSet<SchedulingSettings> SchedulingSettings => Set<SchedulingSettings>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Professional>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.Name).HasMaxLength(160).IsRequired(); entity.Property(x => x.MaxConcurrentAppointments).HasDefaultValue(1); entity.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(); });
        modelBuilder.Entity<Service>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.Name).HasMaxLength(160).IsRequired(); entity.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(); });
        modelBuilder.Entity<ProfessionalService>(entity => { entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.TenantId, x.ProfessionalId, x.ServiceId }).IsUnique(); });
        modelBuilder.Entity<AvailabilityRule>(entity => { entity.HasKey(x => x.Id); entity.HasIndex(x => new { x.TenantId, x.ProfessionalId, x.DayOfWeek, x.StartsAt, x.EndsAt }).IsUnique(); });
        modelBuilder.Entity<AvailabilityException>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.Reason).HasMaxLength(300).IsRequired(); entity.HasIndex(x => new { x.TenantId, x.ProfessionalId, x.Date }); });
        modelBuilder.Entity<ScheduleBlock>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.Reason).HasMaxLength(300).IsRequired(); entity.HasIndex(x => new { x.TenantId, x.ProfessionalId, x.StartsAtUtc, x.EndsAtUtc }); });
        modelBuilder.Entity<SchedulingSettings>(entity => { entity.HasKey(x => x.TenantId); entity.Property(x => x.TimeZoneId).HasMaxLength(80).IsRequired(); entity.Property(x => x.ConflictMode).HasConversion<int>(); entity.Property(x => x.DefaultMaxConcurrentAppointments).HasDefaultValue(1); });
        modelBuilder.Entity<Appointment>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.CustomerName).HasMaxLength(160).IsRequired(); entity.Property(x => x.CustomerContact).HasMaxLength(200).IsRequired(); entity.Property(x => x.Version).IsConcurrencyToken(); entity.HasIndex(x => new { x.TenantId, x.ProfessionalId, x.StartsAtUtc, x.EndsAtUtc }); });
        modelBuilder.Entity<OutboxMessage>(entity => { entity.HasKey(x => x.Id); entity.Property(x => x.EventType).HasMaxLength(200).IsRequired(); entity.Property(x => x.Payload).IsRequired(); entity.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc }); });
    }
}
