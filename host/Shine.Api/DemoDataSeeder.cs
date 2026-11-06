using BusinessCatalog.Domain;
using BusinessCatalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scheduling.Domain;
using Scheduling.Infrastructure;
using Shine.Domain;
using Shine.Domain.Authorization;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure.Persistence.Seed;

namespace Shine.Api;

public interface IDemoDataTargetGuard
{
    void EnsureAllowed();
}

public sealed class DemoDataTargetGuard(IConfiguration configuration, IHostEnvironment environment) : IDemoDataTargetGuard
{
    public void EnsureAllowed()
    {
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("DemoData:Enabled"))
            throw new InvalidOperationException("Demo data is available only in Development with DemoData:Enabled=true.");

        var raw = configuration.GetConnectionString("ShineDb")
            ?? throw new InvalidOperationException("ConnectionStrings:ShineDb is required.");
        var connection = new NpgsqlConnectionStringBuilder(raw);
        var localHost = string.Equals(connection.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(connection.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(connection.Host, "::1", StringComparison.OrdinalIgnoreCase);
        if (!localHost || !string.Equals(connection.Database, DemoDataSeeder.DatabaseName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Demo data refuses this target. Use local database '{DemoDataSeeder.DatabaseName}'.");
    }
}

public sealed class DemoDataSeeder(
    ShineDbContext core,
    BusinessCatalogDbContext catalog,
    SchedulingDbContext scheduling,
    ITenantExecutionContext tenantExecutionContext,
    IPasswordHashService passwordHashService,
    IConfiguration configuration,
    IDemoDataTargetGuard targetGuard)
{
    public const string DatabaseName = "shine_demo";
    public const string AccountName = "Shine Demo — Aurora Bem-Estar";
    public const string AdministratorEmail = "admin.demo@example.test";
    public const string ViewerEmail = "viewer.demo@example.test";
    public const string EditorEmail = "editor.demo@example.test";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        targetGuard.EnsureAllowed();
        var password = configuration["DemoData:Password"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new InvalidOperationException("DemoData:Password must be supplied at runtime with at least 12 characters.");

        var administrator = await EnsureUserAsync(AdministratorEmail, password, cancellationToken);
        var viewer = await EnsureUserAsync(ViewerEmail, password, cancellationToken);
        var editor = await EnsureUserAsync(EditorEmail, password, cancellationToken);
        var account = await EnsureAccountAsync(cancellationToken);
        var downtown = await EnsureUnitAsync(account.Id, "Aurora Centro", cancellationToken);
        var gardens = await EnsureUnitAsync(account.Id, "Aurora Jardins", cancellationToken);

        await EnsureAccountMembershipAsync(account.Id, administrator.Id, cancellationToken);
        await EnsureAccountMembershipAsync(account.Id, viewer.Id, cancellationToken);
        await EnsureAccountMembershipAsync(account.Id, editor.Id, cancellationToken);
        await EnsureUnitMembershipAsync(administrator.Id, downtown.Id, administrator.Id, isOwner: true, cancellationToken);
        await EnsureUnitMembershipAsync(administrator.Id, gardens.Id, administrator.Id, isOwner: true, cancellationToken);
        await EnsureUnitMembershipAsync(viewer.Id, downtown.Id, administrator.Id, isOwner: false, cancellationToken);
        await EnsureUnitMembershipAsync(editor.Id, gardens.Id, administrator.Id, isOwner: false, cancellationToken);

        await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(core, account.Id, administrator.Id, cancellationToken);
        using (tenantExecutionContext.EnterTenant(downtown.Id))
            await AuthorizationSeed.SeedTenantDefaultsAsync(core, downtown.Id, administrator.Id, administrator.Id, cancellationToken);
        using (tenantExecutionContext.EnterTenant(gardens.Id))
            await AuthorizationSeed.SeedTenantDefaultsAsync(core, gardens.Id, administrator.Id, administrator.Id, cancellationToken);
        await EnsureLimitedRoleAsync(account.Id, viewer.Id, CustomerAccountRole.ViewerName, downtown.Id, cancellationToken);
        await EnsureLimitedRoleAsync(account.Id, editor.Id, CustomerAccountRole.EditorName, gardens.Id, cancellationToken);
        using (tenantExecutionContext.EnterTenant(downtown.Id))
            await EnsureLegacyMemberRoleAsync(downtown.Id, viewer.Id, cancellationToken);
        using (tenantExecutionContext.EnterTenant(gardens.Id))
            await EnsureLegacyMemberRoleAsync(gardens.Id, editor.Id, cancellationToken);

        await SeedUnitAsync(downtown.Id, null, "Marina Costa", "Corte Essencial", 45, cancellationToken);
        await SeedUnitAsync(gardens.Id, editor.Id, "Rafael Lima", "Terapia Relaxante", 60, cancellationToken);
    }

    private async Task<User> EnsureUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        var normalized = User.NormalizeEmail(email);
        var user = await core.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, cancellationToken);
        if (user is null)
        {
            user = new User(email, passwordHashService.Hash(password));
            core.Users.Add(user);
        }
        else
        {
            user.ChangePassword(passwordHashService.Hash(password));
            user.VerifyEmail(DateTime.UtcNow);
        }
        await core.SaveChangesAsync(cancellationToken);
        return user;
    }

    private async Task<CustomerAccount> EnsureAccountAsync(CancellationToken cancellationToken)
    {
        var account = await core.CustomerAccounts.SingleOrDefaultAsync(x => x.Name == AccountName, cancellationToken);
        if (account is not null) return account;
        account = new CustomerAccount(AccountName);
        core.CustomerAccounts.Add(account);
        await core.SaveChangesAsync(cancellationToken);
        return account;
    }

    private async Task<Tenant> EnsureUnitAsync(Guid accountId, string name, CancellationToken cancellationToken)
    {
        var unit = await core.Tenants.SingleOrDefaultAsync(x => x.CustomerAccountId == accountId && x.Name == name, cancellationToken);
        if (unit is not null) return unit;
        unit = new Tenant(name);
        unit.AssignToCustomerAccount(accountId);
        core.Tenants.Add(unit);
        await core.SaveChangesAsync(cancellationToken);
        return unit;
    }

    private async Task EnsureAccountMembershipAsync(Guid accountId, Guid userId, CancellationToken cancellationToken)
    {
        if (await core.CustomerAccountUsers.AnyAsync(x => x.AccountId == accountId && x.UserId == userId, cancellationToken)) return;
        core.CustomerAccountUsers.Add(new CustomerAccountUser(accountId, userId));
        await core.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureUnitMembershipAsync(Guid userId, Guid tenantId, Guid creatorId, bool isOwner, CancellationToken cancellationToken)
    {
        if (await core.UserTenants.AnyAsync(x => x.UserId == userId && x.TenantId == tenantId, cancellationToken)) return;
        core.UserTenants.Add(new UserTenant(userId, tenantId, creatorId, isOwner));
        await core.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureLimitedRoleAsync(Guid accountId, Guid userId, string roleName, Guid unitId, CancellationToken cancellationToken)
    {
        var role = await core.CustomerAccountRoles.SingleAsync(x => x.AccountId == accountId && x.Name == roleName, cancellationToken);
        var assignment = await core.CustomerAccountUserRoles
            .Include(x => x.Units)
            .Include(x => x.Modules)
            .SingleOrDefaultAsync(x => x.AccountId == accountId && x.UserId == userId && x.RoleId == role.Id, cancellationToken);
        if (assignment is null)
        {
            assignment = new CustomerAccountUserRole(accountId, userId, role.Id);
            core.CustomerAccountUserRoles.Add(assignment);
        }
        assignment.ReplaceScope(allUnits: false, allModules: true, [unitId], []);
        await core.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureLegacyMemberRoleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var role = await AuthorizationSeed.EnsureMemberRoleAsync(core, tenantId, cancellationToken);
        if (await core.UserTenantRoles.AnyAsync(x => x.TenantId == tenantId && x.UserId == userId && x.RoleId == role.Id, cancellationToken)) return;
        core.UserTenantRoles.Add(new UserTenantRole(userId, tenantId, role.Id));
        await core.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedUnitAsync(Guid tenantId, Guid? professionalUserId, string professionalName, string serviceName, int durationMinutes, CancellationToken cancellationToken)
    {
        using var tenantScope = tenantExecutionContext.EnterTenant(tenantId);
        var customer = await core.Customers.SingleOrDefaultAsync(x => x.Name == "Cliente Demonstração", cancellationToken);
        if (customer is null)
        {
            customer = new Customer(tenantId, "Cliente Demonstração", $"cliente.{tenantId:N}@example.test", "11900000000");
            core.Customers.Add(customer);
            await core.SaveChangesAsync(cancellationToken);
        }

        var professional = await catalog.Professionals.SingleOrDefaultAsync(x => x.Name == professionalName, cancellationToken);
        if (professional is null)
        {
            professional = new Professional(tenantId, professionalName, professionalUserId);
            catalog.Professionals.Add(professional);
        }
        var service = await catalog.Services.SingleOrDefaultAsync(x => x.Name == serviceName, cancellationToken);
        if (service is null)
        {
            service = new Service(tenantId, serviceName, durationMinutes);
            catalog.Services.Add(service);
        }
        await catalog.SaveChangesAsync(cancellationToken);
        if (!await catalog.ProfessionalServices.AnyAsync(x => x.ProfessionalId == professional.Id && x.ServiceId == service.Id, cancellationToken))
        {
            catalog.ProfessionalServices.Add(new ProfessionalService(tenantId, professional.Id, service.Id));
            await catalog.SaveChangesAsync(cancellationToken);
        }

        if (!await scheduling.SchedulingSettings.AnyAsync(cancellationToken))
            scheduling.SchedulingSettings.Add(new SchedulingSettings(tenantId));
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            if (!await scheduling.AvailabilityRules.AnyAsync(x => x.ProfessionalId == professional.Id && x.DayOfWeek == day, cancellationToken))
                scheduling.AvailabilityRules.Add(new AvailabilityRule(tenantId, professional.Id, day, TimeSpan.FromHours(9), TimeSpan.FromHours(17)));
        await scheduling.SaveChangesAsync(cancellationToken);

        if (await scheduling.Appointments.AnyAsync(x => x.CustomerId == customer.Id, cancellationToken)) return;
        var firstStart = NextBusinessDayAtUtc(10);
        var scheduled = new Appointment(tenantId, professional.Id, service.Id, customer.Name, customer.Email!, firstStart, firstStart.AddMinutes(durationMinutes), customer.Id);
        var confirmedStart = firstStart.AddHours(2);
        var confirmed = new Appointment(tenantId, professional.Id, service.Id, customer.Name, customer.Email!, confirmedStart, confirmedStart.AddMinutes(durationMinutes), customer.Id);
        confirmed.ChangeStatus(AppointmentStatus.Confirmed);
        var cancelledStart = firstStart.AddDays(1);
        var cancelled = new Appointment(tenantId, professional.Id, service.Id, customer.Name, customer.Email!, cancelledStart, cancelledStart.AddMinutes(durationMinutes), customer.Id);
        cancelled.ChangeStatus(AppointmentStatus.Cancelled);
        scheduling.Appointments.AddRange(scheduled, confirmed, cancelled);
        await scheduling.SaveChangesAsync(cancellationToken);
    }

    private static DateTime NextBusinessDayAtUtc(int hour)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone).Date.AddDays(1);
        while (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) local = local.AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.AddHours(hour), DateTimeKind.Unspecified), timeZone);
    }
}
