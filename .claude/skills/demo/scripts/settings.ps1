# settings.ps1 [-Page Tabs] [-Invoke 'Button name'] -Out file.png : in the demo's open Settings window,
# selects a page and/or invokes a button by its name (UI Automation, no mouse or keys), then captures the window.
param([string]$Page = '', [string]$Invoke = '', [string]$Out)
. "$PSScriptRoot\common.ps1"
$p = Get-DemoProcess; if (-not $p) { 'demo not running'; exit 1 }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$ae = [System.Windows.Automation.AutomationElement]
function Prop($id, $v) { New-Object System.Windows.Automation.PropertyCondition($id, $v) }
$win = $ae::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
  (New-Object System.Windows.Automation.AndCondition((Prop $ae::ProcessIdProperty $p.Id), (Prop $ae::NameProperty 'Drift Settings'))))
if (-not $win) { 'no settings window (open it with: node press.mjs comma ctrl)'; exit 1 }
if ($Page) {
  $tab = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.AndCondition((Prop $ae::NameProperty $Page),
      (Prop $ae::ControlTypeProperty ([System.Windows.Automation.ControlType]::RadioButton)))))
  if (-not $tab) { "no page $Page"; exit 1 }
  $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 700
}
if ($Invoke) {
  $b = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (Prop $ae::NameProperty $Invoke))
  if (-not $b) { "no $Invoke"; exit 1 }
  $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 1200
}
if ($Out) { & "$PSScriptRoot\capture.ps1" -ProcId $p.Id -Out $Out -Title 'Settings' }
