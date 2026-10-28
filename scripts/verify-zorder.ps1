param(
    [string]$Exe = "src\Petty.App\bin\Debug\net10.0-windows10.0.19041.0\Petty.exe",
    [string]$OutDir = "$env:TEMP\petty-zorder"
)

$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Z {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint data, IntPtr extra);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

[void][Z]::SetProcessDpiAwarenessContext([IntPtr](-4))
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$exePath = (Resolve-Path $Exe).Path
Remove-Item $OutDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $OutDir | Out-Null

$process = Start-Process -FilePath $exePath -ArgumentList @("--data-dir", $OutDir, "--create-name", "Kitty", "--exit-after", "22") -PassThru
Start-Sleep -Seconds 5

$report = Get-Content (Join-Path $OutDir "startup-report.json") -Raw | ConvertFrom-Json
$pet = [IntPtr][Int64]$report.window.handle
$rect = New-Object Z+RECT
[void][Z]::GetWindowRect($pet, [ref]$rect)
$cx = [int](($rect.Left + $rect.Right) / 2)
$cy = [int](($rect.Top + $rect.Bottom) / 2)
Write-Host ("pet at {0},{1} {2}x{3}" -f $rect.Left, $rect.Top, ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))

function Centre-Pixel {
    $bmp = New-Object System.Drawing.Bitmap(64, 64)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($cx - 32, $cy - 32, 0, 0, (New-Object System.Drawing.Size(64, 64)))
    $g.Dispose()
    # Return the most saturated colour in the patch so the cat is easy to spot.
    $best = $null; $bestScore = -1
    for ($y = 0; $y -lt 64; $y += 4) {
        for ($x = 0; $x -lt 64; $x += 4) {
            $p = $bmp.GetPixel($x, $y)
            $score = [Math]::Abs($p.R - $p.G) + [Math]::Abs($p.G - $p.B)
            if ($score -gt $bestScore) { $bestScore = $score; $best = $p }
        }
    }
    $bmp.Dispose()
    return $best
}

$catPixel = Centre-Pixel
Write-Host ("desktop exposed: centre pixel R{0} G{1} B{2} (expect the cat's orange/white)" -f $catPixel.R, $catPixel.G, $catPixel.B)

# 1. An ordinary window placed over the pet must cover it.
$cover = New-Object System.Windows.Forms.Form
$cover.FormBorderStyle = 'None'
$cover.StartPosition = 'Manual'
$cover.SetBounds([Math]::Max($rect.Left - 50, 0), [Math]::Max($rect.Top - 50, 0), 400, 400)
$cover.BackColor = [System.Drawing.Color]::Magenta
$cover.Show()
[void][Z]::SetForegroundWindow($cover.Handle)
Start-Sleep -Milliseconds 900
$coveredPixel = Centre-Pixel
Write-Host ("covered by a normal window: centre pixel R{0} G{1} B{2} (expect 255,0,255)" -f $coveredPixel.R, $coveredPixel.G, $coveredPixel.B)

$covered = ($coveredPixel.R -eq 255 -and $coveredPixel.G -eq 0 -and $coveredPixel.B -eq 255)
Write-Host ("z-order check: pet is covered by an ordinary window = {0}" -f $covered)

$cover.Close()
[void]$process.WaitForExit(60000)
Write-Host ("exit={0}" -f $process.ExitCode)
Write-Host "--- click log ---"
Get-Content (Join-Path $OutDir "petty.log") | Select-String -Pattern "Pet clicked|Behaviour" | Select-Object -Last 6

