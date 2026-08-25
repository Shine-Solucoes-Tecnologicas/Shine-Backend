using System.Xml.Linq;

namespace Shine.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Theory]
    [InlineData("Billing", "Scheduling")]
    [InlineData("Scheduling", "Billing")]
    [InlineData("Billing", "BusinessCatalog")]
    [InlineData("BusinessCatalog", "Billing")]
    [InlineData("Scheduling", "BusinessCatalog")]
    [InlineData("BusinessCatalog", "Scheduling")]
    public void Feature_modules_reference_each_other_only_through_application_contracts(string module, string forbiddenModule)
    {
        var projectFiles = Directory.GetFiles(Path.Combine(Root, "modules", module), "*.csproj", SearchOption.AllDirectories);

        foreach (var projectFile in projectFiles)
            Assert.DoesNotContain(ProjectReferences(projectFile), reference =>
            {
                var normalized = Normalize(reference);
                var targetsModule = normalized.Contains($"/modules/{forbiddenModule}/", StringComparison.OrdinalIgnoreCase);
                var targetsApplication = normalized.Contains(
                    $"/modules/{forbiddenModule}/src/{forbiddenModule}.Application/",
                    StringComparison.OrdinalIgnoreCase);
                return targetsModule && !targetsApplication;
            });
    }

    [Fact]
    public void Domain_and_application_projects_do_not_reference_infrastructure_projects()
    {
        var projectFiles = Directory.GetFiles(Root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => path.Contains(".Domain", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains(".Application", StringComparison.OrdinalIgnoreCase));

        foreach (var projectFile in projectFiles)
            Assert.DoesNotContain(ProjectReferences(projectFile), reference =>
                reference.Contains(".Infrastructure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Shared_has_no_project_dependencies()
    {
        var sharedProject = Path.Combine(Root, "platform", "Shared", "src", "Shine.Shared", "Shine.Shared.csproj");
        Assert.Empty(ProjectReferences(sharedProject));
    }

    private static string[] ProjectReferences(string projectFile) =>
        XDocument.Load(projectFile)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(value!, Path.GetDirectoryName(projectFile)!))
            .ToArray();

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Shine.Backend.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
