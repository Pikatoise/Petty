param([switch]$Zip)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $repo 'src/Petty.App/Petty.App.csproj') -Raw
$version = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected an explicit semantic release version.' }
$releaseName = "Petty-$version-win-x64"
$releaseRoot = Join-Path $repo ('artifacts/portable/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
$folder = Join-Path $releaseRoot $releaseName
New-Item -ItemType Directory -Path $folder -Force | Out-Null

# Artwork and license notices are embedded resources. The Desktop Runtime is installed separately.
dotnet publish (Join-Path $repo 'src/Petty.App/Petty.App.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -m:1 -o $folder
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }

$files = @(Get-ChildItem -LiteralPath $folder -Recurse -File)
if ($files.Count -ne 1 -or $files[0].Name -ne 'Petty.exe') {
    throw 'Portable output must contain only Petty.exe, with embedded artwork and licenses.'
}
if ($Zip) {
    $archive = Join-Path $releaseRoot "$releaseName.zip"
    Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $archive -CompressionLevel Optimal
    Get-Item -LiteralPath $archive | Select-Object FullName, Length
} else {
    $files | Select-Object FullName, Length
}
