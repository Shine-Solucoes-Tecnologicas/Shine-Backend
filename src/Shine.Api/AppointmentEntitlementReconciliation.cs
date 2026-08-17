using Microsoft.EntityFrameworkCore;
using Scheduling.Domain;
using Scheduling.Infrastructure;
using Shine.Domain;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Shine.Api;

public sealed class AppointmentEntitlementReconciliationService(
    SchedulingDbContext scheduling,
    ShineDbContext core,
    IEntitlementLimitGuard entitlements,
    ITenantExecutionContext tenantExecutionContext)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await scheduling.Database.BeginTransactionAsync(cancellationToken);
        await scheduling.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended('appointment-entitlement-reconciliation', 0))",
            cancellationToken);

        var tenantIds = await core.Tenants.AsNoTracking().Select(x => x.Id).ToArrayAsync(cancellationToken);
        foreach (var tenantId in tenantIds)
        {
            using var tenantScope = tenantExecutionContext.EnterTenant(tenantId);
            var activeIds = (await scheduling.Appointments.AsNoTracking()
                    .Where(x => x.Status == AppointmentStatus.Scheduled || x.Status == AppointmentStatus.Confirmed)
                    .Select(x => x.Id).ToArrayAsync(cancellationToken))
                .ToHashSet();
            await entitlements.ReconcileAsync(tenantId, EntitlementKeys.SchedulingActiveAppointments, activeIds, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class AppointmentEntitlementReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AppointmentEntitlementReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AppointmentEntitlementReconciliationService>().ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "Appointment entitlement reconciliation failed."); }

            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }
}
