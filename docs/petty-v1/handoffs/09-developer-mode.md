# Session 09 — Debug-only developer panel

- Stage document: `docs/petty-v1/sessions/09-developer-mode.md`
- Result: implemented, uncommitted. Open the Debug build tray menu and choose **Developer panel**. Release has no developer entry or handler.
- Panel shows behavior, clip/FPS, needs, food and walk area; edits needs, empties/fills the bowl and requests play/pet through normal eligibility. Decay rates, thresholds, and play/pet durations update the running pet and scene without replacing its state. Inputs are finite and clamped; threshold order is maintained. Reset restores tuning defaults.
- Overrides are stored atomically in `<data-dir>/dev-tuning.json`, separately from the pet and preferences files. Invalid startup config is logged and defaults are used. Debug edits to actual pet needs/food are saved as pet state.
- Checks: Debug and Release app builds succeeded without warnings; 153 Debug tests passed outside the sandbox. A WPF harness checked live apply, reload, non-finite input rejection and bowl command. Release assembly metadata was inspected: debug types and entry methods were absent. Desktop appearance, menu interaction and extended use remain owner checks. The temporary harness was removed.
- Ready for next stage? **yes**, subject to owner testing. Stage 10 needs explicit owner approval of breed and coat designs before asset production.

- Owner follow-up: tuning and need fields are sliders that apply immediately; writes are debounced and flushed when the window closes. Reset remains available. A WPF harness verified immediate changes, reset and save-on-close.

- Owner follow-up: developer menu, diagnostics, controls and status messages use RU/EN resources. Decay rates use units per minute throughout Core and panel. Legacy debug JSON hourly values are converted on load; default real-time decay is unchanged. Debug/Release builds, 154 Debug and 152 Release tests passed; a WPF harness verified language switching, minute calculations and migration.

- Owner follow-up: corrected the Debug petting default to 3 s; added eating interval, sleep, wander and run-chance tuning plus safe walk/run/sleep/wake commands. Interaction duration is fixed at scene start, so live duration changes apply to the next action without disrupting rewards or props. Failed debug/pet saves and invalid loaded settings are reported. A WPF harness checked controls, bounds, localization and persistence; 157 Debug and 152 Release tests passed.

- Owner follow-up: every tuning and need slider has a synchronized editable numeric field with locale-aware decimals and finite-value validation. Sleep chance is configurable (default 100%); idle time accumulates across walks, and failed sleep rolls wait for the next full idle interval. A Windows WPF harness checked input/slider synchronization and persistence; 159 Debug and 154 Release tests passed.

- Owner follow-up: the Debug panel has a persisted 0.1×–20× time-speed control. A shared scaled active clock advances pet behavior, needs, motion, animation and scenes while excluding sleep; desktop floor tracking, wall-clock timestamps and persistence remain real-time. Tray exit restores 1× so departure is not held by debug slowdown. Debug tests cover speed changes and suspend/resume; an isolated 10× launch entered in 0.404 s and exited cleanly. Release builds/tests passed; WPF control input and persistence were checked.

- Owner-reported sleep issue: removed the hidden happiness gate and allowed the tired, hungry pet to sleep when no food is available. Food seeking still takes priority. Regression tests reproduce low happiness and zero-needs cases; an isolated 20× Debug run reached Tired -> Sleeping with both needs at zero. 163 Debug and 156 Release tests passed.

- Owner follow-up: each sleep samples one duration from a configurable range, default 120–600 seconds. The Debug panel exposes both bounds; changes affect the next sleep and old `sleepSeconds` settings migrate to a suitable range. Deterministic tests cover independent samples, bounds and mid-sleep changes; WPF harness verified controls, localization and migration. An isolated 20× run slept 40 simulated seconds in 2.045 real seconds and woke normally. 165 Debug and 157 Release tests passed.

- Owner follow-up: developer controls are grouped into collapsible General, Needs and food, Sleep, Movement, and Play and petting sections. Diagnostics and status stay visible above the sections. A WPF harness checked all controls, commands, live localization and persistence, and a rendered preview was inspected.

- Owner follow-up: developer window layout and reusable labeled slider/input control were moved to XAML; only behavior remains in code-behind. The XAML pages are excluded from Release. Debug/Release builds passed, a WPF harness exercised categories, input, commands, live language and persistence, and Release assembly inspection found no Debug window/control types or XAML resources.
