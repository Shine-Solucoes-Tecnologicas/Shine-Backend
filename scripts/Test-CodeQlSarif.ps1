[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path,

    [ValidateRange(0, 10)]
    [double] $MinimumSecuritySeverity = 7.0
)

$ErrorActionPreference = 'Stop'

function Get-PropertyValue {
    param(
        [AllowNull()]
        [object] $InputObject,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if ($null -eq $InputObject) {
        return $null
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-SecuritySeverity {
    param(
        [AllowNull()]
        [object] $Result,

        [AllowNull()]
        [object] $Rule
    )

    $resultProperties = Get-PropertyValue -InputObject $Result -Name 'properties'
    $ruleProperties = Get-PropertyValue -InputObject $Rule -Name 'properties'
    $rawSeverity = Get-PropertyValue -InputObject $resultProperties -Name 'security-severity'
    if ($null -eq $rawSeverity) {
        $rawSeverity = Get-PropertyValue -InputObject $ruleProperties -Name 'security-severity'
    }

    if ($null -eq $rawSeverity) {
        return $null
    }

    $severity = 0.0
    $parsed = [double]::TryParse(
        [string] $rawSeverity,
        [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref] $severity)

    if (-not $parsed) {
        throw "Invalid CodeQL security severity '$rawSeverity'."
    }

    return $severity
}

$resolvedPath = Resolve-Path -LiteralPath $Path
$sarifFiles = @(Get-ChildItem -LiteralPath $resolvedPath -Recurse -File -Filter '*.sarif')
if ($sarifFiles.Count -eq 0) {
    throw "No SARIF files were found under '$resolvedPath'."
}

$findingCount = 0
$blockingFindings = [Collections.Generic.List[object]]::new()

foreach ($sarifFile in $sarifFiles) {
    try {
        $sarif = Get-Content -LiteralPath $sarifFile.FullName -Raw | ConvertFrom-Json -Depth 100
    }
    catch {
        throw "Unable to parse SARIF file '$($sarifFile.FullName)': $($_.Exception.Message)"
    }

    $runs = @(Get-PropertyValue -InputObject $sarif -Name 'runs')
    if ($runs.Count -eq 0 -or $null -eq $runs[0]) {
        throw "SARIF file '$($sarifFile.FullName)' does not contain any runs."
    }

    foreach ($run in $runs) {
        $driver = Get-PropertyValue -InputObject (Get-PropertyValue -InputObject $run -Name 'tool') -Name 'driver'
        $rules = @(Get-PropertyValue -InputObject $driver -Name 'rules')
        $rulesById = @{}
        foreach ($rule in $rules) {
            if ($null -eq $rule) {
                continue
            }

            $ruleId = Get-PropertyValue -InputObject $rule -Name 'id'
            if (-not [string]::IsNullOrWhiteSpace([string] $ruleId)) {
                $rulesById[[string] $ruleId] = $rule
            }
        }

        foreach ($result in @(Get-PropertyValue -InputObject $run -Name 'results')) {
            if ($null -eq $result) {
                continue
            }

            $findingCount++
            $ruleId = [string] (Get-PropertyValue -InputObject $result -Name 'ruleId')
            $rule = if ($rulesById.ContainsKey($ruleId)) { $rulesById[$ruleId] } else { $null }

            $level = [string] (Get-PropertyValue -InputObject $result -Name 'level')
            if ([string]::IsNullOrWhiteSpace($level)) {
                $defaultConfiguration = Get-PropertyValue -InputObject $rule -Name 'defaultConfiguration'
                $level = [string] (Get-PropertyValue -InputObject $defaultConfiguration -Name 'level')
            }

            $securitySeverity = Get-SecuritySeverity -Result $result -Rule $rule
            $isBlocking = $level -eq 'error' -or
                ($null -ne $securitySeverity -and $securitySeverity -ge $MinimumSecuritySeverity)

            if ($isBlocking) {
                $locations = @(Get-PropertyValue -InputObject $result -Name 'locations')
                $physicalLocation = if ($locations.Count -gt 0) {
                    Get-PropertyValue -InputObject $locations[0] -Name 'physicalLocation'
                } else {
                    $null
                }
                $artifactLocation = Get-PropertyValue -InputObject $physicalLocation -Name 'artifactLocation'
                $region = Get-PropertyValue -InputObject $physicalLocation -Name 'region'

                $blockingFindings.Add([pscustomobject]@{
                    RuleId = if ([string]::IsNullOrWhiteSpace($ruleId)) { '<unknown>' } else { $ruleId }
                    Level = if ([string]::IsNullOrWhiteSpace($level)) { '<unspecified>' } else { $level }
                    SecuritySeverity = $securitySeverity
                    File = [string] (Get-PropertyValue -InputObject $artifactLocation -Name 'uri')
                    Line = Get-PropertyValue -InputObject $region -Name 'startLine'
                })
            }
        }
    }
}

Write-Output "CodeQL SARIF validation processed $findingCount finding(s) from $($sarifFiles.Count) file(s)."

foreach ($finding in $blockingFindings) {
    $severityText = if ($null -eq $finding.SecuritySeverity) { 'n/a' } else { $finding.SecuritySeverity }
    Write-Error -ErrorAction Continue (
        "Blocking CodeQL finding: rule={0}; level={1}; securitySeverity={2}; file={3}; line={4}" -f
        $finding.RuleId,
        $finding.Level,
        $severityText,
        $finding.File,
        $finding.Line)
}

if ($blockingFindings.Count -gt 0) {
    throw "CodeQL reported $($blockingFindings.Count) blocking finding(s)."
}

Write-Output "CodeQL SARIF gate passed with no blocking findings."
