# run.ps1 [-Seed] [-Screen n] : (re)starts the demo Drift on its own profile, with the web engine's
# debugging port open, centred on a screen (default: the first non-primary one, else the primary).
# -Seed writes a fresh session first: five regular tabs, the first three in a split.
# -Session file.json starts from that session instead (a fixed one for before/after comparisons).
param([switch]$Seed, [string]$Session = '', [int]$Screen = -1)
. "$PSScriptRoot\common.ps1"
if (-not (Test-Path "$App\Drift.exe")) { "no demo build in ${App}; run build.ps1 first"; exit 1 }
Get-DemoProcess | Stop-Process -Force
Start-Sleep -Milliseconds 800
New-Item -ItemType Directory -Force "$DemoHome\AppData\Local\Drift" | Out-Null
if ($Seed) {
  $tabs = @(
    @{ Url = 'https://example.com/'; Title = 'Example Domain'; Pinned = $false; Place = 'Regular' },
    @{ Url = 'https://www.wikipedia.org/'; Title = 'Wikipedia'; Pinned = $false; Place = 'Regular' },
    @{ Url = 'https://example.org/'; Title = 'Example Org'; Pinned = $false; Place = 'Regular' },
    @{ Url = 'https://example.net/'; Title = 'Example Net'; Pinned = $false; Place = 'Regular' },
    @{ Url = 'https://www.iana.org/'; Title = 'IANA'; Pinned = $false; Place = 'Regular' }
  )
  $session = @{ Tabs = $tabs; Active = 0; Folders = @(); Closed = @();
    Splits = @(@{ Pages = @(0, 1, 2); Shares = @(0.4, 0.3, 0.3); Row = 0 }); PullsFolderMade = $true }
  $session | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 "$DemoHome\AppData\Local\Drift\session.json"
}
if ($Session) { Copy-Item $Session "$DemoHome\AppData\Local\Drift\session.json" -Force }
Add-Type -AssemblyName System.Windows.Forms
Add-Type 'using System;using System.Runtime.InteropServices;public static class DemoWin{[DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f);[StructLayout(LayoutKind.Sequential)]public struct R{public int l,t,r,b;}[DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out R r);}'
# BOTH must point at the demo's home, or Drift finds the user's real profile. Only for the demo: put back after,
# so whatever runs next in this shell sees the user's own folders.
$saved = $env:USERPROFILE, $env:LOCALAPPDATA, $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
$env:USERPROFILE = $DemoHome
$env:LOCALAPPDATA = "$DemoHome\AppData\Local"
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$DebugPort"
try { $p = Start-Process "$App\Drift.exe" -PassThru }
finally { $env:USERPROFILE, $env:LOCALAPPDATA, $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = $saved }
$p.Id | Set-Content $PidFile
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 400 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 15; $p.Refresh(); $h = $p.MainWindowHandle }
$screens = [System.Windows.Forms.Screen]::AllScreens
$s = if ($Screen -ge 0) { $screens[$Screen] } else { $screens | Where-Object { -not $_.Primary } | Select-Object -First 1 }
if (-not $s) { $s = [System.Windows.Forms.Screen]::PrimaryScreen }
$r = New-Object DemoWin+R; [void][DemoWin]::GetWindowRect($h, [ref]$r)
$w = $r.r - $r.l; $hh = $r.b - $r.t
$x = $s.Bounds.X + [int](($s.Bounds.Width - $w) / 2); $y = $s.Bounds.Y + [int](($s.Bounds.Height - $hh) / 2)
[void][DemoWin]::SetWindowPos($h, [IntPtr]::Zero, $x, $y, 0, 0, 0x0015)
"pid $($p.Id), window ${w}x$hh at $x,$y"
