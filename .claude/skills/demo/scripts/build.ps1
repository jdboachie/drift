# build.ps1 [-NoRun] [-Seed] : builds the working tree into the demo's own folder (never the repo's bin,
# which the user's running Drift locks), relaunches the demo, then restores the link-handler registry.
param([switch]$NoRun, [switch]$Seed, [string]$Session = '')
. "$PSScriptRoot\common.ps1"
& "$PSScriptRoot\stop.ps1" | Out-Null
# OutputPath too, not just OutDir: the XAML compiler copies its .xbf files to OutputPath, and stale XAML
# next to the user's older Drift.dll breaks their Drift the next time it loads that XAML.
$out = dotnet build "$Repo\Drift\Drift.csproj" -c Debug -p:Platform=x64 "-p:OutDir=$App\\" "-p:OutputPath=$App\\" -v q -nologo 2>&1
$userBin = Split-Path $UserExe
$leaked = Get-ChildItem $userBin -Filter *.xbf -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt (Get-Item $UserExe).LastWriteTime.AddMinutes(1) }
if ($leaked) { "WARNING: this build wrote into the user's bin: $($leaked.Name -join ', ')" }
$errors = $out | Select-String -Pattern ': error ' | Select-Object -First 8
if ($LASTEXITCODE -ne 0) { 'BUILD FAILED'; if ($errors) { $errors } else { $out | Select-Object -Last 15 }; exit 1 }
'build succeeded'
if ($NoRun) { return }
& "$PSScriptRoot\run.ps1" -Seed:$Seed -Session $Session
Start-Sleep -Seconds 6   # Drift rewrites the registry a few seconds after it starts
& "$PSScriptRoot\restore-registry.ps1"
