param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $repo 'src/Petty.App/Petty.App.csproj') -Raw
$version = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected an explicit semantic release version.' }
$releaseName = "Petty-$version-win-x64"
$releaseRoot = Join-Path $repo ('artifacts/portable/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
$folder = Join-Path $releaseRoot $releaseName
New-Item -ItemType Directory -Path $folder -Force | Out-Null

dotnet publish (Join-Path $repo 'src/Petty.App/Petty.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -m:1 -o $folder
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }

foreach ($file in @('Petty.exe', 'coreclr.dll', 'PresentationFramework.dll', 'Assets/Sprites/ball.png')) {
    if (!(Test-Path -LiteralPath (Join-Path $folder $file))) { throw "Missing portable file: $file" }
}
$packs = Join-Path $folder 'Assets/Sprites/packs'
if (@(Get-ChildItem -LiteralPath $packs -Recurse -Filter '*.png').Count -ne 40 -or
    @(Get-ChildItem -LiteralPath $packs -Recurse -Filter '*.json').Count -ne 40) {
    throw 'Portable release must contain forty external sprite/metadata pairs.'
}
$unwanted = Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object {
    $_.Name -in @('pet-state.json', 'preferences.json', 'dev-tuning.json', 'cat-orange-white.png', 'cat-orange-white.json') -or
    $_.Extension -in @('.bak', '.log')
}
if ($unwanted) { throw 'Portable output contains user data or retired content.' }
Copy-Item -LiteralPath (Join-Path $repo 'docs/PORTABLE_README.md') -Destination (Join-Path $folder 'README.md')
foreach ($document in @('WINDOWS_RELEASE.md', 'RELEASE_CHECKLIST.md')) {
    Copy-Item -LiteralPath (Join-Path $repo "docs/$document") -Destination (Join-Path $folder $document)
}
$archive = Join-Path $releaseRoot "$releaseName.zip"
Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $archive -CompressionLevel Optimal
Get-Item -LiteralPath $archive | Select-Object FullName, Length
