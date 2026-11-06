# Content integration verification

From the repository root on Windows:

```powershell
dotnet run --project scripts/ContentVerification -c Release
```

Loads all forty installed atlases through the WPF runtime loader, validates every pack,
checks frozen frames and alpha hit testing, checks eight distinct silhouettes, exercises
both species, all breed previews, coat cycling/wraparound and existing-coat recreation
in RU/EN, and compares regenerated PNG bytes
against the repository assets after WPF DPI initialization.

Creator content is rendered without showing windows at 200% into `artifacts/verification/content/creator-*.png`.
Adaptive layout checks cover RU/EN dialogs and tray content at normal and compact DIP budgets,
including creator reflow and scrolling to final actions. Interaction action previews cover
720p, Full HD, the reference display and 4K at representative DPI scales.
Tutorial checks exercise four independent WPF Pages in a Frame, passive scenes in RU/EN,
first-run/replay navigation, fixed header/footer bounds, last-page Skip visibility,
completion-save failure feedback and scrolling confined to each page. Preview frames are written
to `artifacts/verification/content/tutorial-*.png` without showing tutorial windows.
Online window checks use an in-memory token store and fake HTTP responses, without
starting SignalR or contacting a server. They render account/friends/composition/history/
incoming views in RU/EN, verify compact friends scrolling, quota refresh after 10/10,
and closing/reopening and acknowledging an incoming snapshot. They do not establish
live network behavior or replace the two-account Desktop acceptance check.
Additional regressions hold a friend mutation in flight while publishing newer
snapshots, verify unchanged mailbox publications retain friend rows, and load 1000
history letters while checking bounded/recycled visual rows and cursor/reset behavior.
A hidden native pet window counts placement messages to verify identical rounded
geometry skips positioning while movement, floor, scale and reattachment still apply.
`artifacts/verification/content/play/` contains a contact sheet and standalone animated preview
sampled by the real Core scene implementation, with the runtime WPF frame controller.
This does not modify pet data, registry settings, preferences or the running pet.
Real desktop interaction, animation quality and native display/DPI changes remain manual.
Verification output is disposable and can be deleted after inspection.
