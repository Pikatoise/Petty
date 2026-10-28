# Petty

A small pixel-art cat that lives on the Windows desktop. It walks around on its own,
sleeps, gets hungry and is controlled from the system tray. Prototype, not a finished
product.

## Requirements

- Windows 10/11
- .NET SDK 10

## Build, test, run

```powershell
dotnet build Petty.slnx -c Release
dotnet test Petty.slnx
dotnet run --project src/Petty.App
```

The built executable is `src/Petty.App/bin/Release/net10.0-windows10.0.19041.0/Petty.exe`.
The tray icon has **Pet settings** and **Exit application**; exit plays the leave animation
before the process ends.

## Data and diagnostics

State, log and diagnostics reports live in `%LOCALAPPDATA%\Petty`
(`pet-state.json`, `petty.log`, `startup-report.json`, `final-report.json`). Point
`PETTY_DATA_DIR` at another folder to keep test runs isolated.

## Command-line diagnostics

```powershell
Petty.exe --data-dir <dir> --exit-after <seconds> [--desktop-layer above|workerw|none]
```

`scripts/verify-*.ps1` use these switches for on-desktop checks.

## Sprite assets

The cat atlas is procedurally generated (no AI image tool is involved):
`Assets/Sprites/cat-orange-white.png` plus `cat-orange-white.json`, 8x6 cells of 64x64.
Regenerate and validate it with:

```powershell
powershell -File scripts/generate-assets.ps1
```

See `assets/sprites/README.md` for the sheet layout and `docs/PROGRESS.md` for the verified
state and known limitations.
