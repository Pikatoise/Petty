# Petty portable release

Version 1.0.0 ships eight breeds / forty coats. The packaged RU/EN README and
[owner acceptance checklist](RELEASE_CHECKLIST.md) describe usage and the remaining
real-desktop acceptance work.

Windows 11 x64 is the primary target; Windows 10 x64 is best effort. Extract the
entire ZIP into a writable folder and start `Petty.exe`. Keep `Assets` and all DLLs
beside the executable. The .NET runtime is included; no SDK, installer or administrator
rights are needed. This is folder deployment, not a single-file application.

## Startup and data

After the first successful pet startup, Petty enables autostart by default using
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Petty`. Settings can
disable it; the disabled preference is respected on later starts. Moving the folder
requires one manual launch from the new location to repair the quoted executable path.
Windows can delay startup or independently disable startup applications in Task Manager.
An inaccessible registry or unsupported path is logged and shown inline in Settings;
Petty continues running. Paths exceeding the Run key's 260-character command limit
must be shortened.

User files stay in `%LOCALAPPDATA%\Petty` (`pet-state.json`, its `.bak`, preferences
and logs); the ZIP contains none of them. A second simultaneous launch exits without
creating another pet/tray icon. Explicit `--data-dir` or `PETTY_DATA_DIR` diagnostic
runs skip registry synchronization so temporary builds cannot replace the regular
startup entry. Their preferences can still be exercised independently.

Tray Exit plays departure. Windows shutdown/logoff saves immediately, does not ask
questions or wait for animation, and releases timers, icons and shell subscriptions.
After Explorer announces a new taskbar, NotifyIcon restores its existing icon;
Petty dismisses the old tray popup and reattaches its desktop windows. The experimental
WorkerW mode remains unsupported on the verified shell; use the default AboveDesktop.

## Manual updates

Download a newer ZIP manually from the project's GitHub Releases. Exit Petty using
the tray, replace the distribution files (including Assets), then reopen Petty.
Keep `%LOCALAPPDATA%\Petty` intact. There is no built-in updater or crash restart loop.
To remove Petty, disable autostart in Settings, exit, then delete its extracted folder.
User data can be retained for a later installation.

## Build and verification

From the repository root with .NET SDK 10:

```powershell
dotnet build Petty.slnx -c Release -m:1
dotnet test Petty.slnx -m:1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-portable.ps1
dotnet run --project scripts/WindowsReleaseVerification -c Release
```

The packaging script creates a fresh directory and ZIP under `artifacts/portable/`,
publishes Release `win-x64 --self-contained true` without trimming, checks runtime files
and forty external sprite pairs, then adds a bilingual README, this guide and the
manual checklist. ZIP names include the project version. Release excludes developer UI.
The Windows harness uses isolated data and a disposable HKCU test key, never the Run
key or the owner's pet. Set `PETTY_VERIFY_SESSION_ENDING=1` to exercise the simulated
WPF session-ending path instead of normal animated exit.
Set `PETTY_VERIFY_MISSING_ASSET=1` to decline the recovery prompt and check that the
existing state remains byte-identical. The ordinary lifecycle modes also launch a
second process and require it to exit with the single-instance status.

Owner checks: actual Windows logoff/reboot, Explorer restart (one icon, reopened tray
popup, normal z-order and click-through), Windows 10, and execution on a clean PC
without .NET. A targeted TaskbarCreated simulation is not an actual Explorer restart.

Implementation references: [Run keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys),
[WPF session ending](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.sessionending?view=windowsdesktop-10.0),
[NotifyIcon shell recovery](https://github.com/dotnet/winforms/blob/main/src/System.Windows.Forms/System/Windows/Forms/NotifyIcon.cs),
[self-contained deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/).
