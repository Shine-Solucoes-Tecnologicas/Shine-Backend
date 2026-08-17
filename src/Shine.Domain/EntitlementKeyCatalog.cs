namespace Shine.Domain;

public static class EntitlementKeys
{
    public const string SchedulingActiveAppointments = "APPOINTMENTS.MAX";
    public const string SchedulingProfessionals = "PROFESSIONALS.MAX";
    public const string SchedulingServices = "SERVICES.MAX";
}

public sealed record EntitlementKeyDefinition(string Key, string ModuleCode, string Description);

public interface IEntitlementKeyCatalog
{
    IReadOnlyCollection<EntitlementKeyDefinition> Keys { get; }
    bool Contains(string key);
}

public sealed class EntitlementKeyCatalog : IEntitlementKeyCatalog
{
    private readonly IReadOnlyDictionary<string, EntitlementKeyDefinition> keys = new[]
    {
        new EntitlementKeyDefinition(EntitlementKeys.SchedulingActiveAppointments, "SCHEDULING", "Quantidade máxima de agendamentos ativos por unidade."),
        new EntitlementKeyDefinition(EntitlementKeys.SchedulingProfessionals, "SCHEDULING", "Quantidade máxima de profissionais ativos por unidade."),
        new EntitlementKeyDefinition(EntitlementKeys.SchedulingServices, "SCHEDULING", "Quantidade máxima de serviços ativos por unidade.")
    }.ToDictionary(x => PlanEntitlement.NormalizeKey(x.Key), StringComparer.Ordinal);

    public IReadOnlyCollection<EntitlementKeyDefinition> Keys => keys.Values.ToArray();
    public bool Contains(string key) => keys.ContainsKey(PlanEntitlement.NormalizeKey(key));
}
