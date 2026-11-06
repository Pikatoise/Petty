# Petty - project instructions

## Working agreement

- Respond briefly in Russian. Write code, comments and documentation in English;
  every Markdown file except `README.md` must be in English.
- Commit, push and build distribution ZIPs only when explicitly requested.
- Review proposed `README.md` changes with the owner before editing it.
- Follow `.editorconfig` and `Settings.XamlStyler`. Use `scripts/format.ps1` to
  format code and `scripts/format.ps1 -Check` to verify it.
- Keep changes focused; report changes, checks and remaining uncertainty.
- Keep developer setup in `DEVELOPMENT.md` and agent rules in these instructions.
  `docs/` is temporary, git-ignored input; exclude it from commits and releases.
  Avoid session handoff archives and progress logs.
- Use `artifacts/` for disposable verification output and remove it after the task.
  Retain only the newest requested portable release under `artifacts/portable/`.
- Prefer built-in .NET and existing modules; justify additional dependencies.

## Read the relevant instructions

- **Desktop:** read [Desktop instructions](src/Petty.App/AGENTS.md) before changing
  `Petty.App`, `Petty.Core`, `Petty.Assets`, desktop tests, sprite assets or Windows
  verification/publishing scripts.
- **Backend:** read [Backend instructions](src/Petty.Server.Api/AGENTS.md) before
  changing any `Petty.Server.*` project, server tests or deployment configuration.
- **Shared online client or protocol:** read both documents when changing
  `Petty.Online.Client` or REST/SignalR contracts consumed by the desktop.

These documents supplement this file. Their rules also apply to the related
projects and scripts listed above, outside their own directories.

## Project boundaries and validation

Petty has a Windows desktop client and an independently buildable Windows/Linux
backend. Optional accounts, friends and letters are implemented; local simulation
and local saves remain independent of network availability. Sounds, runtime AI
and an automatic updater are outside the product scope.

- `Petty.Desktop.slnx` builds the client; `Petty.Server.slnx` builds the backend.
  Server projects must remain independent of Desktop, Core, Assets, WPF and Win32.
- `Petty.Online.Client` is platform-neutral and independent of server assemblies,
  WPF and the local pet model. Keep server configuration and credentials out of
  `pet-state.json`.
- Use .NET SDK 10. Run targeted checks during development and the relevant
  solution/harness checks from the component instructions before completion.
  Cross-component changes require both sets of checks.
- Preserve regression coverage when removing redundant tests; test count is not
  a quality target. Documentation-only changes need link/content checks rather
  than application builds.
