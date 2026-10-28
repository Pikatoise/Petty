param(
    [string]$Exe = "src\Petty.App\bin\Debug\net10.0-windows10.0.19041.0\Petty.exe",
    [string]$OutDir = "$env:TEMP\petty-stage3e",
    [int]$ExitAfter = 16
)

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder b, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint data, IntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public static string Cls(IntPtr h) { var b = new StringBuilder(256); GetClassName(h, b, 256); return b.ToString(); }
}
"@

[void][W]::SetProcessDpiAwarenessContext([IntPtr](-4))
Add-Type -AssemblyName System.Drawing

$exePath = (Resolve-Path $Exe).Path
Remove-Item $OutDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $OutDir | Out-Null

$process = Start-Process -FilePath $exePath -ArgumentList @("--data-dir", $OutDir, "--create-name", "Kitty", "--exit-after", "$ExitAfter") -PassThru
Start-Sleep -Seconds 5

$pet = [IntPtr]::Zero
$all = New-Object System.Collections.ArrayList
[W]::EnumWindows({ param($h, $l)
    if ([W]::Cls($h).StartsWith("HwndWrapper[Petty")) {
        $r = New-Object W+RECT
        [void][W]::GetWindowRect($h, [ref]$r)
        [void]$script:all.Add([pscustomobject]@{
            Handle = $h; Left = $r.Left; Top = $r.Top
            Width = $r.Right - $r.Left; Height = $r.Bottom - $r.Top
            Visible = [W]::IsWindowVisible($h)
        })
    }
    return $true
}, [IntPtr]::Zero) | Out-Null

foreach ($candidate in $all) {
    Write-Host ("petty window 0x{0:X} rect={1},{2} {3}x{4} visible={5}" -f `
        $candidate.Handle.ToInt64(), $candidate.Left, $candidate.Top, $candidate.Width, $candidate.Height, $candidate.Visible)
}

$pet = ($all | Where-Object { $_.Visible -and $_.Width -eq 192 } | Select-Object -First 1).Handle
if (-not $pet) { throw "The pet window was not found." }

$rect = New-Object W+RECT
[void][W]::GetWindowRect($pet, [ref]$rect)
Write-Host ("pet=0x{0:X} rect={1},{2} {3}x{4} visible={5} iconic={6}" -f `
    $pet.ToInt64(), $rect.Left, $rect.Top, ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top), `
    [W]::IsWindowVisible($pet), [W]::IsIconic($pet))

# Hide every window except the pet, the shell desktop and the taskbar.
$progman = [W]::FindWindow("Progman", [NullString]::Value)
$hidden = 0
[W]::EnumWindows({ param($h, $l)
    $class = [W]::Cls($h)
    $keep = $h -eq $script:pet -or $h -eq $script:progman -or
            $class -eq 'Shell_TrayWnd' -or $class -eq 'Shell_SecondaryTrayWnd' -or
            $class -eq 'Progman' -or $class -eq 'WorkerW' -or
            $class.StartsWith('HwndWrapper[Petty')
    if (-not $keep -and [W]::IsWindowVisible($h) -and -not [W]::IsIconic($h)) {
        [void][W]::ShowWindow($h, 6)  # SW_MINIMIZE
        $script:hidden++
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
Write-Host ("minimised {0} windows; pet iconic={1}" -f $hidden, [W]::IsIconic($pet))

Start-Sleep -Milliseconds 1200

function Save-Shot([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap(2560, 1600)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size(2560, 1600)))
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

Save-Shot (Join-Path $OutDir "desktop.png")

# Does the cat actually cover screen pixels? Sample the sprite area for palette colours.
$bmp = [System.Drawing.Bitmap]::FromFile((Join-Path $OutDir "desktop.png"))
$orange = 0; $white = 0; $dark = 0
for ($y = $rect.Top; $y -lt [Math]::Min($rect.Bottom, 1600); $y += 2) {
    for ($x = [Math]::Max($rect.Left, 0); $x -lt [Math]::Min($rect.Right, 2560); $x += 2) {
        $p = $bmp.GetPixel($x, $y)
        if ($p.R -eq 232 -and $p.G -eq 137 -and $p.B -eq 43) { $orange++ }
        elseif ($p.R -eq 255 -and $p.G -eq 243 -and $p.B -eq 220) { $white++ }
        elseif ($p.R -eq 43 -and $p.G -eq 26 -and $p.B -eq 18) { $dark++ }
    }
}
$bmp.Dispose()
Write-Host ("sprite colour hits: orange={0} white={1} outline={2}" -f $orange, $white, $dark)

# Real mouse click on the cat's body, then on a transparent corner.
$cx = [int](($rect.Left + $rect.Right) / 2)
$cy = [int](($rect.Top + $rect.Bottom) / 2)
[void][W]::SetCursorPos($cx, $cy)
Start-Sleep -Milliseconds 150
[W]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)  # LEFTDOWN
[W]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)  # LEFTUP
Write-Host ("clicked cat body at {0},{1}" -f $cx, $cy)
Start-Sleep -Milliseconds 1200

[void][W]::SetCursorPos($rect.Left + 4, $rect.Top + 4)
Start-Sleep -Milliseconds 150
[W]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
[W]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Write-Host ("clicked transparent corner at {0},{1}" -f ($rect.Left + 4), ($rect.Top + 4))

$exited = $process.WaitForExit(60000)
Write-Host ("exited={0} code={1}" -f $exited, $(if ($exited) { $process.ExitCode } else { 'running' }))
Write-Host "--- log (clicks / behaviour) ---"
Get-Content (Join-Path $OutDir "petty.log") | Select-String -Pattern "clicked|Behaviour|Placement|above-desktop|SetParent" | Select-Object -Last 14
