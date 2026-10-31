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

Creator windows are rendered offscreen at 200% into `artifacts/session10/creator-*.png`.
`artifacts/session10/play/` contains a contact sheet and standalone animated preview
sampled by the real Core scene implementation, with the runtime WPF frame controller.
This does not modify pet data, registry settings, preferences or the running pet.
Real desktop interaction, animation quality and native display/DPI changes remain manual.
