# Shared by the demo scripts: where the demo lives, and how to find its process safely.
$Repo = (Resolve-Path "$PSScriptRoot\..\..\..\..").Path
$Base = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DriftDemo'
$App = "$Base\app"            # the demo's build output (never the repo's bin)
$DemoHome = "$Base\home"      # the demo's own USERPROFILE / LOCALAPPDATA
$PidFile = "$Base\demo.pid"
$UserExe = "$Repo\Drift\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\Drift.exe"
$DebugPort = 9444

# The demo's Drift process, or nothing. Checks name and path: process IDs get reused.
function Get-DemoProcess {
  if (-not (Test-Path $PidFile)) { return }
  Get-Process -Id ([int](Get-Content $PidFile)) -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -eq 'Drift' -and $_.Path -like "$App\*" }
}
