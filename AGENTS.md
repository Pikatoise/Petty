# Petty — agent instructions

## Working agreement

- Follow .editorconfig for C# and Settings.XamlStyler for XAML. Use
  scripts/format.ps1 to format and scripts/format.ps1 -Check to verify the agreed style.
- Commit, push and build distribution ZIPs only when explicitly requested.
- Keep changes focused. Report what changed, checks run and remaining uncertainty.
- Keep durable user/developer guidance in README.md and these instructions;
  sprite-format details belong in assets/sprites/README.md. Do not recreate session
  handoff archives or progress logs. docs/ is temporary, git-ignored specification
  input; keep it out of commits and release artifacts.
- Use artifacts/ for disposable verification output; remove it after the task.
  Retain only the newest requested portable release under artifacts/portable/.
- Routine visual and desktop acceptance belongs to the owner. Automated or simulated
  checks do not establish live hover, DPI, z-order, shell restart or logoff behavior.

## Product and architecture

Petty is a Windows 11 x64 desktop pet; Windows 10 is best effort. One active pet,
primary monitor only, four cat breeds and four dog breeds with five coats each.
Breed/coat definitions live in assets/breed-designs.json and external PNG/JSON packs.
The current UI is Cozy Home, Russian/English, with a custom tray popup. The next
version adds optional online accounts, friends and letters; local simulation stays
independent of network availability. No sounds, runtime AI or automatic updater.

- Petty.Desktop.slnx builds the Windows client; Petty.Server.slnx builds the backend
  independently on Windows/Linux. Server projects never reference Desktop, Core,
  Assets, WPF or Win32. Keep server configuration and credentials out of pet-state.
- Backend uses ASP.NET Core Controllers, PostgreSQL/EF Core and scoped Mediator.
  Domain is platform-free; Application owns use cases; Infrastructure owns storage;
  Api owns transport and composition. Add online features one specification stage
  at a time. Backend startup does not apply migrations automatically.
- Use EF Core with feature-specific repositories and Unit of Work for business
  persistence. Repositories share the scoped ServerDbContext; handlers commit via
  IUnitOfWork.

- Petty.Core: WPF/Win32-free domain, FSM, motion, clocks, scene math and persistence.
- Petty.App: WPF/Win32 integration, rendering, windows, input and composition.
- Petty.Assets: deterministic C# sprite generation and validation; never run by the app.
- Petty.Tests: headless xUnit regression tests. scripts/ contains integration harnesses.
- Prefer built-in .NET and existing modules; keep dependencies justified. Behavior
  decides actions; animation renders them. Avoid growing App/PetSession or adding
  speculative abstractions. Tune defaults in PetTuning, not UI handlers.

## Behavior and data invariants

- Creator selects species, breed, coat and name; coat arrows wrap. Existing pets
  can be renamed; recreation requires confirmation and a successful save before
  the current session is replaced. Closing Settings keeps the app running.
- Needs decay during running time, including lock/fullscreen and pet sleep. Freeze
  while closed or the PC sleeps/hibernates; no offline catch-up. Use monotonic active
  time. Pets never die. Awake/sleep cycles are session-local.
- Sleep blocks play; petting pauses and resumes remaining sleep. Food can wake a
  hungry sleeper. Eating starts a fresh awake cycle. Actions cannot overlap;
  eating and exit reject user actions. Exit has highest priority.
- The hover menu follows at normal movement speed. Detect the exposed pet rectangle,
  including transparent pixels, while preserving click-through. Hide immediately
  on hover loss and during interactions/eating/exit. Do not freeze movement.
- Bowl inventory is separate from satiety: +25/click, capacity 100, ignore clicks
  during pouring; dragging never feeds. Pet and props share the eased floor.
- Sprite cells are 64x64 classic pixel art, genuine alpha, nearest-neighbor scaling,
  consistent ground alignment. Props stay separate. Validate PNG/grid/metadata and
  pixels before use; cache decoded frozen frames and advance by elapsed time.
- Preserve schema v1 and atomic state replacement with .bak recovery. Unsupported
  newer saves and missing appearances must not be overwritten implicitly. Migrate
  cat-orange-white to british-shorthair-blue only after replacement art loads,
  preserving identity, needs and bowl data.
- User data: %LOCALAPPDATA%\Petty, overridden by --data-dir or PETTY_DATA_DIR.
  Verification uses isolated data; it must not change owner saves or autostart.
- Maintain the single-instance mutex. Tray Exit animates departure; Windows
  shutdown/logoff saves and closes promptly without animation or cancellation.
  Fatal errors log and close. Stop timers, release native icons and unsubscribe
  shell/power events. Explorer restart restores the existing tray/window integration.
- Autostart defaults on after successful startup; repair moved paths when enabled,
  respect disabled preferences and skip registry synchronization for isolated data.
- Developer UI/tuning stays Debug-only, with separate dev-tuning.json. Never ship
  owner data or Debug settings in portable output.

## Verified Windows constraints

- Default AboveDesktop: top-level, non-topmost window inserted above Progman using
  GetWindow(progman, GW_HWNDPREV). WorkerW is experimental: reparented layered WPF
  windows render nothing on the verified shell. Do not silently switch strategies.
- WPF owns layered styles. Never replace the style word wholesale. The supported
  read-modify-write adds WS_EX_TOOLWINDOW only. ShowInTaskbar=False uses a hidden
  owner; inspect ancestry with GetAncestor, not GetParent.
- WM_NCHITTEST returns HTTRANSPARENT on transparent pixels; WM_MOUSEACTIVATE returns
  MA_NOACTIVATE. Pet/props stay out of Alt+Tab and the taskbar, behind normal apps.
- The topmost taskbar covers the pet; standing on its upper edge is intentional.
  WalkSurface combines monitor bounds/work area with a genuinely visible taskbar;
  ignore auto-hide slivers and top bars. Preserve eased floor movement, full-width
  walking, arrival-driven walk completion and monitor-relative bowl recovery.
- Keep InvariantGlobalization=false for Petty.App: WPF text input needs real cultures.
- Keep ShutdownMode.OnExplicitShutdown: cancelling first creation exits explicitly;
  closing secondary windows does not end the app.

## Validation

Run from the repository root on Windows with .NET SDK 10:

```powershell
dotnet build Petty.Desktop.slnx -c Release
dotnet test Petty.Desktop.slnx -c Debug
dotnet test Petty.Desktop.slnx -c Release
dotnet build Petty.Server.slnx -c Release
dotnet test Petty.Server.slnx -c Release
dotnet run --project scripts/ContentVerification -c Release
dotnet run --project src/Petty.Assets -c Release -- --verify
```

Run targeted tests during development; use both configurations when Debug guards
or shared behavior change. ContentVerification covers runtime loading, every frame's
alpha mask, deterministic generation and RU/EN creator workflows. For Windows lifecycle
changes, use scripts/WindowsReleaseVerification with isolated data and report its
simulated limits. Preserve regression checks for recovery, clock/sleep, interactions,
metadata, localization and shell geometry. Remove redundant tests only when their
assertions remain covered; test count itself is not a quality target.
