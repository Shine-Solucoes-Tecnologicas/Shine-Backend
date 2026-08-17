using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Application;
using Scheduling.Domain;
using Shine.Api.Controllers;
using Shine.Domain;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class PublicSchedulingCapacityTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Public_booking_applies_allow_warn_and_block_modes_below_capacity()
    {
        Assert.IsType<OkObjectResult>((await BookAsync(ConflictMode.Allow, allowConflict: false)).Result);

        var warning = Assert.IsType<ConflictObjectResult>((await BookAsync(ConflictMode.WarnAndConfirm, allowConflict: false)).Result);
        Assert.Contains("APPOINTMENT_CONFLICT", JsonSerializer.Serialize(warning.Value));
        Assert.IsType<OkObjectResult>((await BookAsync(ConflictMode.WarnAndConfirm, allowConflict: true)).Result);

        var blocked = Assert.IsType<ConflictObjectResult>((await BookAsync(ConflictMode.Block, allowConflict: true)).Result);
        Assert.Contains("APPOINTMENT_UNAVAILABLE", JsonSerializer.Serialize(blocked.Value));
    }

    private async Task<ActionResult<PublicAppointmentResponse>> BookAsync(ConflictMode mode, bool allowConflict)
    {
        var tenantId = Guid.NewGuid();
        var professional = new Professional(tenantId, $"Public {Guid.NewGuid():N}", maxConcurrentAppointments: 2);
        var service = new Service(tenantId, $"Public service {Guid.NewGuid():N}", 30);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var startsAt = date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var settings = new SchedulingSettings(tenantId);
        settings.Update(30, 0, 0, "UTC", mode, 2);
        await using (var setup = fixture.CreateSchedulingDb())
        {
            setup.AddRange(professional, service, new ProfessionalService(tenantId, professional.Id, service.Id),
                new AvailabilityRule(tenantId, professional.Id, date.DayOfWeek, new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0)),
                settings,
                new Appointment(tenantId, professional.Id, service.Id, "Existing", "existing@example.test", startsAt, startsAt.AddMinutes(30)));
            await setup.SaveChangesAsync();
        }

        var noTenant = new NoTenant();
        var execution = new TenantExecutionContext(noTenant);
        await using var db = fixture.CreateSchedulingDb(noTenant, execution);
        var controller = new PublicSchedulingController(db, new AvailabilitySlotCalculator(), new ServiceDurationEstimator([]),
            new NoOpPublisher(), new EnabledModuleAccess(), new UnlimitedGuard(), execution);
        return await controller.CreateAppointment(tenantId,
            new PublicCreateAppointmentRequest(professional.Id, service.Id, "Customer", "customer@example.test",
                startsAt, startsAt.AddMinutes(30), AllowConflict: allowConflict), default);
    }

    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public Guid? UserTenantId => null;
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => false;
    }
    private sealed class EnabledModuleAccess : IModuleAccess
    {
        public Task<IReadOnlyCollection<ModuleDescriptor>> GetAccessibleAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<ModuleDescriptor>>([]);
        public Task<bool> HasAccessAsync(Guid tenantId, string moduleCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task SetAsync(Guid tenantId, string moduleCode, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class UnlimitedGuard : IEntitlementLimitGuard
    {
        public Task<EntitlementLimitDecision> TryReserveAsync(Guid unitId, string key, long quantity = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EntitlementLimitDecision(key, EntitlementLimitStatus.Unlimited, 0, quantity, null, null));
        public Task ReleaseAsync(Guid unitId, string key, long quantity = 1, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<EntitlementLimitDecision> TryReserveAsync(Guid unitId, string key, Guid operationId, long quantity = 1, CancellationToken cancellationToken = default) => TryReserveAsync(unitId, key, quantity, cancellationToken);
        public Task ReleaseAsync(Guid unitId, string key, Guid operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReconcileAsync(Guid unitId, string key, IReadOnlySet<Guid> activeOperationIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class NoOpPublisher : IAppointmentEventPublisher
    {
        public Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
