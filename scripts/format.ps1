param([switch]$Check)

$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet tool restore --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Formatter tool restore failed.' }

    $formatOptions = @('--verbosity', 'quiet')
    if ($Check) { $formatOptions += '--verify-no-changes' }
    $styleDiagnostics = @('IDE0005', 'IDE0007', 'IDE0008', 'IDE0011', 'IDE0022', 'IDE0161', 'IDE1006')
    foreach ($project in @('Petty.Desktop.slnx', 'Petty.Server.slnx', 'scripts/ContentVerification/ContentVerification.csproj', 'scripts/WindowsReleaseVerification/WindowsReleaseVerification.csproj')) {
        dotnet format whitespace $project @formatOptions
        if ($LASTEXITCODE -ne 0) { throw "Whitespace check failed: $project" }
        dotnet format style $project --no-restore --severity info --diagnostics $styleDiagnostics @formatOptions
        if ($LASTEXITCODE -ne 0) { throw "Code style check failed: $project" }
        dotnet format whitespace $project --no-restore @formatOptions
        if ($LASTEXITCODE -ne 0) { throw "Final whitespace check failed: $project" }
    }

    $xamlOptions = @('-d', 'src/Petty.App', '-r', '-c', 'Settings.XamlStyler', '-l', 'Minimal')
    if ($Check) { $xamlOptions += '--passive' }
    dotnet xstyler @xamlOptions
    if ($LASTEXITCODE -ne 0) { throw 'XAML formatting check failed.' }
}
finally {
    Pop-Location
}
