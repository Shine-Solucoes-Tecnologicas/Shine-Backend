[CmdletBinding()]
param(
    [Parameter()]
    [string] $Solution = './Shine.Backend.slnx'
)

$ErrorActionPreference = 'Stop'

$reportLines = & dotnet list $Solution package `
    --vulnerable `
    --include-transitive `
    --format json `
    --output-version 1 `
    --no-restore

if ($LASTEXITCODE -ne 0) {
    Write-Error 'NuGet vulnerability audit could not be completed. The pipeline fails closed when advisory data is unavailable.'
    exit 1
}

try {
    $report = ($reportLines -join [Environment]::NewLine) | ConvertFrom-Json
}
catch {
    Write-Error 'NuGet returned an invalid vulnerability report.'
    exit 1
}

$reportErrors = @($report.problems | Where-Object { $_.level -eq 'error' })
if ($reportErrors.Count -gt 0) {
    Write-Error 'NuGet reported an error while obtaining vulnerability data.'
    exit 1
}

$findings = @(
    foreach ($project in @($report.projects)) {
        foreach ($framework in @($project.frameworks)) {
            if ($null -eq $framework) {
                continue
            }

            foreach ($packageGroup in @('topLevelPackages', 'transitivePackages')) {
                foreach ($package in @($framework.$packageGroup)) {
                    if ($null -eq $package) {
                        continue
                    }

                    foreach ($vulnerability in @($package.vulnerabilities)) {
                        if ($null -eq $vulnerability) {
                            continue
                        }

                        [PSCustomObject]@{
                            Project     = $project.path
                            Framework   = $framework.framework
                            Package     = $package.id
                            Version     = $package.resolvedVersion
                            Severity    = $vulnerability.severity
                            AdvisoryUrl = $vulnerability.advisoryUrl
                        }
                    }
                }
            }
        }
    }
)

$blockingSeverities = @('high', 'critical')
$blockingFindings = @($findings | Where-Object {
    $blockingSeverities -contains $_.Severity.ToLowerInvariant()
})

foreach ($finding in $findings) {
    $message = '{0} {1} has a {2} severity advisory: {3} (project: {4}, framework: {5})' -f `
        $finding.Package,
        $finding.Version,
        $finding.Severity,
        $finding.AdvisoryUrl,
        $finding.Project,
        $finding.Framework

    if ($blockingSeverities -contains $finding.Severity.ToLowerInvariant()) {
        Write-Host "::error title=NuGet vulnerable dependency::$message"
    }
    else {
        Write-Host "::warning title=NuGet vulnerable dependency::$message"
    }
}

if ($blockingFindings.Count -gt 0) {
    Write-Error "NuGet audit blocked the build: $($blockingFindings.Count) High/Critical vulnerability finding(s)."
    exit 1
}

Write-Host "NuGet audit passed. Found $($findings.Count) vulnerability finding(s), none High/Critical."
