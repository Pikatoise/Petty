# Petty — progress handoff

Short state file so work can continue cheaply after a context compaction or a new session.
Read this together with `AGENTS.md`; `AGENTS.md` is the specification.

## Environment

- Windows 11 Pro 26H1 (build 28000), .NET SDK 10.0.402, xUnit 2.9.3.
- Repo root: `C:\Users\wierw\Desktop\Projects\Petty` (git repo, `AGENTS.md` is git-ignored).
- Solution file is `Petty.slnx` (SDK 10 default, not `.sln`).
- Display used for verification: single 2560x1600 monitor at 200 % DPI, auto-hidden taskbar.

## Stage status

| Stage | Scope | Status |
|---|---|---|
| 1 | Core domain, persistence, offline progression, bowl placement, metadata, tests | **done** |
| 2 | Procedural pixel-art atlas, frames, metadata, validator, previews | **done** |
| 3 | WPF pet window, desktop placement, click-through, animation playback | **done** (WorkerW layer limited, see below) |
| 3.1 | Taskbar-aware walk line, full-width walking, arrival-driven walks | **done** |
| 4 | Tray icon, lifecycle, animated exit, first-run wizard, settings | not started |
| 5 | Bowl window (drag/feed), ball, Play action, runtime wiring, autosave | not started |
| 6 | Release verification, README, architecture doc, limitations list | not started |

## Repository map

```
Petty.slnx
Directory.Build.props            nullable+implicit usings, LangVersion latest
src/Petty.Core/                  net10.0, no WPF/Win32
  Domain/Needs.cs                satiety/happiness 0..100, decay, thresholds
  Domain/PetTuning.cs            every rate/duration, Sanitized()
  Domain/Bowl.cs                 food inventory, Fill/Consume, capacity clamp
  Domain/PetSnapshot.cs          persisted schema v1 (camelCase JSON)
  Domain/BowlPlacement.cs        monitor-relative bowl position (relativeX + bottomOffset + device)
  Domain/PetCatalog.cs           "cat" / "orange-white-01" / "cat-orange-white"
  Domain/PetNameRules.cs         1..24 chars, trimmed, no control chars
  Behavior/PetBehaviorMachine.cs explicit FSM + BehaviorTransition events
  Runtime/PetRuntime.cs          glue: needs+bowl+FSM, eat bites, play gain, OfflineProgression
  Runtime/PetMotion.cs           screen-space walk/run/enter/exit motion (tested, WPF-free)
  Runtime/WalkSurface.cs         walk band from monitor + work area + visible taskbar rect
  Runtime/FloorFollow.cs         eased step up/down when the taskbar slides in or out
  Animation/AnimationPlayback.cs time-based frame cursor, loop override
  Animation/PetAnimationMap.cs   PetActivity -> clip name (single mapping point)
  Persistence/PetStateStore.cs   atomic JSON (tmp -> File.Replace), .bak, corrupt/version handling
  Assets/SpriteMetadata.cs       metadata model + validator (required clips, overlap, grid)
  Diagnostics/FileAppLog.cs      rotating log
src/Petty.Assets/                net10.0-windows console, System.Drawing (WinForms ref)
  PixelCanvas.cs                 ARGB buffer, hard-edge primitives, outline, PNG I/O
  CatSpriteRenderer.cs           cat drawing, CatPose, groundRow = 52
  Animations.cs                  the 8 clips and their poses
  AtlasBuilder/Writer/Validator  atlas 8x6, metadata, frames, previews, checks
src/Petty.App/                   WPF WinExe, net10.0-windows10.0.19041.0, per-monitor DPI v2
  App.xaml(.cs)                  composition root, diagnostics reports, shutdown
  AppOptions.cs                  --data-dir, --exit-after, --desktop-layer, --no-desktop-layer
  PetPaths.cs                    %LOCALAPPDATA%\Petty, PETTY_DATA_DIR override
  Sprites/SpriteSheetLoader.cs   atlas decode once -> frozen CroppedBitmap per frame + alpha plane
  Sprites/AnimationController.cs clip switching + FrameChanged only on visible changes
  Views/PetWindow.xaml(.cs)      192x192 (64x64 x3) layered window, alpha hit-testing
  Desktop/NativeMethods.cs       the whole Win32 surface
  Desktop/DisplayService.cs      physical-pixel monitor layout
  Desktop/DesktopWindowService.cs desktop placement, shell host discovery, hit-test probes
  Desktop/ShellBarProbe.cs       taskbar rect per monitor + "is it really on screen" hit test
  Pet/PetSession.cs              timers (20 ms frames, 200 ms simulation), motion, exit flow
tests/Petty.Tests/               xUnit, 120 tests, all green
assets/sprites/                  atlas + metadata + 48 frames + previews + README
scripts/generate-assets.ps1      regenerate / --verify the atlas
scripts/verify-desktop.ps1       run the app, minimise other windows, screenshot, click the cat
scripts/verify-zorder.ps1        prove a normal window covers the pet
scripts/verify-taskbar.ps1       reveal the auto-hide taskbar and watch the pet step up/down
```

## Key facts to reuse

- Sprite atlas: 512x384, 8x6 cells of 64x64, transparent, 7 colours, real RGBA.
- Clips/cells: idle 0-3, walk 4-11, run 12-19, eat 20-25, sleep 26-29, play 30-37,
  tired 38-41, exit 42-47. `groundRow = 52` (outline row that touches the ground).
- Cat faces **right** in every frame; flip horizontally for leftward movement.
- Ball and bowl are **not** in the atlas — draw them as separate objects.
- FSM priorities: exit > user play > eating > hunger > sleep > idle/wander.
- Persistence: `PetStateStore` atomic save, `.bak` fallback, schema v1, JSON camelCase.
- Needs thresholds: hungry <= 30, tired <= 20, full recovery > 45.
- Tuning defaults: satiety -4/h, happiness -3/h, play 12 s (+30 happiness, -2 satiety),
  eat bite 1.2 s (5 food -> 9 satiety), bowl 100 cap / +25 per click, autosave 2 min,
  seek safety cap 120 s (walks end on arrival).
- Pet window geometry: 3 screen pixels per sprite pixel (64 -> 192 px, independent of DPI);
  window top = floor - (groundRow + 1) * 3, so on a 1600 px tall screen top = 1441 at the
  bottom edge and 1345 while standing on a 96 px taskbar.
- Walk band = `WalkSurface.Compute(display, visibleTaskbarRect, windowWidth)`:
  monitor bounds and work area are intersected, then a taskbar that is *really on screen*
  raises the floor to its top edge (<= 8 px slivers are ignored as auto-hide artifacts);
  a left/right docked bar narrows the range instead; a top bar is ignored. Horizontal range
  is the full monitor width: window-left goes from `monitor.X` to `monitor.Right - 192`.
- The taskbar keeps its docked rect while auto-hidden, and Windows may move it between
  `y = screen - 96` (shown) and `y = screen - 2` (hidden). `ShellBarProbe` therefore reads
  the rect *and* checks `WindowFromPoint` at 25/50/75 % of the bar; a retracted bar is not
  hit-testable. Polled every 250 ms (`PetSession._shellBarTimer`), and the floor is eased at
  900 px/s by `FloorFollow` so the pet steps up/down instead of teleporting.
- Walks end when the pet reaches its target (`PetBehaviorMachine.NotifyArrived`), not on a
  fixed timer. `WalkDuration`/`RunDuration` (60 s) are only safety caps now. Idle strolls
  stay within 30 % of the screen width; playful runs pick the far edge 35 % of the time,
  so the pet does cross the whole desktop (see `scripts/verify-taskbar.ps1`).
- The pet can never be drawn *over* the taskbar: the bar is `WS_EX_TOPMOST` and the pet is
  deliberately not topmost. Standing on the bar's top edge is the closest legal behaviour.
- WPF sets `WS_EX_LAYERED|TOOLWINDOW|NOACTIVATE` itself. **Do not** call `SetWindowLongPtr`
  on the pet window: it breaks WPF's layered composition and the window renders nothing.

## Commands

```powershell
dotnet build Petty.slnx -c Release
dotnet test  Petty.slnx -c Release
dotnet run   --project src/Petty.Assets        # regenerate atlas + validate
dotnet run   --project src/Petty.Assets -- --verify
dotnet build src/Petty.App
src\Petty.App\bin\Debug\net10.0-windows10.0.19041.0\Petty.exe --exit-after 20
powershell -File scripts\verify-desktop.ps1    # screenshot + click verification
powershell -File scripts\verify-taskbar.ps1    # reveal the auto-hide bar, watch the pet step up
```

Data directory defaults to `%LOCALAPPDATA%\Petty`; use `--data-dir` (or `PETTY_DATA_DIR`)
to keep test runs isolated. Diagnostics land in that directory: `petty.log`,
`startup-report.json`, `final-report.json`.

## Stage 3 — what was verified on the real desktop

Verified (see `scripts/verify-desktop.ps1`, `scripts/verify-zorder.ps1`):

- Window is 192x192 physical pixels at (x, 1441) on a 1600 px screen: the paws rest on the
  bottom edge, scale stays 3x at 200 % DPI.
- The sprite renders (screenshot shows the pixel cat on the wallpaper; ~2000 palette pixels
  sampled in the window area).
- No taskbar entry: `ShowInTaskbar="False"` makes WPF own the window from a hidden owner
  window, so no button appears. No activation: the window is layered and `WM_MOUSEACTIVATE`
  returns `MA_NOACTIVATE` (`PetWindow.WndProc`). Ex-style at runtime is `0x80000` (layered
  only); `WS_EX_TOOLWINDOW`/`WS_EX_NOACTIVATE` are **not** set, and setting them by hand with
  `SetWindowLongPtr` breaks WPF's layered rendering, so the style audit only reports.
- Alpha hit-testing via `WM_NCHITTEST`: transparent corner returns `HTTRANSPARENT` (clicks go
  to the desktop), the cat's body returns `HTCLIENT` and a real mouse click logs `Pet clicked.`
- Z-order: an ordinary window placed over the pet covers it (verified by pixel sampling).
- The pet stays above the wallpaper after the user clicks an empty part of the desktop.
- Animated exit: `--exit-after` triggers `Exiting`, the cat walks off-screen, the process
  exits with code 0. Logged sequence: Entering -> Idle -> Running/Walking -> Exiting.

Not verified / limited:

- **Wallpaper (`WorkerW`) hosting does not work on this shell.** `SHELLDLL_DefView` lives in
  `Progman`; the only screen-sized `WorkerW` is a *child of Progman*. `SetParent` into it
  actually succeeds (`GetAncestor(GA_ROOT) == Progman`) but a WPF layered window then renders
  nothing, so the pet is invisible. The default is therefore a top-level, non-topmost window
  inserted directly above `Progman` — visible on the desktop, covered by normal windows, but
  drawn **above desktop icons**. `--desktop-layer=workerw` keeps the experimental path.
- Focus stealing on click was only verified indirectly (layered window + `MA_NOACTIVATE`);
  the end-to-end foreground-window check was inconclusive.
- Multi-monitor, mixed DPI and display hot-plug are untested (single monitor available here).
- Screen readers / UIA see a window with no accessible content.

## Stage 3.1 — taskbar-aware walk line (verified)

Problem reported by the user: the pet always walked exactly along the bottom edge of the
screen, so the auto-hide taskbar covered it, and it barely moved along the width.

What changed:

- `WalkSurface.Compute` (Core, pure, 14 tests) builds the walk band from the monitor bounds,
  the work area and the taskbar rectangle that is *currently visible*. A visible bottom bar
  raises the floor to its top edge; a left/right bar narrows the range; a top bar and
  auto-hide slivers (< 8 px) are ignored.
- `ShellBarProbe` (App) finds `Shell_TrayWnd` / `Shell_SecondaryTrayWnd`, reads its rectangle
  and decides whether the bar is really on screen: a retracted auto-hide bar keeps its docked
  rectangle but is not hit-testable, so `WindowFromPoint` at 25/50/75 % of the bar is used.
- `PetSession` re-reads the bar every 250 ms and eases the floor at 900 px/s (`FloorFollow`),
  so the pet steps onto the bar when it slides up and comes back down when it hides.
- Walks now end on arrival (`PetBehaviorMachine.NotifyArrived`) instead of after a 5 s timer
  (that timer was why the pet only ever shuffled ~100 px). Idle strolls stay within 30 % of
  the width; playful runs head for the far edge 35 % of the time, crossing the whole screen.
- The walk band is the full monitor width: window-left runs from 0 to `2560 - 192 = 2368`,
  i.e. every column of a 2560 px screen is reachable.

Verified on the real desktop (`scripts/verify-taskbar.ps1`, log + window rects):

```
startup          window 101,1441  -> floor 1600  (bar rect 0,1598 2560x96 = retracted)
bar revealed     window 496,1345  -> floor 1504  ("Taskbar shown: the pet steps up to floor 1504")
bar retracted    window 584,1441  -> floor 1600  ("Taskbar hidden: the pet returns to floor 1600")
walk spread      x 101..430 in 20 s while idle-walking (previously ~70 px total)
```

Limitation: the pet can **not** be painted on top of the taskbar. `Shell_TrayWnd` is
`WS_EX_TOPMOST`, and the pet is deliberately not topmost (spec §5), so the best legal result
is standing on the bar's top edge, which is what happens now. Reaching "walks on the panel"
would require a permanently topmost pet window, which would also cover every normal window.

Note for future sessions: this machine's display falls asleep quickly; a sleeping monitor
returns blank screenshots and the shell does not animate its auto-hide taskbar, so the
verification scripts call `SetThreadExecutionState(ES_DISPLAY_REQUIRED)` and jiggle the mouse
first. If the session is locked, no desktop verification is possible at all.

## Audit pass (before stage 4)

Applied all findings from the repo audit except the two deliberately skipped ones:

- Periodic autosave every `PetTuning.AutoSaveInterval` (2 min) through the same `SaveState`
  used at shutdown; state is saved before the diagnostics report, and report errors never
  block saving.
- Seeking food now really ends at the bowl: the view calls `PetRuntime.NotifyArrived()`,
  which dispatches `ArriveAtBowl` while `SeekingFood`. The seek safety cap was raised to
  120 s (walking speed 26 px/s × 45 s was shorter than the screen), so a hungry cat reaches
  even a far bowl. Verified live with a saved state (`bowlFood 60`, `satiety 10`): Idle ->
  SeekingFood -> Eating (bowl-reached) -> Idle (bowl-emptied).
- Simulation delta is clamped to `MaxOfflineSpan` (7 days) instead of 5 minutes, so time the
  PC spent asleep decays needs like offline time.
- A named `Local\Petty.SingleInstance` mutex makes a second launch exit (code 3); verified
  live.
- A changed display layout or walk range re-applies the window position immediately, and a
  running walk's target is clamped into the new band.
- `PetSnapshot.Normalized` now replaces invalid stored names (empty, too long, control
  characters) with the default name instead of keeping them.
- The frame timer runs at 20 ms while the pet moves or the floor eases, otherwise at the
  current clip's fps (capped at 250 ms); animation stays time-based.
- Dead code removed: `PetTuning.Validate()`, `Needs.IsTired`, `NullAppLog`,
  `DesktopAttachment.IsDesktopLayer`, `PetCatalog.BreedDisplayName`, unused P/Invokes
  (including `SetWindowLongPtr`), `PetWindow.CurrentFrame/FacesLeft`, `AnimationPlayback.Reset`.
- Root `README.md` rewritten (requirements, build/test/run, data location, asset regeneration).

Skipped on purpose: #12 (`OnLocationChanged` warning noise) and #13 (committed derived frame
PNGs).

Build: `dotnet build Petty.slnx -c Release` → 0 warnings, 0 errors.
Tests: `dotnet test Petty.slnx` → 120 passing.

## Stage 4 plan (next)

1. `TrayService` — `NotifyIcon` + context menu: "Pet settings", "Exit application"; dispose on
   shutdown; icon generated from the idle sprite frame.
2. Lifecycle: keep `ShutdownMode.OnExplicitShutdown`; tray exit requests the animated exit;
   closing wizard/settings must not exit.
3. First-run wizard (`Views/CreationWizardWindow`) — preview of the cat, breed "Cat" (fixed),
   "Generate coat" (single coat), name box, validation via `PetNameRules`, then persist.
4. Settings window — rename, recreate pet with confirmation; never edit breed/coat.
5. Wire `App` to show the wizard only when `PetStateStore.Load()` has no usable snapshot and
   remove the provisional "Kitty" pet.

## Known limits / notes

- Atlas art is procedurally generated (no AI image tool in this environment); see
  `assets/sprites/README.md`. It is a real, validated sprite sheet, not a placeholder.
- No tray icon, wizard or settings window yet (stage 4); the app is currently started and
  stopped from the command line only.
- Autosave only runs for an already persisted pet; the provisional "Kitty" is intentionally
  not written until the creation wizard exists (stage 4).
- Repositioning after a live display/taskbar change is implemented but only observed on a
  single-monitor setup, so hot-plug remains unverified.
