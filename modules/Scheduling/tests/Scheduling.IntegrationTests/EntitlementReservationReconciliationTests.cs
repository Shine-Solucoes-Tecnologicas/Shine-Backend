using Microsoft.EntityFrameworkCore;
using Scheduling.Application;
using Scheduling.Domain;
using Shine.Api;
using Shine.Api.Controllers;
using Shine.Application;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class EntitlementReservationReconciliationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Controller_compensates_when_scheduling_persistence_pipeline_fails_after_reservation()
    {
        var tenantId = await CreateTenantAsync();
        var currentTenant = new TestTenant(tenantId);
        await using var coreDb = fixture.CreateDb(currentTenant);
        await using var schedulingDb = fixture.CreateSchedulingDb(currentTenant);
        var professional = new Professional(tenantId, "Professional");
        var service = new Service(tenantId, "Service", 30);
        await using (var catalogDb = fixture.CreateBusinessCatalogDb(currentTenant))
        {
            catalogDb.AddRange(professional, service, new ProfessionalService(tenantId, professional.Id, service.Id));
            await catalogDb.SaveChangesAsync();
        }
        var guard = new EntitlementLimitGuard(coreDb, new FixedEntitlements(10));
        var controller = new SchedulingController(schedulingDb, currentTenant, new AvailabilitySlotCalculator(), new ThrowingPublisher(), new NoOpLogWriter(), guard, businessCatalog: fixture.CreateBusinessCatalogReader(currentTenant));
        var startsAt = DateTime.UtcNow.AddDays(1);

        await Assert.ThrowsAsync<InjectedSchedulingFailure>(() => controller.CreateAppointment(
            new CreateAppointmentRequest(professional.Id, service.Id, "Customer", "customer@example.com", startsAt, startsAt.AddMinutes(30)),
            CancellationToken.None));

        Assert.Equal(0, (await coreDb.EntitlementUsages.SingleAsync(x => x.TenantId == tenantId)).Used);
        Assert.Equal(EntitlementReservationStatus.Released,
            (await coreDb.EntitlementReservations.SingleAsync(x => x.TenantId == tenantId)).Status);
        Assert.False(await schedulingDb.Appointments.AnyAsync(x => x.TenantId == tenantId));
    }

    [Fact]
    public async Task Scheduling_rollback_after_core_reservation_is_recovered()
    {
        var tenantId = await CreateTenantAsync();
        var appointment = NewAppointment(tenantId);
        var noTenant = new NoTenant();
        var execution = new TenantExecutionContext(noTenant);
        await using var coreDb = fixture.CreateDb(noTenant, execution);
        await using var schedulingDb = fixture.CreateSchedulingDb(noTenant, execution);
        var guard = new EntitlementLimitGuard(coreDb, new FixedEntitlements(10));

        using (execution.EnterTenant(tenantId))
        {
            await using var failedSchedulingTransaction = await schedulingDb.Database.BeginTransactionAsync();
            Assert.True((await guard.TryReserveAsync(tenantId, EntitlementKeys.SchedulingActiveAppointments, appointment.Id)).Allowed);
            schedulingDb.Appointments.Add(appointment);
            await schedulingDb.SaveChangesAsync();
            await failedSchedulingTransaction.RollbackAsync();
            schedulingDb.ChangeTracker.Clear();
        }

        await new AppointmentEntitlementReconciliationService(schedulingDb, coreDb, guard, execution).ReconcileAsync();
        await AssertUsageAsync(coreDb, execution, tenantId, appointment.Id, 0, EntitlementReservationStatus.Released);
    }

    [Fact]
    public async Task Active_appointment_without_reservation_is_restored_authoritatively_even_when_limit_is_exhausted()
    {
        var tenantId = await CreateTenantAsync();
        var appointment = NewAppointment(tenantId);
        await using (var setup = fixture.CreateSchedulingDb())
        {
            setup.Appointments.Add(appointment);
            await setup.SaveChangesAsync();
        }

        var noTenant = new NoTenant();
        var execution = new TenantExecutionContext(noTenant);
        await using var coreDb = fixture.CreateDb(noTenant, execution);
        await using var schedulingDb = fixture.CreateSchedulingDb(noTenant, execution);
        var guard = new EntitlementLimitGuard(coreDb, new FixedEntitlements(0));
        var service = new AppointmentEntitlementReconciliationService(schedulingDb, coreDb, guard, execution);

        await service.ReconcileAsync();
        await service.ReconcileAsync();

        await AssertUsageAsync(coreDb, execution, tenantId, appointment.Id, 1, EntitlementReservationStatus.Reserved);
    }

    [Fact]
    public async Task Persisted_cancellation_is_recovered_when_release_did_not_run()
    {
        var tenantId = await CreateTenantAsync();
        var appointment = NewAppointment(tenantId);
        var noTenant = new NoTenant();
        var execution = new TenantExecutionContext(noTenant);
        await using var coreDb = fixture.CreateDb(noTenant, execution);
        await using var schedulingDb = fixture.CreateSchedulingDb(noTenant, execution);
        var guard = new EntitlementLimitGuard(coreDb, new FixedEntitlements(10));

        using (execution.EnterTenant(tenantId))
        {
            Assert.True((await guard.TryReserveAsync(tenantId, EntitlementKeys.SchedulingActiveAppointments, appointment.Id)).Allowed);
            schedulingDb.Appointments.Add(appointment);
            await schedulingDb.SaveChangesAsync();
            appointment.ChangeStatus(AppointmentStatus.Cancelled);
            await schedulingDb.SaveChangesAsync();
        }

        await new AppointmentEntitlementReconciliationService(schedulingDb, coreDb, guard, execution).ReconcileAsync();
        await AssertUsageAsync(coreDb, execution, tenantId, appointment.Id, 0, EntitlementReservationStatus.Released);

        using (execution.EnterTenant(tenantId))
        {
            await guard.ReleaseAsync(tenantId, EntitlementKeys.SchedulingActiveAppointments, appointment.Id);
            Assert.Equal(0, (await coreDb.EntitlementUsages.SingleAsync(x => x.TenantId == tenantId)).Used);
        }
    }

    [Fact]
    public async Task Release_failure_after_persisted_cancellation_is_recovered()
    {
        var tenantId = await CreateTenantAsync();
        var appointment = NewAppointment(tenantId);
        var currentTenant = new TestTenant(tenantId);
        await using var coreDb = fixture.CreateDb(currentTenant);
        await using var schedulingDb = fixture.CreateSchedulingDb(currentTenant);
        var guard = new EntitlementLimitGuard(coreDb, new FixedEntitlements(10));
        Assert.True((await guard.TryReserveAsync(tenantId, EntitlementKeys.SchedulingActiveAppointments, appointment.Id)).Allowed);
        schedulingDb.Appointments.Add(appointment);
        await schedulingDb.SaveChangesAsync();
        var controller = new SchedulingController(schedulingDb, currentTenant, new AvailabilitySlotCalculator(), new NoOpPublisher(), new NoOpLogWriter(), new FailingReleaseGuard(guard), businessCatalog: fixture.CreateBusinessCatalogReader(currentTenant));

        await Assert.ThrowsAsync<InjectedSchedulingFailure>(() => controller.ChangeAppointmentStatus(
            appointment.Id,
            new ChangeAppointmentStatusRequest(AppointmentStatus.Cancelled),
            CancellationToken.None));
        Assert.Equal(AppointmentStatus.Cancelled, (await schedulingDb.Appointments.SingleAsync(x => x.Id == appointment.Id)).Status);

        var noTenant = new NoTenant();
        var execution = new TenantExecutionContext(noTenant);
        await using var reconciliationCore = fixture.CreateDb(noTenant, execution);
        await using var reconciliationScheduling = fixture.CreateSchedulingDb(noTenant, execution);
        var reconciliationGuard = new EntitlementLimitGuard(reconciliationCore, new FixedEntitlements(10));
        await new AppointmentEntitlementReconciliationService(reconciliationScheduling, reconciliationCore, reconciliationGuard, execution).ReconcileAsync();
        await AssertUsageAsync(reconciliationCore, execution, tenantId, appointment.Id, 0, EntitlementReservationStatus.Released);
    }

    [Fact]
    public async Task Concurrent_reconciliation_is_serialized_and_idempotent()
    {
        var tenantId = await CreateTenantAsync();
        var appointment = NewAppointment(tenantId);
        await using (var setup = fixture.CreateSchedulingDb())
        {
            setup.Appointments.Add(appointment);
            await setup.SaveChangesAsync();
        }

        async Task ReconcileAsync()
        {
            var noTenant = new NoTenant();
            var execution = new TenantExecutionContext(noTenant);
            await using var coreDb = fixture.CreateDb(noTenant, execution);
            await using var schedulingDb = fixture.CreateSchedulingDb(noTenant, execution);
            var guard = new EntitlementLimitGuard(coreDb, new FixedEntitlements(10));
            await new AppointmentEntitlementReconciliationService(schedulingDb, coreDb, guard, execution).ReconcileAsync();
        }

        await Task.WhenAll(ReconcileAsync(), ReconcileAsync());

        await using var verification = fixture.CreateDb();
        Assert.Equal(1, (await verification.EntitlementUsages.SingleAsync(x => x.TenantId == tenantId)).Used);
        Assert.Single(await verification.EntitlementReservations
            .Where(x => x.TenantId == tenantId && x.OperationId == appointment.Id && x.Status == EntitlementReservationStatus.Reserved)
            .ToArrayAsync());
    }

    [Fact]
    public async Task Unlimited_correlated_reservation_is_tracked_and_compensated()
    {
        var tenantId = await CreateTenantAsync();
        var appointmentId = Guid.NewGuid();
        var noTenant = new NoTenant();
        var execution = new TenantExecutionContext(noTenant);
        await using var coreDb = fixture.CreateDb(noTenant, execution);
        var guard = new EntitlementLimitGuard(coreDb, new UnlimitedEntitlements());

        using (execution.EnterTenant(tenantId))
        {
            var decision = await guard.TryReserveAsync(tenantId, EntitlementKeys.SchedulingActiveAppointments, appointmentId);
            Assert.Equal(EntitlementLimitStatus.Unlimited, decision.Status);
            Assert.Equal(1, (await coreDb.EntitlementUsages.SingleAsync(x => x.TenantId == tenantId)).Used);
            await guard.ReleaseAsync(tenantId, EntitlementKeys.SchedulingActiveAppointments, appointmentId);
            Assert.Equal(0, (await coreDb.EntitlementUsages.SingleAsync(x => x.TenantId == tenantId)).Used);
        }
    }

    private static Appointment NewAppointment(Guid tenantId)
    {
        var startsAt = DateTime.UtcNow.AddDays(1);
        return new Appointment(tenantId, Guid.NewGuid(), Guid.NewGuid(), "Reconciliation test", "test@example.com", startsAt, startsAt.AddMinutes(30));
    }

    private async Task<Guid> CreateTenantAsync()
    {
        await using var db = fixture.CreateDb();
        var tenant = new Tenant($"Reconciliation {Guid.NewGuid():N}");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static async Task AssertUsageAsync(
        DbContext coreDb,
        TenantExecutionContext execution,
        Guid tenantId,
        Guid appointmentId,
        long expectedUsage,
        EntitlementReservationStatus expectedStatus)
    {
        using (execution.EnterTenant(tenantId))
        {
            var usage = await coreDb.Set<EntitlementUsage>().SingleAsync(x => x.TenantId == tenantId);
            var reservation = await coreDb.Set<EntitlementReservation>().SingleAsync(x => x.OperationId == appointmentId);
            Assert.Equal(expectedUsage, usage.Used);
            Assert.Equal(expectedStatus, reservation.Status);
        }
    }

    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public Guid? UserTenantId => null;
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => false;
    }

    private sealed record TestTenant(Guid UnitId) : ICurrentTenant
    {
        public Guid? TenantId => UnitId;
        public Guid? UserTenantId => Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => true;
    }

    private sealed class ThrowingPublisher : IAppointmentEventPublisher
    {
        public Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken = default) =>
            Task.FromException(new InjectedSchedulingFailure());
    }

    private sealed class NoOpPublisher : IAppointmentEventPublisher
    {
        public Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FailingReleaseGuard(IEntitlementLimitGuard inner) : IEntitlementLimitGuard
    {
        public Task<EntitlementLimitDecision> TryReserveAsync(Guid unitId, string key, long quantity = 1, CancellationToken cancellationToken = default) => inner.TryReserveAsync(unitId, key, quantity, cancellationToken);
        public Task ReleaseAsync(Guid unitId, string key, long quantity = 1, CancellationToken cancellationToken = default) => inner.ReleaseAsync(unitId, key, quantity, cancellationToken);
        public Task<EntitlementLimitDecision> TryReserveAsync(Guid unitId, string key, Guid operationId, long quantity = 1, CancellationToken cancellationToken = default) => inner.TryReserveAsync(unitId, key, operationId, quantity, cancellationToken);
        public Task ReleaseAsync(Guid unitId, string key, Guid operationId, CancellationToken cancellationToken = default) => Task.FromException(new InjectedSchedulingFailure());
        public Task ReconcileAsync(Guid unitId, string key, IReadOnlySet<Guid> activeOperationIds, CancellationToken cancellationToken = default) => inner.ReconcileAsync(unitId, key, activeOperationIds, cancellationToken);
    }

    private sealed class InjectedSchedulingFailure : Exception;

    private sealed class NoOpLogWriter : IOperationalLogWriter
    {
        public Task WriteAsync(string level, string category, string message, Exception? exception = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedEntitlements(long limit) : IEntitlementAccess
    {
        public Task<EntitlementSnapshot> GetAsync(Guid unitId, CancellationToken cancellationToken = default) => Task.FromResult(new EntitlementSnapshot(unitId, new HashSet<string>(), new Dictionary<string, long> { [EntitlementKeys.SchedulingActiveAppointments] = limit }));
        public Task<bool> HasModuleAsync(Guid unitId, string moduleCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> AllowsAsync(Guid unitId, string limitCode, long requested = 1, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<EntitlementGrant?> EvaluateAsync(Guid unitId, string key, CancellationToken cancellationToken = default) => Task.FromResult<EntitlementGrant?>(new EntitlementGrant(key, limit, "test"));
        public Task<EntitlementLimitDecision> EvaluateLimitAsync(Guid unitId, string key, long currentUsage, long requested = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult(currentUsage + requested <= limit
                ? new EntitlementLimitDecision(key, EntitlementLimitStatus.Available, currentUsage, requested, limit, limit - currentUsage - requested)
                : new EntitlementLimitDecision(key, EntitlementLimitStatus.Exhausted, currentUsage, requested, limit, 0));
    }

    private sealed class UnlimitedEntitlements : IEntitlementAccess
    {
        public Task<EntitlementSnapshot> GetAsync(Guid unitId, CancellationToken cancellationToken = default) => Task.FromResult(new EntitlementSnapshot(unitId, new HashSet<string>(), new Dictionary<string, EntitlementGrant> { [EntitlementKeys.SchedulingActiveAppointments] = new(EntitlementKeys.SchedulingActiveAppointments, 0, "test", isUnlimited: true) }));
        public Task<bool> HasModuleAsync(Guid unitId, string moduleCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> AllowsAsync(Guid unitId, string limitCode, long requested = 1, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<EntitlementGrant?> EvaluateAsync(Guid unitId, string key, CancellationToken cancellationToken = default) => Task.FromResult<EntitlementGrant?>(new EntitlementGrant(key, 0, "test", isUnlimited: true));
        public Task<EntitlementLimitDecision> EvaluateLimitAsync(Guid unitId, string key, long currentUsage, long requested = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EntitlementLimitDecision(key, EntitlementLimitStatus.Unlimited, currentUsage, requested, null, null));
    }
}
