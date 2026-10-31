# Petty 1.0.0 — owner acceptance

Every item below is **owner to verify**; unchecked items are not reported as passed.
Use a copy of important saves before recovery tests. Close any other Petty instance.
Diagnostic `--data-dir` runs skip real autostart registration; test autostart with a
normal launch. Keep the default AboveDesktop mode; WorkerW is experimental.

- [ ] Extract into a path with spaces; start Petty.exe. First launch detects Windows
  language, shows live previews, creates one named pet and registers autostart.
- [ ] Review all 8 breeds / 40 coats, their likeness, walk/run loops and eating/sleep
  readability. Coat arrows wrap and recreation starts on the current appearance.
- [ ] Switch RU/EN; check text fits and keyboard focus/tab order at 100%, 150%, 200%.
  Rename, cancel/confirm recreation; close Settings without stopping the pet.
- [ ] Hover the moving pet near every screen edge: two icons follow at normal speed,
  disappear on leaving, and never appear over a covering application. Transparent
  pixels still allow desktop/icon clicks; no pet/prop entries in Alt+Tab/taskbar.
- [ ] Play lasts about 6 s; paw and ball contact align in both directions; the ball
  rolls out. Petting shows hand/reaction. Repeated actions do not overlap. No actions
  interrupt eating/exit; scene props disappear after actions and pet replacement.
- [ ] Each bowl click adds 25 up to 100; no extra food from rapid clicks during pouring
  or from dragging. Levels decrease during eating; approach from both sides aligns
  the mouth. Taskbar reveal/hide moves the pet and bowl to the same floor.
- [ ] Lock/unlock and use fullscreen apps: running needs still advance. Sleep/hibernate
  and resume: no needs catch-up. Close/reopen after a long pause: needs are retained.
- [ ] With an interaction menu visible, restart Explorer: one tray icon returns, tray
  popup and pet hover reopen, windows stay behind ordinary apps, click-through works.
- [ ] Windows shutdown/logoff saves promptly without departure or prompts; normal tray
  Exit animates and removes all icons/windows. Next startup restores the pet.
- [ ] Move the portable folder; one manual launch repairs enabled autostart. Disable
  autostart, reopen and log in again: it stays disabled. Check Windows Task Manager
  startup settings separately if Windows suppresses the entry.
- [ ] With disposable data, decline missing-appearance recovery: old JSON is unchanged.
  Restore a corrupted primary from its valid .bak, save, corrupt the primary again:
  recovery remains possible. An unsupported newer schema stays untouched.
- [ ] Test on a clean Windows 11 x64 PC without .NET and, if needed, Windows 10 x64.
  Hot-plug/DPI changes must keep the primary-monitor pet and bowl visible.
- [ ] Observe normal daily use for orphan windows, memory/CPU growth, stuck animation,
  or unexpected logs. No formal 24/72-hour automated soak was performed.

Record OS/build, DPI, ZIP SHA256, result and any relevant petty.log excerpt when
reporting an issue. Do not mark an OS scenario passed from a simulated notification.
