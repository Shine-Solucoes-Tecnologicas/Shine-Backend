using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scheduling.Application;
using Scheduling.Domain;
using Scheduling.Infrastructure;
using Shine.Api;
using Shine.Api.Controllers;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class SchedulingEntitlementEndpointTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Appointment_creation_is_blocked_when_the_limit_is_not_configured()
    {
        await using var db = fixture.CreateSchedulingDb();
        var unitId = Guid.NewGuid();
        var professional = new Professional(unitId, $"Professional {Guid.NewGuid():N}");
        var service = new Service(unitId, $"Service {Guid.NewGuid():N}", 30);
        await using (var catalogDb = fixture.CreateBusinessCatalogDb())
        {
            catalogDb.AddRange(professional, service, new ProfessionalService(unitId, professional.Id, service.Id));
            await catalogDb.SaveChangesAsync();
        }
        var controller = new SchedulingController(db, new TestTenant(unitId), new AvailabilitySlotCalculator(),
            new NoOpPublisher(), new NoOpLogWriter(), new NotConfiguredGuard(), new AllowAllSchedulingScopeAuthorization(), businessCatalog: fixture.CreateBusinessCatalogReader(new TestTenant(unitId)));
        var startsAtUtc = DateTime.UtcNow.AddDays(2);

        var response = await controller.CreateAppointment(new CreateAppointmentRequest(
            professional.Id, service.Id, "Customer", "customer@example.test", startsAtUtc, startsAtUtc.AddMinutes(30)), default);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        Assert.Contains("ENTITLEMENT_LIMIT_NOT_CONFIGURED", JsonSerializer.Serialize(conflict.Value));
        Assert.False(db.Appointments.Any(x => x.TenantId == unitId));
    }

    [Fact]
    public async Task Concurrent_authenticated_creation_does_not_exceed_professional_capacity()
    {
        var unitId = Guid.NewGuid();
        var professional = new Professional(unitId, $"Concurrent {Guid.NewGuid():N}");
        var service = new Service(unitId, $"Concurrent service {Guid.NewGuid():N}", 30);
        await using (var setup = fixture.CreateBusinessCatalogDb())
        {
            setup.AddRange(professional, service, new ProfessionalService(unitId, professional.Id, service.Id));
            await setup.SaveChangesAsync();
        }
        var startsAtUtc = DateTime.UtcNow.AddDays(3);
        var request = new CreateAppointmentRequest(professional.Id, service.Id, "Customer", "customer@example.test", startsAtUtc, startsAtUtc.AddMinutes(30));

        async Task<ActionResult<AppointmentResponse>> CreateAsync()
        {
            await using var db = fixture.CreateSchedulingDb(new TestTenant(unitId));
            var controller = new SchedulingController(db, new TestTenant(unitId), new AvailabilitySlotCalculator(),
                new NoOpPublisher(), new NoOpLogWriter(), new UnlimitedGuard(), new AllowAllSchedulingScopeAuthorization(), businessCatalog: fixture.CreateBusinessCatalogReader(new TestTenant(unitId)));
            return await controller.CreateAppointment(request, default);
        }

        var results = await Task.WhenAll(CreateAsync(), CreateAsync());
        Assert.Single(results, x => x.Result is OkObjectResult);
        var conflict = Assert.Single(results, x => x.Result is ObjectResult { StatusCode: StatusCodes.Status409Conflict });
        Assert.Contains("CAPACITY_EXCEEDED", JsonSerializer.Serialize(Assert.IsType<ObjectResult>(conflict.Result).Value));
        await using var verification = fixture.CreateSchedulingDb();
        Assert.Equal(1, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            verification.Appointments.Where(x => x.TenantId == unitId && x.ProfessionalId == professional.Id)));
    }

    [Fact]
    public async Task Concurrent_reschedules_serialize_the_destination_capacity()
    {
        var unitId = Guid.NewGuid();
        var professional = new Professional(unitId, $"Reschedule {Guid.NewGuid():N}");
        var service = new Service(unitId, $"Reschedule service {Guid.NewGuid():N}", 30);
        var baseTime = DateTime.UtcNow.AddDays(4);
        var first = new Appointment(unitId, professional.Id, service.Id, "First", "first@example.test", baseTime, baseTime.AddMinutes(30));
        var second = new Appointment(unitId, professional.Id, service.Id, "Second", "second@example.test", baseTime.AddHours(1), baseTime.AddHours(1).AddMinutes(30));
        await using (var catalogSetup = fixture.CreateBusinessCatalogDb())
        {
            catalogSetup.AddRange(professional, service);
            await catalogSetup.SaveChangesAsync();
        }
        await using (var setup = fixture.CreateSchedulingDb())
        {
            setup.AddRange(first, second);
            await setup.SaveChangesAsync();
        }
        var target = baseTime.AddHours(3);

        async Task<ActionResult<AppointmentResponse>> RescheduleAsync(Guid id, Guid version)
        {
            await using var db = fixture.CreateSchedulingDb(new TestTenant(unitId));
            var controller = new SchedulingController(db, new TestTenant(unitId), new AvailabilitySlotCalculator(),
                new NoOpPublisher(), new NoOpLogWriter(), new UnlimitedGuard(), new AllowAllSchedulingScopeAuthorization(), businessCatalog: fixture.CreateBusinessCatalogReader(new TestTenant(unitId)));
            return await controller.RescheduleAppointment(id,
                new RescheduleAppointmentRequest(target, target.AddMinutes(30), version), default);
        }

        var results = await Task.WhenAll(RescheduleAsync(first.Id, first.Version), RescheduleAsync(second.Id, second.Version));
        Assert.Single(results, x => x.Result is OkObjectResult);
        Assert.Single(results, x => x.Result is ObjectResult { StatusCode: StatusCodes.Status409Conflict });
        await using var verification = fixture.CreateSchedulingDb();
        Assert.Equal(1, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            verification.Appointments.Where(x => x.TenantId == unitId && x.ProfessionalId == professional.Id && x.StartsAtUtc == target)));
    }

    [Fact]
    public async Task Authenticated_creation_links_an_active_customer_from_the_current_unit_and_publishes_the_reference()
    {
        var tenant = new Tenant($"Scheduling customer {Guid.NewGuid():N}");
        var customer = new Customer(tenant.Id, "Customer snapshot source", "customer@example.test");
        await using var customerDb = fixture.CreateDb();
        customerDb.AddRange(tenant, customer);
        await customerDb.SaveChangesAsync();

        var professional = new Professional(tenant.Id, $"Professional {Guid.NewGuid():N}");
        var service = new Service(tenant.Id, $"Service {Guid.NewGuid():N}", 30);
        await using (var catalogDb = fixture.CreateBusinessCatalogDb(new TestTenant(tenant.Id)))
        {
            catalogDb.AddRange(professional, service, new ProfessionalService(tenant.Id, professional.Id, service.Id));
            await catalogDb.SaveChangesAsync();
        }
        await using var schedulingDb = fixture.CreateSchedulingDb(new TestTenant(tenant.Id));
        var publisher = new CapturingPublisher();
        var validator = new CustomerManagement(customerDb);
        var controller = new SchedulingController(schedulingDb, new TestTenant(tenant.Id), new AvailabilitySlotCalculator(),
            publisher, new NoOpLogWriter(), new UnlimitedGuard(), new AllowAllSchedulingScopeAuthorization(), validator, fixture.CreateBusinessCatalogReader(new TestTenant(tenant.Id)));
        var startsAtUtc = DateTime.UtcNow.AddDays(5);

        var result = await controller.CreateAppointment(new CreateAppointmentRequest(
            professional.Id, service.Id, "Customer snapshot", "snapshot@example.test", startsAtUtc,
            startsAtUtc.AddMinutes(30), CustomerId: customer.Id), default);

        var response = Assert.IsType<AppointmentResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(customer.Id, response.CustomerId);
        Assert.Equal("Customer snapshot", response.CustomerName);
        var createdEvent = Assert.IsType<AppointmentCreatedEvent>(Assert.Single(publisher.Events));
        Assert.Equal(customer.Id, createdEvent.CustomerId);
        var persisted = await schedulingDb.Appointments.AsNoTracking().SingleAsync(x => x.Id == response.Id);
        Assert.Equal(customer.Id, persisted.CustomerId);
        Assert.Equal("Customer snapshot", persisted.CustomerName);
    }

    [Fact]
    public async Task Authenticated_creation_rejects_foreign_and_inactive_customers()
    {
        var tenant = new Tenant($"Current unit {Guid.NewGuid():N}");
        var otherTenant = new Tenant($"Other unit {Guid.NewGuid():N}");
        var foreignCustomer = new Customer(otherTenant.Id, "Foreign customer");
        var inactiveCustomer = new Customer(tenant.Id, "Inactive customer");
        inactiveCustomer.Deactivate(DateTime.UtcNow);
        await using var customerDb = fixture.CreateDb();
        customerDb.AddRange(tenant, otherTenant, foreignCustomer, inactiveCustomer);
        await customerDb.SaveChangesAsync();

        var professional = new Professional(tenant.Id, $"Professional {Guid.NewGuid():N}");
        var service = new Service(tenant.Id, $"Service {Guid.NewGuid():N}", 30);
        await using (var catalogDb = fixture.CreateBusinessCatalogDb(new TestTenant(tenant.Id)))
        {
            catalogDb.AddRange(professional, service, new ProfessionalService(tenant.Id, professional.Id, service.Id));
            await catalogDb.SaveChangesAsync();
        }
        await using var schedulingDb = fixture.CreateSchedulingDb(new TestTenant(tenant.Id));
        var controller = new SchedulingController(schedulingDb, new TestTenant(tenant.Id), new AvailabilitySlotCalculator(),
            new NoOpPublisher(), new NoOpLogWriter(), new UnlimitedGuard(), new AllowAllSchedulingScopeAuthorization(), new CustomerManagement(customerDb), fixture.CreateBusinessCatalogReader(new TestTenant(tenant.Id)));
        var startsAtUtc = DateTime.UtcNow.AddDays(6);

        foreach (var customerId in new[] { foreignCustomer.Id, inactiveCustomer.Id })
        {
            var result = await controller.CreateAppointment(new CreateAppointmentRequest(
                professional.Id, service.Id, "Snapshot", "contact", startsAtUtc, startsAtUtc.AddMinutes(30),
                CustomerId: customerId), default);

            var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
            Assert.Contains("CUSTOMER_NOT_FOUND", JsonSerializer.Serialize(notFound.Value));
        }

        Assert.False(await schedulingDb.Appointments.AnyAsync(x => x.TenantId == tenant.Id));
    }

    [Fact]
    public async Task Database_rejects_customer_reference_from_another_unit()
    {
        var tenant = new Tenant($"Appointment unit {Guid.NewGuid():N}");
        var otherTenant = new Tenant($"Customer unit {Guid.NewGuid():N}");
        var foreignCustomer = new Customer(otherTenant.Id, "Foreign customer");
        await using (var customerDb = fixture.CreateDb())
        {
            customerDb.AddRange(tenant, otherTenant, foreignCustomer);
            await customerDb.SaveChangesAsync();
        }

        var professional = new Professional(tenant.Id, $"Professional {Guid.NewGuid():N}");
        var service = new Service(tenant.Id, $"Service {Guid.NewGuid():N}", 30);
        await using (var catalogDb = fixture.CreateBusinessCatalogDb())
        {
            catalogDb.AddRange(professional, service);
            await catalogDb.SaveChangesAsync();
        }
        await using var schedulingDb = fixture.CreateSchedulingDb();
        var startsAtUtc = DateTime.UtcNow.AddDays(7);
        schedulingDb.Appointments.Add(new Appointment(tenant.Id, professional.Id, service.Id, "Snapshot", "contact",
            startsAtUtc, startsAtUtc.AddMinutes(30), foreignCustomer.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => schedulingDb.SaveChangesAsync());
    }

    [Fact]
    public async Task Authenticated_creation_rejects_an_inactive_catalog_entry_through_the_contract()
    {
        var unitId = Guid.NewGuid();
        var professional = new Professional(unitId, $"Inactive {Guid.NewGuid():N}");
        professional.SetActive(false);
        var service = new Service(unitId, $"Service {Guid.NewGuid():N}", 30);
        await using (var catalogDb = fixture.CreateBusinessCatalogDb())
        {
            catalogDb.AddRange(professional, service, new ProfessionalService(unitId, professional.Id, service.Id));
            await catalogDb.SaveChangesAsync();
        }
        await using var schedulingDb = fixture.CreateSchedulingDb(new TestTenant(unitId));
        var controller = new SchedulingController(schedulingDb, new TestTenant(unitId), new AvailabilitySlotCalculator(),
            new NoOpPublisher(), new NoOpLogWriter(), new UnlimitedGuard(), new AllowAllSchedulingScopeAuthorization(), businessCatalog: fixture.CreateBusinessCatalogReader(new TestTenant(unitId)));
        var startsAtUtc = DateTime.UtcNow.AddDays(3);

        var result = await controller.CreateAppointment(new CreateAppointmentRequest(
            professional.Id, service.Id, "Customer", "contact", startsAtUtc, startsAtUtc.AddMinutes(30)), default);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.False(await schedulingDb.Appointments.AnyAsync(x => x.TenantId == unitId));
    }

    [Fact]
    public async Task Appointment_conflict_uses_problem_json_without_appointment_identity_or_customer_data()
    {
        var unitId = Guid.NewGuid();
        var professional = new Professional(unitId, $"Conflict {Guid.NewGuid():N}");
        var service = new Service(unitId, $"Conflict service {Guid.NewGuid():N}", 30);
        var startsAtUtc = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);
        var existing = new Appointment(unitId, professional.Id, service.Id, "Sensitive customer", "secret@example.test",
            startsAtUtc, startsAtUtc.AddMinutes(30));
        var settings = new SchedulingSettings(unitId);
        settings.Update(15, 0, 0, "UTC", ConflictMode.WarnAndConfirm, 2);
        await using (var catalogDb = fixture.CreateBusinessCatalogDb())
        {
            catalogDb.AddRange(professional, service, new ProfessionalService(unitId, professional.Id, service.Id));
            await catalogDb.SaveChangesAsync();
        }
        await using var db = fixture.CreateSchedulingDb(new TestTenant(unitId));
        db.AddRange(existing, settings);
        await db.SaveChangesAsync();
        var controller = CreateController(db, unitId);

        var response = await controller.CreateAppointment(new CreateAppointmentRequest(
            professional.Id, service.Id, "New customer", "new@example.test", startsAtUtc, startsAtUtc.AddMinutes(30)), default);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Contains("application/problem+json", result.ContentTypes);
        var problem = Assert.IsType<SchedulingProblemDetails>(result.Value);
        Assert.Equal(SchedulingProblemCodes.AppointmentConflict, problem.Code);
        var details = Assert.IsType<AppointmentConflictDetails>(problem.Details);
        Assert.True(details.ConfirmationRequired);
        Assert.Single(details.ConflictingIntervals);
        var serialized = JsonSerializer.Serialize(problem);
        Assert.DoesNotContain(existing.Id.ToString(), serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sensitive customer", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret@example.test", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capacity_problem_reports_minimal_ordered_saturated_buckets()
    {
        var unitId = Guid.NewGuid();
        var professional = new Professional(unitId, $"Capacity {Guid.NewGuid():N}");
        var service = new Service(unitId, $"Capacity service {Guid.NewGuid():N}", 30);
        var startsAtUtc = new DateTime(2026, 9, 11, 10, 10, 0, DateTimeKind.Utc);
        var settings = new SchedulingSettings(unitId);
        settings.Update(15, 0, 0, "UTC", ConflictMode.Allow, 1);
        await using (var catalogDb = fixture.CreateBusinessCatalogDb())
        {
            catalogDb.AddRange(professional, service, new ProfessionalService(unitId, professional.Id, service.Id));
            await catalogDb.SaveChangesAsync();
        }
        await using var db = fixture.CreateSchedulingDb(new TestTenant(unitId));
        db.AddRange(settings, new Appointment(unitId, professional.Id, service.Id, "Existing", "contact",
            startsAtUtc.AddMinutes(-3), startsAtUtc.AddMinutes(10)));
        await db.SaveChangesAsync();
        var controller = CreateController(db, unitId);

        var response = await controller.CreateAppointment(new CreateAppointmentRequest(
            professional.Id, service.Id, "New", "contact", startsAtUtc, startsAtUtc.AddMinutes(30)), default);

        var problem = Assert.IsType<SchedulingProblemDetails>(Assert.IsType<ObjectResult>(response.Result).Value);
        Assert.Equal(SchedulingProblemCodes.CapacityExceeded, problem.Code);
        var details = Assert.IsType<CapacityExceededDetails>(problem.Details);
        Assert.Equal(1, details.EffectiveCapacity);
        Assert.Equal(2, details.ResultingPeakOccupancy);
        Assert.Equal(2, details.SaturatedBuckets.Count);
        Assert.True(details.SaturatedBuckets.SequenceEqual(details.SaturatedBuckets.OrderBy(x => x.StartUtc)));
    }

    [Fact]
    public async Task Schedule_block_problem_exposes_intervals_but_not_internal_reason()
    {
        var unitId = Guid.NewGuid();
        var professional = new Professional(unitId, $"Blocked {Guid.NewGuid():N}");
        var service = new Service(unitId, $"Blocked service {Guid.NewGuid():N}", 30);
        var startsAtUtc = new DateTime(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);
        await using (var catalogDb = fixture.CreateBusinessCatalogDb())
        {
            catalogDb.AddRange(professional, service, new ProfessionalService(unitId, professional.Id, service.Id));
            await catalogDb.SaveChangesAsync();
        }
        await using var db = fixture.CreateSchedulingDb(new TestTenant(unitId));
        db.Add(new ScheduleBlock(unitId, professional.Id, startsAtUtc, startsAtUtc.AddMinutes(30), "Internal reason"));
        await db.SaveChangesAsync();

        var response = await CreateController(db, unitId).CreateAppointment(new CreateAppointmentRequest(
            professional.Id, service.Id, "Customer", "contact", startsAtUtc, startsAtUtc.AddMinutes(30)), default);

        var problem = Assert.IsType<SchedulingProblemDetails>(Assert.IsType<ObjectResult>(response.Result).Value);
        Assert.Equal(SchedulingProblemCodes.ScheduleBlocked, problem.Code);
        Assert.Single(Assert.IsType<ScheduleBlockedDetails>(problem.Details).BlockedIntervals);
        Assert.DoesNotContain("Internal reason", JsonSerializer.Serialize(problem), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stale_version_and_invalid_transition_have_distinct_typed_contracts()
    {
        var unitId = Guid.NewGuid();
        var appointment = new Appointment(unitId, Guid.NewGuid(), Guid.NewGuid(), "Customer", "contact",
            DateTime.UtcNow.AddDays(4), DateTime.UtcNow.AddDays(4).AddMinutes(30));
        await using var db = fixture.CreateSchedulingDb(new TestTenant(unitId));
        db.Add(appointment);
        await db.SaveChangesAsync();
        var controller = CreateController(db, unitId);

        var staleResponse = await controller.RescheduleAppointment(appointment.Id,
            new RescheduleAppointmentRequest(appointment.StartsAtUtc.AddHours(1), appointment.EndsAtUtc.AddHours(1), Guid.NewGuid()), default);
        var staleResult = Assert.IsType<ObjectResult>(staleResponse.Result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, staleResult.StatusCode);
        var stale = Assert.IsType<SchedulingProblemDetails>(staleResult.Value);
        Assert.Equal(SchedulingProblemCodes.StaleAppointment, stale.Code);
        Assert.Equal(appointment.Version, Assert.IsType<StaleAppointmentDetails>(stale.Details).CurrentVersion);

        var transitionResult = Assert.IsType<ObjectResult>(await controller.ChangeAppointmentStatus(
            appointment.Id, new ChangeAppointmentStatusRequest(AppointmentStatus.Scheduled), default));
        Assert.Equal(StatusCodes.Status409Conflict, transitionResult.StatusCode);
        var transition = Assert.IsType<SchedulingProblemDetails>(transitionResult.Value);
        Assert.Equal(SchedulingProblemCodes.InvalidAppointmentTransition, transition.Code);
        Assert.Contains(AppointmentStatus.Confirmed,
            Assert.IsType<InvalidAppointmentTransitionDetails>(transition.Details).AllowedTransitions);
    }

    private SchedulingController CreateController(SchedulingDbContext db, Guid unitId) =>
        new(db, new TestTenant(unitId), new AvailabilitySlotCalculator(), new NoOpPublisher(), new NoOpLogWriter(),
            new UnlimitedGuard(), new AllowAllSchedulingScopeAuthorization(),
            businessCatalog: fixture.CreateBusinessCatalogReader(new TestTenant(unitId)));

    private sealed record TestTenant(Guid UnitId) : ICurrentTenant
    {
        public Guid? TenantId => UnitId;
        public Guid? UserTenantId => Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => true;
    }

    private sealed class NotConfiguredGuard : IEntitlementLimitGuard
    {
        public Task<EntitlementLimitDecision> TryReserveAsync(Guid unitId, string key, long quantity = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EntitlementLimitDecision(key, EntitlementLimitStatus.NotConfigured, 0, quantity, null, null));
        public Task ReleaseAsync(Guid unitId, string key, long quantity = 1, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<EntitlementLimitDecision> TryReserveAsync(Guid unitId, string key, Guid operationId, long quantity = 1, CancellationToken cancellationToken = default) => TryReserveAsync(unitId, key, quantity, cancellationToken);
        public Task ReleaseAsync(Guid unitId, string key, Guid operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReconcileAsync(Guid unitId, string key, IReadOnlySet<Guid> activeOperationIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    private sealed class AllowAllSchedulingScopeAuthorization : ISchedulingScopeAuthorization
    {
        public Task<SchedulingScopeDecision> AuthorizeAsync(string permissionCode, Guid? requestedProfessionalId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(new SchedulingScopeDecision(
                SchedulingScopeDecisionStatus.Allowed, Shine.Domain.Authorization.PermissionScope.All, requestedProfessionalId));
        public Task<SchedulingScopeDecision> AuthorizeAllAsync(string permissionCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SchedulingScopeDecision(SchedulingScopeDecisionStatus.Allowed, Shine.Domain.Authorization.PermissionScope.All));
    }

    private sealed class CapturingPublisher : IAppointmentEventPublisher
    {
        public List<AppointmentEvent> Events { get; } = [];
        public Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(appointmentEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpLogWriter : IOperationalLogWriter
    {
        public Task WriteAsync(string level, string category, string message, Exception? exception = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
