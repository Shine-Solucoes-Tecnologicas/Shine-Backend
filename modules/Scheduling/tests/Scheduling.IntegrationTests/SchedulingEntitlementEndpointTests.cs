using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scheduling.Application;
using Scheduling.Domain;
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
            new NoOpPublisher(), new NoOpLogWriter(), new NotConfiguredGuard(), businessCatalog: fixture.CreateBusinessCatalogReader(new TestTenant(unitId)));
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
                new NoOpPublisher(), new NoOpLogWriter(), new UnlimitedGuard(), businessCatalog: fixture.CreateBusinessCatalogReader(new TestTenant(unitId)));
            return await controller.CreateAppointment(request, default);
        }

        var results = await Task.WhenAll(CreateAsync(), CreateAsync());
        Assert.Single(results, x => x.Result is OkObjectResult);
        var conflict = Assert.Single(results, x => x.Result is ConflictObjectResult);
        Assert.Contains("CAPACITY_EXCEEDED", JsonSerializer.Serialize(Assert.IsType<ConflictObjectResult>(conflict.Result).Value));
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
                new NoOpPublisher(), new NoOpLogWriter(), new UnlimitedGuard(), businessCatalog: fixture.CreateBusinessCatalogReader(new TestTenant(unitId)));
            return await controller.RescheduleAppointment(id,
                new RescheduleAppointmentRequest(target, target.AddMinutes(30), version), default);
        }

        var results = await Task.WhenAll(RescheduleAsync(first.Id, first.Version), RescheduleAsync(second.Id, second.Version));
        Assert.Single(results, x => x.Result is OkObjectResult);
        Assert.Single(results, x => x.Result is ConflictObjectResult);
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
            publisher, new NoOpLogWriter(), new UnlimitedGuard(), validator, fixture.CreateBusinessCatalogReader(new TestTenant(tenant.Id)));
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
            new NoOpPublisher(), new NoOpLogWriter(), new UnlimitedGuard(), new CustomerManagement(customerDb), fixture.CreateBusinessCatalogReader(new TestTenant(tenant.Id)));
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
            new NoOpPublisher(), new NoOpLogWriter(), new UnlimitedGuard(), businessCatalog: fixture.CreateBusinessCatalogReader(new TestTenant(unitId)));
        var startsAtUtc = DateTime.UtcNow.AddDays(3);

        var result = await controller.CreateAppointment(new CreateAppointmentRequest(
            professional.Id, service.Id, "Customer", "contact", startsAtUtc, startsAtUtc.AddMinutes(30)), default);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.False(await schedulingDb.Appointments.AnyAsync(x => x.TenantId == unitId));
    }

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
