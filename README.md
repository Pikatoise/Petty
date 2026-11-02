# Petty

Pixel desktop pets for Windows: eight cat/dog breeds, five coats each, one active pet.
Original procedural pixel art; the application does not generate assets at runtime.

## Quick start / Быстрый старт

**RU:** Распакуйте весь portable ZIP в отдельную папку и запустите `Petty.exe`.
.NET и права администратора не нужны. Основная платформа — Windows 11 x64;
Windows 10 поддерживается по возможности. Питомец живёт на основном мониторе.

Выберите вид, породу, окрас и имя. Наведите курсор на питомца для игры с мячом
или поглаживания. Во сне доступно поглаживание, но не игра. Миска пополняется
на 25 единиц за клик до 100; клики во время насыпания игнорируются. Миску можно
перетаскивать. Правый клик по значку в трее открывает настройки и выход.
Имя сохраняется автоматически; создание нового питомца требует подтверждения.
Закрытие настроек оставляет программу работать, выход из трея проигрывает уход.

Потребности снижаются только при работающей программе, включая сон питомца.
При закрытой программе и во сне компьютера они не меняются. Автозапуск включён
по умолчанию; его можно отключить в настройках. После переноса папки запустите
Petty вручную один раз для обновления пути автозапуска.

Данные находятся в `%LOCALAPPDATA%\Petty` и не входят в ZIP. Для обновления выйдите
через трей и замените файлы приложения вместе с `Assets`, сохранив папку данных.
Для удаления отключите автозапуск, выйдите и удалите папку приложения.

**EN:** Extract the entire portable ZIP and run `Petty.exe`. The .NET runtime is
included; no administrator rights are required. Windows 11 x64 is the primary
platform, Windows 10 best effort; the pet uses the primary monitor.

Choose species, breed, coat and name. Hover for Play or Pet; sleeping blocks Play,
while petting pauses and resumes sleep. Click the draggable bowl to add 25 food
(cap 100); clicks during pouring are ignored. Right-click the tray for Settings
or animated Exit. Renames save automatically; replacement requires confirmation.
Closing Settings keeps Petty running. Needs decay while running, including pet
sleep, and freeze while closed or the PC sleeps/hibernates.

Autostart defaults on and can be disabled in Settings. Launch once after moving
the folder to repair its startup path. Data stays in `%LOCALAPPDATA%\Petty`.
Update manually by exiting and replacing application files including `Assets`;
retain the data folder. To uninstall, disable autostart, exit and delete the app folder.

## Build and verify

Windows and .NET SDK 10 are required to build. Run from the repository root:

```powershell
dotnet build Petty.slnx -c Release
dotnet test Petty.slnx -c Debug
dotnet test Petty.slnx -c Release
dotnet run --project src/Petty.App
dotnet run --project scripts/ContentVerification -c Release
dotnet run --project src/Petty.Assets -c Release -- --verify
```

Release executable: `src/Petty.App/bin/Release/net10.0-windows10.0.19041.0/Petty.exe`.
Headless tests cover domain behavior, persistence/recovery, clocks, geometry and
asset contracts. ContentVerification exercises all 40 atlases/2560 frames,
per-pixel hit masks, RU/EN creator selection and deterministic generation.
Generated verification files are disposable under `artifacts/verification/`.

```powershell
# Isolated runtime smoke; skips real autostart registration.
# Close any other Petty instance first.
& 'src/Petty.App/bin/Release/net10.0-windows10.0.19041.0/Petty.exe' --data-dir "$env:TEMP\PettyCheck" --create-name Milo --exit-after 20
# Simulated Windows lifecycle checks with isolated state/registry keys:
dotnet run --project scripts/WindowsReleaseVerification -c Release
# Produce a self-contained win-x64 ZIP when needed:
./scripts/publish-portable.ps1
```

For a short Release performance sample, close other Petty instances, set
`$env:PETTY_PROFILE='1'`, run WindowsReleaseVerification, then clear the variable
with `Remove-Item Env:PETTY_PROFILE`. This measures idle, walking, sleeping and
repeated normal play scenes for 20 seconds each after warm-up. Output includes
state coverage, CPU as a percentage of one logical core, process memory,
managed allocation rate and timed session callbacks. Measurements include the
small in-process harness overhead; they do not establish long-run stability.
Results use isolated data under `artifacts/verification/performance/`.

`--data-dir` / `PETTY_DATA_DIR` overrides the default data folder. Diagnostic launch
also accepts `--desktop-layer above|workerw|none`; use default `above` for normal use.
Portable output is under `artifacts/portable/`; retain `Assets`, DLLs and licenses
beside `Petty.exe`. Packaging includes this README and LICENSE; no user data.

## Structure and constraints

Code style is defined in `.editorconfig` (C#) and `Settings.XamlStyler` (XAML).
The XAML formatter is pinned in `dotnet-tools.json` as a development tool only.

```powershell
./scripts/format.ps1        # restore the local tool and format source/tests/harnesses
./scripts/format.ps1 -Check # verify without changing files
```

- `src/Petty.Core`: headless needs, behavior, scenes, movement, clocks and JSON state.
- `src/Petty.App`: WPF/Win32 desktop windows, interaction, tray, settings and lifecycle.
- `src/Petty.Assets`: deterministic sprite generator/validator and preview template.
- `tests/Petty.Tests`: focused xUnit regressions; `scripts/`: integration tools.
- `assets/breed-designs.json`: breed/coat design data. See
  [sprite format and generation](assets/sprites/README.md) for asset details.
- [AGENTS.md](AGENTS.md): development rules and verified Windows integration constraints.

Petty is non-topmost: normal windows cover it, and it stands on the taskbar edge.
Transparent sprite pixels pass input through. The runtime loads cached frames,
uses elapsed-time animation and stops owned timers on shutdown. State schema v1
uses atomic replacement and a backup; newer unsupported saves are preserved.
Legacy orange-cat saves migrate only after the replacement appearance loads.
Debug-only tuning is separate from pet state and absent from Release.

## Desktop regression checks

For changes affecting native windows, input or lifecycle, check on Windows:

- Tray, creation, rename/recreation, RU/EN text and keyboard input at relevant DPI.
- Moving hover menu, both action scenes, bowl drag/feed, transparency and Alt+Tab.
- Ordinary-window coverage, taskbar show/hide, primary-display/DPI changes.
- Lock/fullscreen, sleep/resume, Explorer restart, fast logoff/shutdown save.
- Enabled/disabled autostart after moving the folder; restart and recovery with
  disposable data, including missing art and corrupt/newer state.

The owner previously accepted the broad v1 Windows flow; subsequent changes require
rechecking affected scenarios. Simulated notifications and successful builds do not
prove live desktop behavior. Windows 10, clean-PC deployment and prolonged stability
need separate verification when relevant. No mandatory endurance run is imposed.
