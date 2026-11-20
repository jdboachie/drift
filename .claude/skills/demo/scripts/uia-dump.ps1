# uia-dump.ps1 -Out file.txt [-Title t] : lists every XAML element in a demo window (not the web pages inside it):
# depth, control type, name, automation id, and its rectangle relative to the window. Diff two dumps, taken
# before and after a change, to find what moved, appeared or vanished.
param([string]$Out, [string]$Title = '')
. "$PSScriptRoot\common.ps1"
$p = Get-DemoProcess; if (-not $p) { 'demo not running'; exit 1 }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$ae = [System.Windows.Automation.AutomationElement]
$windows = $ae::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,
  (New-Object System.Windows.Automation.PropertyCondition($ae::ProcessIdProperty, $p.Id)))
$win = $windows | Where-Object { $_.Current.Name -like "*$Title*" -and $_.Current.Name -notlike 'Drift Settings*' -or ($Title -and $_.Current.Name -like "*$Title*") } | Select-Object -First 1
if (-not $win) { 'no window'; exit 1 }
$origin = $win.Current.BoundingRectangle
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$lines = New-Object System.Collections.Generic.List[string]
function Walk($el, $depth) {
  $c = $el.Current
  if ($c.FrameworkId -eq 'Chrome') { return }   # web content (the pages): skipped
  $r = $c.BoundingRectangle
  $rect = if ($r.IsEmpty) { 'offscreen' } else { '{0},{1} {2}x{3}' -f [int]($r.X - $origin.X), [int]($r.Y - $origin.Y), [int]$r.Width, [int]$r.Height }
  $lines.Add(('{0}{1} "{2}" #{3} {4}{5}' -f ('  ' * $depth), $c.ControlType.ProgrammaticName.Replace('ControlType.', ''), $c.Name, $c.AutomationId, $rect, $(if ($c.IsOffscreen) { ' [hidden]' } else { '' })))
  $child = $walker.GetFirstChild($el)
  while ($child) { Walk $child ($depth + 1); $child = $walker.GetNextSibling($child) }
}
Walk $win 0
$lines | Set-Content -Encoding utf8 $Out
"$($lines.Count) elements -> $Out"
