namespace Shine.Domain;

/// <summary>Stable, case-insensitive identifier used to reference a platform module.</summary>
public readonly record struct ModuleCode
{
    public string Value { get; }

    public ModuleCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Module code is required.", nameof(value));

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Module code may contain only letters, numbers, '-' or '_'.", nameof(value));

        Value = normalized;
    }

    public override string ToString() => Value;
    public static implicit operator string(ModuleCode code) => code.Value;
}

public sealed record ModuleDescriptor(
    ModuleCode Code,
    string Name,
    string Description,
    string Version,
    IReadOnlyCollection<ModuleCode> Dependencies)
{
    public IReadOnlyCollection<ModuleEndpoint> Endpoints { get; init; } = [];
    public IReadOnlyCollection<ModuleService> Services { get; init; } = [];

    public static ModuleDescriptor Create(
        string code,
        string name,
        string description,
        string version = "1.0.0",
        IEnumerable<string>? dependencies = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Module name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Module description is required.", nameof(description));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Module version is required.", nameof(version));

        var dependencyCodes = (dependencies ?? [])
            .Select(static dependency => new ModuleCode(dependency))
            .Distinct()
            .ToArray();

        return new ModuleDescriptor(new ModuleCode(code), name.Trim(), description.Trim(), version.Trim(), dependencyCodes);
    }
}

public sealed record ModuleEndpoint(string Method, string Route);
public sealed record ModuleService(string Contract, string Lifetime);

public interface IModuleCatalog
{
    IReadOnlyCollection<ModuleDescriptor> Modules { get; }
    void Register(ModuleDescriptor module);
    ModuleDescriptor Get(ModuleCode code);
}

/// <summary>In-memory registry for statically deployed modules.</summary>
public sealed class ModuleCatalog : IModuleCatalog
{
    private readonly Dictionary<ModuleCode, ModuleDescriptor> modules = [];
    private readonly object sync = new();

    public IReadOnlyCollection<ModuleDescriptor> Modules
    {
        get { lock (sync) return modules.Values.OrderBy(module => module.Code.Value).ToArray(); }
    }

    public void Register(ModuleDescriptor module)
    {
        ArgumentNullException.ThrowIfNull(module);
        lock (sync)
        {
            if (modules.ContainsKey(module.Code))
                throw new InvalidOperationException($"Module '{module.Code}' is already registered.");
            if (module.Dependencies.Contains(module.Code))
                throw new InvalidOperationException($"Module '{module.Code}' cannot depend on itself.");
            if (module.Dependencies.Any(dependency => !modules.ContainsKey(dependency)))
                throw new InvalidOperationException($"Module '{module.Code}' has an unregistered dependency.");

            modules.Add(module.Code, module);
            if (HasCycle())
            {
                modules.Remove(module.Code);
                throw new InvalidOperationException($"Registering module '{module.Code}' would create a circular dependency.");
            }
        }
    }

    public ModuleDescriptor Get(ModuleCode code)
    {
        lock (sync)
            return modules.TryGetValue(code, out var module)
                ? module
                : throw new KeyNotFoundException($"Module '{code}' is not registered.");
    }

    private bool HasCycle()
    {
        var visiting = new HashSet<ModuleCode>();
        var visited = new HashSet<ModuleCode>();
        return modules.Keys.Any(code => Visit(code, visiting, visited));

        bool Visit(ModuleCode code, HashSet<ModuleCode> current, HashSet<ModuleCode> completed)
        {
            if (current.Contains(code)) return true;
            if (!completed.Add(code)) return false;
            current.Add(code);
            foreach (var dependency in modules[code].Dependencies)
                if (Visit(dependency, current, completed)) return true;
            current.Remove(code);
            return false;
        }
    }
}
