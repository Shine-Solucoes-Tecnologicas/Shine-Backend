[CmdletBinding()]
param(
    [ValidateSet('Prepare', 'Check', 'RunApi', 'RunFrontend', 'Stop')]
    [string] $Action = 'Prepare',
    [string] $ConnectionString = 'Host=localhost;Port=5433;Database=shine;Username=shine;Password=shine',
    [string] $FrontendPath,
    [ValidateRange(5, 300)]
    [int] $ReadinessTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($FrontendPath)) {
    $FrontendPath = Join-Path (Split-Path -Parent $repositoryRoot) 'Shine-Frontend'
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string] $Command,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $FailureMessage
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)."
    }
}

function Assert-Command {
    param([Parameter(Mandatory)] [string] $Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

function Assert-DockerAvailable {
    Assert-Command 'docker'
    & docker version --format '{{.Server.Version}}' *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Engine is not available. Start Docker Desktop and rerun this command."
    }
}

function Assert-PostgresReady {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
    do {
        & docker compose exec -T postgres pg_isready -U shine -d shine *> $null
        if ($LASTEXITCODE -eq 0) {
            Write-Host 'PostgreSQL is ready.'
            return
        }

        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    & docker compose ps
    throw "PostgreSQL did not become ready within $ReadinessTimeoutSeconds seconds. Run 'docker compose logs postgres' for details."
}

function Invoke-Migrations {
    $contexts = @(
        @{ Name = 'Core'; Context = 'ShineDbContext'; Project = 'platform/Core/src/Shine.Infrastructure' },
        @{ Name = 'Scheduling'; Context = 'SchedulingDbContext'; Project = 'modules/Scheduling/src/Scheduling.Infrastructure' },
        @{ Name = 'Business Catalog'; Context = 'BusinessCatalogDbContext'; Project = 'modules/BusinessCatalog/src/BusinessCatalog.Infrastructure' },
        @{ Name = 'Billing'; Context = 'BillingDbContext'; Project = 'modules/Billing/src/Billing.Infrastructure' }
    )

    foreach ($item in $contexts) {
        Write-Host "Applying $($item.Name) migrations..."
        Invoke-Checked -Command 'dotnet' -Arguments @(
            'ef', 'database', 'update',
            '--project', $item.Project,
            '--startup-project', 'host/Shine.Api',
            '--context', $item.Context,
            '--no-build'
        ) -FailureMessage "$($item.Name) migrations failed"
    }
}

Push-Location $repositoryRoot
try {
    switch ($Action) {
        'Prepare' {
            Assert-DockerAvailable
            Assert-Command 'dotnet'
            Invoke-Checked -Command 'docker' -Arguments @('compose', 'up', '-d', 'postgres', 'rabbitmq') -FailureMessage 'Could not start local dependencies'
            Assert-PostgresReady
            Invoke-Checked -Command 'dotnet' -Arguments @('tool', 'restore') -FailureMessage 'Could not restore local .NET tools'
            Invoke-Checked -Command 'dotnet' -Arguments @('restore', 'Shine.Backend.slnx') -FailureMessage 'Could not restore backend dependencies'
            Invoke-Checked -Command 'dotnet' -Arguments @('build', 'Shine.Backend.slnx', '--no-restore', '--configuration', 'Debug', '--nologo') -FailureMessage 'Could not build the backend before applying migrations'

            $previousConnectionString = [Environment]::GetEnvironmentVariable('ConnectionStrings__ShineDb', 'Process')
            [Environment]::SetEnvironmentVariable('ConnectionStrings__ShineDb', $ConnectionString, 'Process')
            try {
                Invoke-Migrations
            }
            finally {
                [Environment]::SetEnvironmentVariable('ConnectionStrings__ShineDb', $previousConnectionString, 'Process')
            }

            Write-Host 'Local backend prepared. Run this script with -Action RunApi, then RunFrontend in another terminal.'
        }
        'Check' {
            Assert-DockerAvailable
            Assert-PostgresReady
            Write-Host 'Local dependencies are ready for the application and integration tests.'
        }
        'RunApi' {
            Assert-DockerAvailable
            Assert-Command 'dotnet'
            Assert-PostgresReady
            [Environment]::SetEnvironmentVariable('ConnectionStrings__ShineDb', $ConnectionString, 'Process')
            Invoke-Checked -Command 'dotnet' -Arguments @('run', '--project', 'host/Shine.Api', '--launch-profile', 'http', '--no-restore') -FailureMessage 'The API stopped with an error'
        }
        'RunFrontend' {
            if (-not (Test-Path -LiteralPath (Join-Path $FrontendPath 'package.json'))) {
                throw "Frontend not found at '$FrontendPath'. Clone Shine-Frontend beside this repository or pass -FrontendPath."
            }

            $npmCommand = if (Get-Command 'npm.cmd' -ErrorAction SilentlyContinue) { 'npm.cmd' } else { 'npm' }
            Assert-Command $npmCommand
            Push-Location $FrontendPath
            try {
                if (-not (Test-Path -LiteralPath 'node_modules')) {
                    Invoke-Checked -Command $npmCommand -Arguments @('ci') -FailureMessage 'Could not restore frontend dependencies'
                }
                Invoke-Checked -Command $npmCommand -Arguments @('run', 'dev') -FailureMessage 'The frontend stopped with an error'
            }
            finally {
                Pop-Location
            }
        }
        'Stop' {
            Assert-DockerAvailable
            Invoke-Checked -Command 'docker' -Arguments @('compose', 'stop') -FailureMessage 'Could not stop local dependencies'
        }
    }
}
finally {
    Pop-Location
}
