using System.Text.RegularExpressions;

namespace Shine.ArchitectureTests;

public sealed class RuntimeDocumentationTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Runtime_documentation_covers_registered_workers_and_persistence_contexts()
    {
        var documentationPath = Path.Combine(Root, "docs", "runtime-and-operations.md");
        var documentation = File.ReadAllText(documentationPath);
        var sourceRoots = new[] { "host", "modules", Path.Combine("platform", "Core", "src") };
        var source = sourceRoots
            .SelectMany(path => Directory.GetFiles(Path.Combine(Root, path), "*.cs", SearchOption.AllDirectories))
            .Select(File.ReadAllText)
            .ToArray();

        var workers = DeclaredTypes(source, @"class\s+(?<name>\w+)[^{;]{0,800}:\s*BackgroundService");
        var contexts = DeclaredTypes(source, @"class\s+(?<name>\w+)[^{;]{0,800}:\s*DbContext");

        Assert.NotEmpty(workers);
        Assert.NotEmpty(contexts);
        Assert.All(workers.Concat(contexts), name => Assert.Contains($"`{name}`", documentation));
        Assert.Contains("docs/runtime-and-operations.md", File.ReadAllText(Path.Combine(Root, "README.md")));
    }

    private static string[] DeclaredTypes(IEnumerable<string> sources, string pattern) => sources
        .SelectMany(source => Regex.Matches(source, pattern, RegexOptions.Singleline)
            .Select(match => match.Groups["name"].Value))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Shine.Backend.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
