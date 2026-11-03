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
`artifacts/verification/content/play/` contains a contact sheet and standalone animated preview
sampled by the real Core scene implementation, with the runtime WPF frame controller.
This does not modify pet data, registry settings, preferences or the running pet.
Real desktop interaction, animation quality and native display/DPI changes remain manual.
Verification output is disposable and can be deleted after inspection.
