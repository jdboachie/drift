# stop.ps1 : stops the demo Drift, only if the saved process really is the demo's Drift.exe.
. "$PSScriptRoot\common.ps1"
$p = Get-DemoProcess
if ($p) { $p | Stop-Process -Force; Start-Sleep -Milliseconds 800; "stopped $($p.Id)" } else { 'not running' }
