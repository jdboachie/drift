# capture.ps1 -Out file.png [-Title t] [-ProcId n] : captures one demo window's own pixels (PrintWindow), so
# windows on top of it don't show. -Title picks a window by part of its title ('Settings'); default: the main one.
param([string]$Out, [string]$Title = '', [int]$ProcId = 0)
. "$PSScriptRoot\common.ps1"
if ($ProcId -eq 0) { $p = Get-DemoProcess; if (-not $p) { 'demo not running'; exit 1 }; $ProcId = $p.Id }
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class DemoCap {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public struct RECT { public int L, T, R, B; }
}
"@
[DemoCap]::SetProcessDPIAware() | Out-Null
$script:found = $null
[DemoCap]::EnumWindows({ param($h, $l)
  $p = 0; [DemoCap]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
  if ($p -eq $ProcId -and [DemoCap]::IsWindowVisible($h)) {
    $sb = New-Object Text.StringBuilder 256; [DemoCap]::GetWindowText($h, $sb, 256) | Out-Null
    if ($sb.ToString() -like "*$Title*" -and $sb.Length -gt 0) { $script:found = $h; return $false }
  }; return $true }, [IntPtr]::Zero) | Out-Null
if (-not $script:found) { 'no window'; exit 1 }
$r = New-Object DemoCap+RECT; [DemoCap]::GetWindowRect($script:found, [ref]$r) | Out-Null
$bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
[DemoCap]::PrintWindow($script:found, $dc, 2) | Out-Null   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($dc); $g.Dispose()
$bmp.Save($Out, [Drawing.Imaging.ImageFormat]::Png)
"$($bmp.Width)x$($bmp.Height) -> $Out"
