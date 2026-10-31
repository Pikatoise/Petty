# Petty 1.0.0 — integration review, 2026-11-01

Result: v1 implementation and portable artifact prepared; **owner acceptance pending**.
The authoritative roster is eight breeds / forty coats, superseding stale counts in
the session plan. Theme and roster were already owner-selected. No v2 work or release
upload was performed. Runtime code is commit `12f17f3`, based on `09a34d1`.

## Fixes from this review

- A save after corrupt-primary recovery preserves the valid .bak. A regression test
  reproduces recovery, save, another primary corruption and successful recovery again.
- Zero happiness stops wandering until interaction replenishes it. Hunger with food
  still wins; tired/sleep states accept play/petting and exit.
- Explorer recovery resumes hover detection when closing a visible interaction menu.
- Initial creation must save successfully before starting a pet. Recreation saves
  before disposing the old session; a failed rename rolls back identity and shows an
  inline error instead of claiming success. Isolated WPF tests exercise denied saves.
- Native error/recovery boxes use no alert-icon flags; no sound playback was added.
  The project version is explicit; the ZIP has a versioned name and bilingual README.

## Checks actually run

| Check | Result / boundary |
|---|---|
| `dotnet build Petty.slnx -c Release -m:1` | Passed, 0 warnings/errors |
| `dotnet test Petty.slnx -c Release --no-build -m:1` | 173 passed |
| `dotnet test Petty.slnx -m:1` | 181 passed, includes Debug-only tests |
| `dotnet run --project src/Petty.Assets -c Release --no-build -- --verify` | All packs/shared assets valid |
| `dotnet run --project scripts/ContentVerification -c Release` | 40 atlases, 2560 frozen/hit-tested frames, 8 silhouettes, 16 RU/EN creator previews; coat wrap/recreation, deterministic PNGs |
| Localization XML parity | 117 keys per language; RU cat / EN dog renders inspected, no claim of live DPI acceptance |
| `dotnet run --project scripts/WindowsReleaseVerification -c Release` | Registry helper against disposable HKCU key, single instance, simulated shell recovery, reopened tray, hover restart, failed rename/recreation, animated exit and reload passed |
| Same harness with `PETTY_VERIFY_SESSION_ENDING=1` | Simulated logoff saves promptly, does not cancel, skips departure; passed |
| Same harness with `PETTY_VERIFY_MISSING_ASSET=1` | Declined recovery leaves JSON byte-identical; passed |
| `dotnet list src/Petty.App/Petty.App.csproj package --include-transitive --no-restore` | Existing WPF-UI and WPF-UI.Abstractions 4.3.0 only; no new package |
| `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-portable.ps1` | Release win-x64 self-contained, no trimming/single-file; external 40 PNG/40 JSON, runtime files, docs/licenses, no user data/dev config |
| Extracted ZIP smoke | Own coreclr.dll and assets; spaces in launch path; moved-folder restore; six-year-old schema-v1 timestamp causes no offline decay; animated shutdown passed |

Review by code inspection covered clock/power subscriptions, timer and prop ownership,
no queues/overlapping actions, +25/cap-100 bowl/pour lock, scene cleanup and shared play
clock, 6-second default play, atomic schema-v1 persistence/recovery, shell styles/input,
primary monitor floor, RU/EN creator/settings/tray, and Release preprocessor guards.
No runtime network/media playback or prototype PlayChip/Generate Coat entry points were
found. Debug window is absent in the loaded Release assembly. The system-tray popup is
temporarily topmost; pet, effects and hover menu remain non-topmost.

## Review axes

Standards: no actionable findings from the independent read-only review.
Spec: no implementation findings; one stale architecture sentence was corrected.
The procedural petting hand/reaction is present; separate decorative hearts are not
implemented (optional in session 06). Subjective animation/art quality remains manual.

## Artifact and acceptance

- ZIP: `artifacts/portable/20261101-020719-8f5ec6/Petty-1.0.0-win-x64.zip`
- Size: 83,323,812 bytes.
- SHA256: `50F0BB9E051C969C303CF0806D1C0D7CBBBFC42DE93D224C54D7CF939BFA6610`
- Smoke evidence: adjacent `smoke-result.json` and `isolated release data/`.
- All tests used isolated data; the owner's pet and actual Run value were untouched.

Use [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) for actual first-run/autostart, sleep,
lock/fullscreen, Explorer restart, logoff/reboot, DPI/desktop input, Windows 10 and
clean-PC acceptance. No real OS shutdown, Explorer kill or prolonged soak was run.
Windows 11 x64 / primary monitor is the target; WorkerW remains experimental.
Unexplained dispatcher gaps over five seconds are discarded by the active clock,
so a long UI stall can lose elapsed running time. Corrupt preferences fall back to
defaults with a logged warning; only pet state has .bak recovery.

The v1 handoff ends here. Publishing to GitHub and any v2 work require separate scope.
