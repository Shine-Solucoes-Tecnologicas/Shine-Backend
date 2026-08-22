param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [switch] $NoBuild,
    [switch] $CollectCoverage
)

$ErrorActionPreference = 'Stop'
$projects = @(
    'tests/Shine.ArchitectureTests/Shine.ArchitectureTests.csproj',
    'platform/Core/tests/Shine.Core.UnitTests/Shine.Core.UnitTests.csproj',
    'modules/Billing/tests/Billing.UnitTests/Billing.UnitTests.csproj',
    'modules/Scheduling/tests/Scheduling.UnitTests/Scheduling.UnitTests.csproj',
    'platform/Core/tests/Shine.Core.IntegrationTests/Shine.Core.IntegrationTests.csproj',
    'modules/Billing/tests/Billing.IntegrationTests/Billing.IntegrationTests.csproj',
    'modules/Scheduling/tests/Scheduling.IntegrationTests/Scheduling.IntegrationTests.csproj'
)

foreach ($project in $projects) {
    $arguments = @('test', $project, '--configuration', $Configuration, '--nologo', '--logger:console;verbosity=minimal')
    if ($NoBuild) { $arguments += @('--no-build', '--no-restore') }
    if ($CollectCoverage -and $project -notlike '*Shine.ArchitectureTests*') {
        $arguments += '--collect:XPlat Code Coverage'
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
