# restore-registry.ps1 : points Drift's link-handler registry values back at the user's own Drift.exe.
# Any Drift.exe that starts rewrites them to its own path, so run this after every demo launch.
. "$PSScriptRoot\common.ps1"
$exe = $UserExe
foreach ($cls in 'DriftURL', 'DriftHTML') {
  if (-not (Test-Path "HKCU:\Software\Classes\$cls")) { continue }
  Set-ItemProperty "HKCU:\Software\Classes\$cls\Application" -Name ApplicationIcon -Value "$exe,0"
  Set-ItemProperty "HKCU:\Software\Classes\$cls\DefaultIcon" -Name '(default)' -Value "$exe,0"
  Set-ItemProperty "HKCU:\Software\Classes\$cls\shell\open\command" -Name '(default)' -Value "`"$exe`" `"%1`""
}
$client = 'HKCU:\Software\Clients\StartMenuInternet\Drift'
if (Test-Path $client) {
  Set-ItemProperty "$client\Capabilities" -Name ApplicationIcon -Value "$exe,0"
  Set-ItemProperty "$client\DefaultIcon" -Name '(default)' -Value "$exe,0"
  Set-ItemProperty "$client\shell\open\command" -Name '(default)' -Value "`"$exe`""
}
$bad = foreach ($k in 'HKCU:\Software\Classes\DriftURL', 'HKCU:\Software\Classes\DriftHTML', $client) {
  if (Test-Path $k) {
    Get-ChildItem $k -Recurse | ForEach-Object { $key = $_; $key.GetValueNames() | ForEach-Object { $key.GetValue($_) } } |
      Where-Object { "$_" -like '*Drift.exe*' -and "$_" -notlike "*$exe*" }
  }
}
"registry values not on the user's Drift.exe: $(@($bad).Count)"
