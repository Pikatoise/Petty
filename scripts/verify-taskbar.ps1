param(
    [string]$Exe = "src\Petty.App\bin\Debug\net10.0-windows10.0.19041.0\Petty.exe",
    [string]$OutDir = "$env:TEMP\petty-taskbar"
)

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint data, IntPtr extra);
  [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

[void][W]::SetProcessDpiAwarenessContext([IntPtr](-4))
Add-Type -AssemblyName System.Drawing

# Keep the display awake: a sleeping monitor produces blank screenshots and the
# shell does not animate its auto-hide taskbar.
[void][W]::SetThreadExecutionState([uint32]2147483651)   # ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
for ($i = 0; $i -lt 4; $i++) { [W]::mouse_event(0x0001, 3, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 120 }

$exePath = (Resolve-Path $Exe).Path
Remove-Item $OutDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $OutDir | Out-Null

$process = Start-Process -FilePath $exePath -ArgumentList @("--data-dir", $OutDir, "--create-name", "Kitty", "--exit-after", "62") -PassThru
Start-Sleep -Seconds 5

$report = Get-Content (Join-Path $OutDir "startup-report.json") -Raw | ConvertFrom-Json
$pet = [IntPtr][Int64]$report.window.handle
$log = Join-Path $OutDir "petty.log"
$rect = New-Object W+RECT

function PetRect([string]$tag) {
    [void][W]::GetWindowRect($pet, [ref]$rect)
    Write-Host ("{0,-24} window {1},{2} {3}x{4}  (floor = top + 159 = {5})" -f `
        $tag, $rect.Left, $rect.Top, ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top), ($rect.Top + 159))
}

function Shot([string]$name) {
    $bmp = New-Object System.Drawing.Bitmap(2560, 1600)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size(2560, 1600)))
    $g.Dispose()
    $bmp.Save((Join-Path $OutDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Write-Host "--- startup ---"
PetRect "taskbar hidden"
Get-Content $log | Select-String -Pattern "Walk area|Taskbar" | Select-Object -Last 3

# Track how far the pet travels while the taskbar stays retracted.
$xMin = [double]::MaxValue; $xMax = [double]::MinValue
for ($i = 0; $i -lt 40; $i++) {
    [void][W]::GetWindowRect($pet, [ref]$rect)
    $xMin = [Math]::Min($xMin, $rect.Left)
    $xMax = [Math]::Max($xMax, $rect.Left)
    Start-Sleep -Milliseconds 500
}
Write-Host ("walked between x={0} and x={1} while hidden" -f $xMin, $xMax)

# Reveal the auto-hide taskbar by parking the cursor on the bottom edge.
[void][W]::SetCursorPos(1280, 1599)
Start-Sleep -Milliseconds 2500
Write-Host "--- taskbar revealed ---"
PetRect "taskbar shown"
Shot "taskbar-shown.png"
Get-Content $log | Select-String -Pattern "Walk area|Taskbar" | Select-Object -Last 3

# Move away again; the bar retracts and the pet should come back down.
[void][W]::SetCursorPos(1280, 600)
Start-Sleep -Milliseconds 3000
Write-Host "--- taskbar retracted ---"
PetRect "taskbar hidden again"
Get-Content $log | Select-String -Pattern "Walk area|Taskbar" | Select-Object -Last 3

[void]$process.WaitForExit(90000)
Write-Host ("exit={0}" -f $process.ExitCode)
[void][W]::SetThreadExecutionState([uint32]2147483648)   # back to ES_CONTINUOUS only
