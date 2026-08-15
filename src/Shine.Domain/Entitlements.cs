namespace Shine.Domain;

public sealed record EntitlementSnapshot(
    Guid UnitId,
    IReadOnlySet<string> Modules,
    IReadOnlyDictionary<string, long> Limits)
{
    public bool HasModule(string moduleCode) => Modules.Contains(new ModuleCode(moduleCode).Value);

    public bool Allows(string limitCode, long requested = 1)
    {
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        return !Limits.TryGetValue(limitCode.Trim().ToUpperInvariant(), out var maximum) || requested <= maximum;
    }
}
