# Petty

A small pixel-art cat that lives on the Windows desktop. It walks around on its own,
sleeps, gets hungry, eats from a bowl you can refill and drag anywhere, and plays with a
ball when you click the Play chip over its head. Prototype, not a finished product: one
cat, one coat, local state only.

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
On the first launch a short wizard asks for the cat's name and shows a live preview; it
is skipped once a valid state exists. The tray icon has **Pet settings** and
**Exit application**; settings rename the cat or create a new one, exit plays the leave
animation before the process ends. The cat, bowl and ball take no taskbar button and do
not appear in Alt+Tab; transparent pixels click through to the desktop.

## Data and diagnostics

State, log and diagnostics reports live in `%LOCALAPPDATA%\Petty`
(`pet-state.json`, `petty.log`, `startup-report.json`, `final-report.json`). Point
`PETTY_DATA_DIR` at another folder to keep test runs isolated. A bad state file is
reported in the log and recovered through a fresh pet, never silently overwritten.

## Command-line diagnostics

```powershell
Petty.exe --data-dir <dir> --create-name <name> --exit-after <seconds> [--desktop-layer above|workerw|none]
```

`--create-name` skips the first-run wizard on a fresh data directory. `scripts/verify-*.ps1`
use these switches for on-desktop checks.

## Sprite assets

The cat atlas is procedurally generated (no AI image tool is involved):
`assets/sprites/cat-orange-white.png` plus `cat-orange-white.json`, 8x6 cells of 64x64, with
separately drawn `bowl-empty.png`, `bowl-full.png` and `ball.png` props. Regenerate and
validate them with:

```powershell
powershell -File scripts/generate-assets.ps1
```

## Documentation

- `docs/ARCHITECTURE.md` — how the projects, runtime loop, desktop integration, persistence
  and asset pipeline fit together.
- `assets/sprites/README.md` — sheet layout, clip table and validation rules.

## Known limits

- Single pet and a single fixed coat; more breeds/coats are meant to arrive as assets.
- Multi-monitor, mixed-DPI and display hot-plug setups are implemented but only verified on a
  single 2560x1600 display at 200 % DPI.
- No installer or publish artifact: build from source.
