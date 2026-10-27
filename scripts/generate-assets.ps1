param(
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    if ($Verify) {
        dotnet run --project src/Petty.Assets -c Release -- --verify
    }
    else {
        dotnet run --project src/Petty.Assets -c Release
    }
}
finally {
    Pop-Location
}
