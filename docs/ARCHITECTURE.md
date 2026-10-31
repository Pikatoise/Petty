# Petty architecture

Orientation for v1.0: what lives where, how a running pet is wired,
and which decisions are deliberate. The sprite sheet is described in
[../assets/sprites/README.md](../assets/sprites/README.md).

## Shape

Petty is a tray application with no main window. Small layered WPF windows belong to
the desktop (pet, bowl, ball, hand, pouring effect and interaction menu) and are inserted just above the shell's
desktop window, so ordinary windows cover them and they are never a topmost overlay.
All pet logic lives in a plain .NET library without WPF or Win32, so it can be tested
headlessly.

## Projects

| Project | Role | Depends on |
|---|---|---|
| `Petty.Core` | Domain: needs, bowl, behaviour FSM, motion, walk surface, floor easing, tuning, JSON state | nothing |
| `Petty.Assets` | Console tool: draws the atlas and props, writes PNG/metadata, validates them | `Petty.Core` metadata types |
| `Petty.App` | WPF shell: windows, tray, wizard, settings, timer loop, desktop integration, composition root | `Petty.Core` |
| `Petty.Tests` | xUnit tests for everything in `Petty.Core` plus the shipped asset files | `Petty.Core`, `Petty.Assets` |

The app references the generated sprite files as content (`Assets/Sprites/...` next to the
executable) and falls back to the repository's `assets/sprites` folder when run from source.

## Startup and shutdown

`App.OnStartup` (composition root) does, in order:

1. `ShutdownMode.OnExplicitShutdown` — closing a window never ends the app.
2. Parse `AppOptions`, resolve `PetPaths` (`--data-dir`, else `PETTY_DATA_DIR`, else
   `%LOCALAPPDATA%\Petty`), open the log file.
3. Take the `Local\Petty.SingleInstance` mutex; a second copy exits with code 3.
4. `PetStateStore.Load()`: a valid snapshot is restored without offline decay;
   `--create-name` creates a pet instead; otherwise the first-run wizard is shown and
   cancelling it shuts the app down explicitly.
5. Load and validate the selected sprite sheet, then create pet and prop windows and
   attach them to the desktop layer. Missing saved art offers explicit recovery;
   declining leaves the old JSON untouched. The retired cat appearance migrates only
   after the British Shorthair blue atlas loads.
6. Start `PetSession` (behaviour + animation + placement), the autosave timer and the tray
   icon, synchronize the autostart preference, and write `startup-report.json`.

Normal shutdown goes through `FinishAndShutdown`: save state, write the final report, close
windows, dispose the tray and the mutex, then `Shutdown()`. The tray's **Exit application**
plays the exit clip first (`PetSession.RequestExit` → `Exiting` → animation end); closing the
settings window changes nothing. WPF `OnSessionEnding` saves immediately without
departure, diagnostics or cancelling logoff/shutdown; `OnExit` releases resources.
`ShellRestartWatcher` receives TaskbarCreated and reattaches desktop windows; NotifyIcon
restores its existing tray icon. Autostart uses a quoted current executable in HKCU Run,
repairs moved paths, respects disabled preferences and skips isolated diagnostic runs.

## Runtime loop

`PetSession` is the only bridge between the domain and the windows:

- `PetRuntime` holds `Needs`, the `Bowl`, the `PetBehaviorMachine` and the snapshot; the
  session feeds it elapsed time and reacts to `Behavior.StateChanged` / its transition log.
- `PetAnimationMap` maps a behaviour state to one clip name; `AnimationController` advances
  the clip by elapsed time and emits frames. Behaviour decides, animation only renders.
- `PetMotion` moves the window position at the current walk/run speed towards a target that
  came from the behaviour machine; walks end on arrival, durations are safety caps.
- `DesktopSurfaceController` tracks the primary display and taskbar, using `WalkSurface` for
  the walk band and `FloorFollow` to ease the floor at ~900 px/s. The session polls it every
  250 ms; the display layout is re-read every ~4 s.
- `PetPropsController` places the bowl at its saved horizontal position on the current floor
  (`PropWindow.PlaceOnFloor`), updates its picture, and computes the food-seeking target.
  While `Playing`, a shared absolute scene sampler selects both sprite frames and
  ball positions using breed contact metadata. `BowlDisplay` owns staged food images
  and the non-queueing 1.2-second pouring effect.

Timers: frame ~20 ms (the clip's own rate while nothing moves), simulation 200 ms
(`PetTuning.TickInterval`), taskbar 250 ms, autosave 2 min. The frame timer also tracks
pointer position at 20 ms while the interaction menu is visible (75 ms while hidden).
Animation and simulation
share a monotonic active-time clock; power notifications exclude sleep and hibernation,
and an unexplained gap over five seconds is discarded. All timers are stopped on dispose.

## Behaviour

`PetBehaviorMachine` states: `Entering`, `Idle`, `Walking`, `Running`, `SeekingFood`,
`Eating`, `Sleeping`, `Playing`, `Petting`, `Tired`, `Exiting`. Priority is exit > eligible user action > eating >
hunger > sleep > idle; every transition carries a short reason that is logged and tested.
Eating requires food in the bowl; waking from sleep still accepts user interaction.

Needs are two independent 0–100 values (satiety, happiness) that decay at
`PetTuning` rates. Below the hungry threshold the cat looks tired and, with food available,
walks to the bowl; play and petting raise happiness. At zero happiness or very low satiety,
wandering stops in favor of tired/sleep behavior; interactions remain available. No numbers are shown on
the desktop.

## Desktop integration

- **Placement:** `DesktopWindowService.Attach` inserts the window after
  `GetWindow(Progman, GW_HWNDPREV)` (`AboveDesktop`, the default). Reparenting into the shell
  wallpaper window (`--desktop-layer=workerw`) exists as an experiment; a reparented WPF
  layered window renders nothing on this shell, so it is not the default.
- **No activation:** `ShowActivated="False"`, `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`.
- **Click-through:** `WM_NCHITTEST` answers `HTTRANSPARENT` for pixels that are transparent in
  the sprite (or for the whole ball). Only the cat, the bowl and the visible interaction menu take
  input.
- **Alt+Tab and the taskbar:** `ShowInTaskbar="False"` hides the taskbar button;
  `DesktopWindowService.MarkAsToolWindow` adds `WS_EX_TOOLWINDOW` with an add-only
  read-modify-write. The style word is never rewritten wholesale — replacing WPF's layered
  styles makes the window render nothing.
- **Floor line:** the walk floor is the monitor edge, or the top of a taskbar that is really
  on screen (`ShellBarProbe` reads the bar rect and hit-tests it, so an auto-hidden bar does
  not count). The pet cannot be painted over the topmost taskbar, so standing on its edge is
  the accepted behaviour. Props use the same floor row, so the bowl steps up with the bar.
- **Strategy choice:** `--desktop-layer above|workerw|none` and `--no-desktop-layer` are
  diagnostic switches.

## Windows and input

The settings window demonstrates Cozy Home resources based on WPF UI 4.3.0 and live RU/EN
switching. `preferences.json` stores the language override and autostart preference
separately from pet state; system UI language defaults to Russian for `ru` locales
and English otherwise. The creator, pet action and tray commands also use these strings;
autostart registration follows the saved preference; public Release excludes developer UI.

The pet window is 192×192 px (64×64 sprite at 3 screen pixels per sprite pixel), the bowl
72×72, the ball/hand 48×48. The physical size is fixed; only the WPF (DIP) size follows the
monitor's DPI. Input handling lives in each window's `WndProc`:

- cat: opaque pixel click = user interaction; hovering its exposed window box shows an
  attached Play/Pet icon menu without making transparent pixels intercept clicks;
  The menu is hidden during eating, playing, petting and exit;
- bowl: opaque pixel press counts as a click (+25 food) unless the pointer moves 4 px, which
  turns it into a two-axis drag within the primary work area; on release the session drops it back onto the floor and
  persists the horizontal position only;
- ball: always click-through.

## Persistence

`PetSnapshot` is the versioned state (`schemaVersion` 1, camelCase JSON): name, breed, coat,
asset id, satiety, happiness, `lastUpdatedUtc`, bowl food and the bowl's monitor-relative x
with the display device name. `PetStateStore` writes atomically (temp file + replace) and
keeps a `.bak`; loading recovers missing or malformed files from a valid backup. Saving
after recovery preserves the valid backup rather than replacing it with damaged JSON. Newer
unsupported versions stop startup and are preserved, including when found in the backup.
Needs do not decay while the app is closed; `lastUpdatedUtc` remains a diagnostic timestamp.
Saves happen on create, rename,
feed, eat, play, bowl drop, every 2 minutes and on shutdown.

## Sprite pipeline

`Petty.Assets` is a deterministic pixel renderer, not an image model: `BreedSpriteRenderer`
and `BreedPackBuilder` use `assets/breed-designs.json` to produce forty 512×512 atlases
(8×8 cells of 64×64, nine clips, 64 frames per coat). Each breed has five coats and an
explicit default. Nested PNG/JSON pairs live in `assets/sprites/packs/<breed>`.
`PropSpriteBuilder` draws bowl levels, ball, hand and pouring frames; `TrayIconBuilder`
draws the icon. `AssetValidator`
re-decodes the result and fails on wrong size, semi-transparent or off-palette pixels, empty
frames, a broken ground line, a bad walk loop or invalid metadata. The app's
`SpriteSheetLoader` applies the same checks at startup, so a hand-made sheet is accepted only
if it follows the documented layout; a malformed sheet is refused with an actionable error
instead of being drawn.

## Diagnostics and errors

Everything technical goes to `petty.log`; `startup-report.json` and `final-report.json`
contain machine-readable geometry (window rects, styles, hit tests, walk area, bowl/ball
positions, desktop attachment) used by the verification scripts. Fatal startup errors show a
short dialog with the log path. Asset and persistence failures are logged, never silent, and
failed rename/recreation saves produce inline feedback. Creation must be persisted before
the old session is replaced or the first session starts.

## Tests

`dotnet test Petty.slnx` runs the WPF-free suite: need bounds and active-time decay,
feeding and bowl inventory, hunger-driven seeking, play happiness, every FSM
transition, JSON round-trip and recovery, bowl clamping, motion and floor easing, walk
surface, animation playback, metadata validation and the shipped asset files. Desktop
behaviour (z-order, click-through, taskbar, drag) is checked with the PowerShell scripts in
`scripts/` and manually on a real desktop.

## Deliberate constraints

- Classic 2D pixel art only: fixed palette, hard edges, 3× nearest-neighbour scaling, no
  anti-aliasing or smooth transforms.
- The pet is not topmost and has no taskbar button; the tray icon is the only entry point.
- One active pet; eight breeds and forty coats ship in v1. Breeds and coats are discovered
  from assets and configuration, without new window code.
- `Petty.App` builds with `InvariantGlobalization=false`: WPF's text caret needs a real
  `CultureInfo` for the input language, otherwise text boxes throw on focus.
- No networking, database, cloud or runtime AI. WPF UI 4.3.0 supplies scoped control
  templates; package license/notices ship alongside the self-contained runtime.

## Where to extend

- New coat/breed: new atlas + metadata in a pack directory; clip names come from metadata.
- New behaviour: a state in `PetActivity`/`PetBehaviorMachine`, a clip in `PetAnimationMap`,
  and (if it moves) a target in `PetSession`. Add a transition test.
- Tuning: `PetTuning` owns every rate, threshold and duration used by the domain.
