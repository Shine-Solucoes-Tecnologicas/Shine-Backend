[CmdletBinding()]
param(
    [ValidateSet('Seed', 'Reset')]
    [string] $Action = 'Seed',
    [Parameter(Mandatory)]
    [Security.SecureString] $Password,
    [string] $ConfirmReset
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$databaseName = 'shine_demo'
$connectionString = "Host=localhost;Port=5433;Database=$databaseName;Username=shine;Password=shine"

if ($Action -eq 'Reset' -and $ConfirmReset -cne $databaseName) {
    throw "Reset refused. Pass -ConfirmReset '$databaseName' to recreate only the isolated demo database."
}

Push-Location $repositoryRoot
$passwordPointer = [IntPtr]::Zero
try {
    & "$PSScriptRoot/Local-Environment.ps1" -Action Check
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if ($Action -eq 'Reset') {
        & docker compose exec -T postgres dropdb --if-exists --force -U shine $databaseName
        if ($LASTEXITCODE -ne 0) { throw 'Could not recreate the isolated demo database.' }
    }

    & "$PSScriptRoot/Local-Environment.ps1" -Action Prepare -ConnectionString $connectionString
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $previousConnection = $env:ConnectionStrings__ShineDb
    $previousEnabled = $env:DemoData__Enabled
    $previousPassword = $env:DemoData__Password
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:ConnectionStrings__ShineDb = $connectionString
        $env:DemoData__Enabled = 'true'
        $env:DemoData__Password = $plainPassword
        & dotnet run --project host/Shine.Api --no-build --no-launch-profile -- --demo-data seed
        if ($LASTEXITCODE -ne 0) { throw 'Demo data seed failed.' }
    }
    finally {
        $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
        $env:ConnectionStrings__ShineDb = $previousConnection
        $env:DemoData__Enabled = $previousEnabled
        $env:DemoData__Password = $previousPassword
        $plainPassword = $null
    }

    Write-Host "Demo data is ready in local database '$databaseName'."
}
finally {
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    Pop-Location
}
