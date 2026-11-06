# Petty Desktop - agent instructions

Read the [project instructions](../../AGENTS.md) first. This document also covers
`Petty.Core`, `Petty.Assets`, desktop tests, assets and Windows verification scripts.

## Product and architecture

- Target Windows 11 x64; Windows 10 is best effort. Keep one active local pet on
  the primary monitor, Cozy Home UI, Russian/English and the custom tray popup.
  Appearance definitions live in `assets/breed-designs.json` and PNG/JSON packs.
- `Petty.Core` owns WPF/Win32-free domain rules, FSM, motion, clocks, scene math
  and persistence. `Petty.App` owns Windows integration, rendering and composition.
  `Petty.Assets` generates and validates sprites; the app never runs the generator.
- Behavior decides actions; animation renders them. Keep `App` and `PetSession`
  focused and tune defaults in `PetTuning`, rather than UI handlers.

## Behavior and persistence

- Creation selects species, breed, coat and name; coat arrows wrap. Rename existing
  pets in place. Recreation needs confirmation and a successful save before replacing
  the session. Closing Settings keeps the app running.
- Needs decay during active running time, including lock/fullscreen and pet sleep.
  Freeze while closed or during PC sleep/hibernation; use monotonic active time
  without offline catch-up. Pets never die; awake/sleep cycles are session-local.
- Sleep blocks play; petting pauses and resumes remaining sleep. Food can wake a
  hungry sleeper. Eating starts a fresh awake cycle. Actions cannot overlap;
  eating and exit reject user actions, and exit has highest priority.
- The hover menu follows at normal movement speed without freezing the pet.
  Detect the exposed pet rectangle, including transparent pixels, while preserving
  click-through. Hide on hover loss and during interactions, eating and exit.
- Bowl inventory is separate from satiety. Preserve `PetTuning` capacity and fill
  values; ignore clicks during pouring, and never feed through dragging.
  Pet and props share the eased floor.
- Preserve schema v1, atomic state replacement and `.bak` recovery. Never implicitly
  overwrite newer saves or missing appearances. Migrate `cat-orange-white` to
  `british-shorthair-blue` only after replacement art loads, preserving identity,
  needs and bowl data.
- User data defaults to `%LOCALAPPDATA%\Petty`, overridden by `--data-dir` or
  `PETTY_DATA_DIR`. Verification must use isolated data and leave owner saves and
  autostart untouched.
- Preserve the single-instance mutex. Tray Exit animates departure; Windows
  shutdown/logoff saves and closes promptly without animation or cancellation.
  Fatal errors log and close. Stop timers, release native icons and unsubscribe
  shell/power events. Explorer restart restores existing tray/window integration.
- Autostart defaults on after successful startup. Repair moved paths when enabled,
  respect disabled preferences and skip registry synchronization for isolated data.
- Keep developer UI/tuning Debug-only and `dev-tuning.json` separate. Exclude owner
  data and Debug settings from portable output.

## Rendering and Windows constraints

- Sprite cells are 64x64 pixel art with genuine alpha, nearest-neighbor scaling
  and consistent ground alignment. Keep props separate; validate PNG/grid/metadata
  and pixels, cache decoded frozen frames and advance by elapsed time. Read
  [sprite documentation](../../assets/sprites/README.md) for format/regeneration.
- Default `AboveDesktop` uses a top-level, non-topmost window above Progman via
  `GetWindow(progman, GW_HWNDPREV)`. WorkerW is experimental: reparented layered WPF
  windows rendered nothing on the verified shell. Preserve the default strategy.
- WPF owns layered styles. Add only `WS_EX_TOOLWINDOW` with read-modify-write;
  replacing the style word breaks rendering. `ShowInTaskbar=False` uses a hidden
  owner; inspect ancestry with `GetAncestor`, rather than `GetParent`.
- `WM_NCHITTEST` returns `HTTRANSPARENT` on transparent pixels;
  `WM_MOUSEACTIVATE` returns `MA_NOACTIVATE`. Keep pet/props out of Alt+Tab and the
  taskbar, behind normal apps.
- The topmost taskbar covers the pet; standing on its upper edge is intentional.
  `WalkSurface` combines monitor bounds/work area with a genuinely visible taskbar;
  ignore auto-hide slivers and top bars. Preserve eased floor movement, full-width
  walking, arrival-driven completion and monitor-relative bowl recovery.
- Preserve `InvariantGlobalization=false` for WPF text input and
  `ShutdownMode.OnExplicitShutdown`: cancelling first creation exits explicitly,
  while closing secondary windows keeps the app running.

## Online integration

- Online startup is opt-in through `PETTY_SERVER_URL`; failures leave the local pet
  running. Keep network work outside simulation and local state saves.
- `OnlineCoordinator` owns synchronization and publishes snapshots through the
  Dispatcher. Coalesce wakeups without losing invalidations during a fetch; share
  mailbox/friends snapshots with windows and the postman instead of duplicate reads.
- Keep tokens in the isolated data directory's origin-specific DPAPI store;
  passwords are never persisted. Serialize refresh rotation. Retain the same letter
  request ID across uncertain sends; report success only after server acknowledgement.
- Preserve one guest and its original visit snapshot. Read only acknowledged IDs;
  later arrivals belong to a subsequent visit. A fresh mailbox generation after
  departure must precede the next visit. Dispose guests and session-bound windows
  on logout, session replacement, pet recreation and shutdown.
- See [development documentation](../../DEVELOPMENT.md) for synchronization,
  delivery and retry details, and [local E2E acceptance](../../tests/E2E/README.md)
  for server/client setup and owner checks.

## Validation

Run from the repository root on Windows with .NET SDK 10:

```powershell
dotnet build Petty.Desktop.slnx -c Release
dotnet test Petty.Desktop.slnx -c Debug
dotnet test Petty.Desktop.slnx -c Release
dotnet run --project scripts/ContentVerification -c Release
dotnet run --project src/Petty.Assets -c Release -- --verify
```

Use both configurations for Debug guards or shared behavior changes.
`ContentVerification` covers runtime sprites/alpha masks, deterministic generation,
RU/EN workflows and online/window regressions. For Windows lifecycle changes, run
`scripts/WindowsReleaseVerification` with isolated data; close other Petty instances
before using that harness. See [harness setup](../../DEVELOPMENT.md#checks).

Routine visual/desktop acceptance belongs to the owner. Automated or simulated
checks do not establish live hover, DPI, z-order, shell restart or logoff behavior;
report these limits. Preserve recovery, clock/sleep, interaction, metadata,
localization and shell geometry coverage.
