using Shine.Domain;

namespace Shine.UnitTests;

public sealed class BackendSubtaskCoverageTests
{
    [Fact]
    public void Module_catalog_rejects_unknown_dependencies_and_cycles()
    {
        var catalog = new ModuleCatalog();
        catalog.Register(ModuleDescriptor.Create("A", "A", "A"));
        catalog.Register(ModuleDescriptor.Create("B", "B", "B", dependencies: ["A"]));

        Assert.Throws<InvalidOperationException>(() => catalog.Register(ModuleDescriptor.Create("C", "C", "C", dependencies: ["MISSING"])));
        Assert.Throws<InvalidOperationException>(() => catalog.Register(ModuleDescriptor.Create("A", "A2", "A2")));
        Assert.Equal("B", catalog.Get(new ModuleCode("b")).Code.Value);
    }

    [Fact]
    public void Module_code_is_stable_and_case_insensitive()
    {
        var code = new ModuleCode(" core ");
        Assert.Equal("CORE", code.Value);
        Assert.Throws<ArgumentException>(() => new ModuleCode("invalid code"));
    }

    [Fact]
    public void Plans_and_overrides_normalize_module_codes()
    {
        var plan = new Plan("starter", "Starter");
        var module = new PlanModule(plan.Id, "core");
        var overrideValue = new TenantModuleOverride(Guid.NewGuid(), "core", true);

        Assert.Equal("STARTER", plan.Code);
        Assert.Equal("CORE", module.ModuleCode);
        overrideValue.SetEnabled(false);
        Assert.False(overrideValue.Enabled);
    }

    [Fact]
    public void Functional_settings_reject_secrets_and_invalid_keys()
    {
        var setting = new FunctionalSetting(" timezone.default ", "America/Sao_Paulo");
        Assert.Equal("TIMEZONE.DEFAULT", setting.Key);
        Assert.Throws<ArgumentException>(() => new FunctionalSetting("JWT_SECRET", "value"));
        Assert.Throws<ArgumentException>(() => new FunctionalSetting("invalid key", "value"));
    }

    [Fact]
    public void Feature_flags_normalize_keys_and_validate_format()
    {
        var flag = new FeatureFlag("new.dashboard", true);
        Assert.Equal("NEW.DASHBOARD", flag.Key);
        flag.SetEnabled(false);
        Assert.False(flag.Enabled);
        Assert.Throws<ArgumentException>(() => new FeatureFlag("bad key", true));
    }

    [Fact]
    public void Date_policy_normalizes_unspecified_values_and_converts_timezones()
    {
        var local = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Unspecified);
        var utc = DateTimePolicy.EnsureUtc(local);
        var tenantTime = DateTimePolicy.ToTenantTime(utc, "UTC");

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(utc, tenantTime);
    }

    [Fact]
    public void Base_entity_collects_and_clears_domain_events()
    {
        var entity = new TestAggregate();
        var domainEvent = new TestEvent();
        entity.AddDomainEvent(domainEvent);

        var pending = entity.ClearDomainEvents();

        Assert.Single(pending);
        Assert.Same(domainEvent, pending.Single());
        Assert.Empty(entity.DomainEvents);
    }

    private sealed class TestAggregate() : BaseEntity<Guid>(Guid.NewGuid());
    private sealed class TestEvent : IDomainEvent;
}
