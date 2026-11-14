param([Parameter(Mandatory=$true)][string]$ClientDll,[Parameter(Mandatory=$true)][string]$BrokenInstaller)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$current=Join-Path $repoRoot 'dist/ArcaneThaiInstaller.exe'
$testRoot=Join-Path $repoRoot ('.local/menu-loader-tests/'+(Get-Date -Format 'yyyyMMdd-HHmmssfff'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$checks=[System.Collections.Generic.List[string]]::new()
$script:resultIndex=0
function Check([bool]$Condition,[string]$Description){if(-not $Condition){throw ('FAILED: '+$Description)};$checks.Add($Description)}
function Hash([string]$Path){(Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()}
function Run([string]$Exe,[string]$Operation,[string]$Path,[bool]$Success=$true){
    $script:resultIndex++
    $report=Join-Path $testRoot ('result-'+$script:resultIndex+'.json')
    & $Exe $Operation $Path $report | Out-Null
    $code=$LASTEXITCODE
    if($Success -and $code -ne 0){throw (Get-Content -Raw -LiteralPath $report)}
    if(-not $Success -and $code -eq 0){throw 'Expected failure'}
    Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
}
function New-Client([string]$Name){
    $root=Join-Path $testRoot $Name
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Copy-Item -LiteralPath $ClientDll -Destination (Join-Path $root 'ArcaneMPClient.dll')
    @{game_path=(Join-Path $testRoot 'not the running game');mod_dll_name='ArcaneMPClient.dll';custom='keep'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'ArcaneMP_launcher.json') -Encoding utf8
    $root
}
$root=New-Client 'fix direct module path'
$official=Join-Path $root 'ArcaneMPClient.dll'
$config=Join-Path $root 'ArcaneMP_launcher.json'
$officialHash=Hash $official
$old=Run $BrokenInstaller '--install' $root
$oldHash=Hash $old.LocalizedPath
Check ((Get-Content -Raw -LiteralPath $config | ConvertFrom-Json).mod_dll_name -eq 'mods\ArcaneThai\ArcaneMPClient.Thai.dll') 'The actual 0.6.0 release reproduces the direct subfolder DLL configuration'
$pending=Run $current '--inspect' $root
Check ($pending.NeedsRefresh -and -not $pending.IsInstalled) 'A direct subfolder load is detected as requiring repair even when the translation is current'
$fixed=Run $current '--install' $root
$inspect=Run $current '--inspect' $root
Check ($fixed.Changed -and $inspect.IsInstalled -and $inspect.LoaderLinked) 'Repair creates a root hard link to the menu payload in mods'
Check ((Get-Content -Raw -LiteralPath $config | ConvertFrom-Json).mod_dll_name -eq 'ArcaneMPClient.Thai.dll') 'Launcher is switched back to the original CEF module directory'
Check ((Hash $fixed.LocalizedPath) -eq $oldHash -and (Hash $fixed.LoaderPath) -eq $oldHash) 'The crash fix changes the load path without changing translated DLL bytes'
Check ((Hash $official) -eq $officialHash -and (Get-Content -Raw -LiteralPath $config | ConvertFrom-Json).custom -eq 'keep') 'Repair preserves the official DLL and unrelated launcher settings'
$again=Run $current '--install' $root
Check (-not $again.Changed -and (Run $current '--inspect' $root).LoaderLinked) 'Repeated repair is idempotent and retains the link identity'
Remove-Item -LiteralPath $fixed.LoaderPath
Check ((Run $current '--inspect' $root).NeedsRefresh) 'A missing root menu link is detected'
$null=Run $current '--install' $root
Check ((Run $current '--inspect' $root).LoaderLinked) 'Reinstall repairs a missing root menu link'
Remove-Item -LiteralPath $fixed.LoaderPath
Copy-Item -LiteralPath $fixed.LocalizedPath -Destination $fixed.LoaderPath
Check (-not (Run $current '--inspect' $root).IsInstalled) 'A detached identical copy is distinguished from the intended hard link'
$null=Run $current '--install' $root
Check ((Run $current '--inspect' $root).LoaderLinked) 'Repair reconnects an identical detached root DLL'

$stream=[IO.File]::Open($fixed.LoaderPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try{$null=Run $current '--install' $root $false;$null=Run $current '--restore' $root $false}finally{$stream.Dispose()}
Check ((Run $current '--inspect' $root).IsInstalled) 'A locked root DLL blocks install and restore without damaging the working link'
Remove-Item -LiteralPath $fixed.LoaderPath
[IO.File]::WriteAllText($fixed.LoaderPath,'a foreign DLL filename owned by the user')
$foreignHash=Hash $fixed.LoaderPath
$configHash=Hash $config
$null=Run $current '--install' $root $false
$null=Run $current '--restore' $root $false
Check ((Hash $fixed.LoaderPath) -eq $foreignHash -and (Hash $config) -eq $configHash) 'Install and restore preserve foreign loader contents and leave configuration intact'
Remove-Item -LiteralPath $fixed.LoaderPath
$null=Run $current '--install' $root
$removed=Run $current '--restore' $root
Check (-not (Test-Path -LiteralPath $fixed.LocalizedPath) -and -not (Test-Path -LiteralPath $fixed.LoaderPath)) 'Restore removes both owned menu paths'
$backup=Split-Path -Parent $removed.BackupPath
Check ((Hash (Join-Path $backup 'ArcaneMPClient.Thai.dll')) -eq $oldHash -and (Hash (Join-Path $backup 'mods/ArcaneThai/ArcaneMPClient.Thai.dll')) -eq $oldHash) 'Restore preserves separate verified backup paths for the payload and root alias'
Check ((Hash $official) -eq $officialHash -and (Get-Content -Raw -LiteralPath $config | ConvertFrom-Json).mod_dll_name -eq 'ArcaneMPClient.dll') 'Restore returns to the official DLL with the original checksum'

$unrepaired=New-Client 'remove broken layout directly'
$oldOnly=Run $BrokenInstaller '--install' $unrepaired
$null=Run $current '--restore' $unrepaired
Check (-not (Test-Path -LiteralPath $oldOnly.LocalizedPath) -and (Get-Content -Raw -LiteralPath (Join-Path $unrepaired 'ArcaneMP_launcher.json') | ConvertFrom-Json).mod_dll_name -eq 'ArcaneMPClient.dll') 'A broken 0.6.0 install can be removed without repairing it first'

$report=[pscustomobject]@{Passed=$checks.Count;Checks=$checks;TestRoot=$testRoot}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repoRoot 'dist/menu-loader-tests.json') -Encoding utf8
$report | ConvertTo-Json -Depth 5
$global:LASTEXITCODE=0
