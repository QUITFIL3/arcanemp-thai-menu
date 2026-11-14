param([Parameter(Mandatory=$true)][string]$ClientDll,[Parameter(Mandatory=$true)][string]$PreviousInstaller)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$current=Join-Path $repoRoot 'dist/ArcaneThaiInstaller.exe'
$testRoot=Join-Path $repoRoot ('.local/mod-migration/'+(Get-Date -Format 'yyyyMMdd-HHmmssfff'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$script:index=0
$checks=[System.Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Description){if(-not $Condition){throw ('FAILED: '+$Description)};$checks.Add($Description)}
function Run([string]$Exe,[string]$Operation,[string]$Path,[bool]$Success=$true){
    $script:index++
    $report=Join-Path $testRoot ('result-'+$script:index+'.json')
    & $Exe $Operation $Path $report arcanemp | Out-Null
    $code=$LASTEXITCODE
    if($Success -and $code -ne 0){throw (Get-Content -Raw -LiteralPath $report)}
    if(-not $Success -and $code -eq 0){throw 'Expected operation to be rejected'}
    Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function New-Client([string]$Name){
    $root=Join-Path $testRoot $Name
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Copy-Item -LiteralPath $ClientDll -Destination (Join-Path $root 'ArcaneMPClient.dll')
    @{game_path=(Join-Path $testRoot 'not the running game');mod_dll_name='ArcaneMPClient.dll';custom='keep me'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'ArcaneMP_launcher.json') -Encoding utf8
    $root
}

$root=New-Client 'legacy 0.4 installation'
$originalHash=Hash (Join-Path $root 'ArcaneMPClient.dll')
$oldMenu=Run $PreviousInstaller '--install' $root
$oldGame=Run $PreviousInstaller '--game-install' $root
Check ((Test-Path -LiteralPath (Join-Path $root 'ArcaneMPClient.Thai.dll')) -and (Test-Path -LiteralPath (Join-Path $root 'files/paks/zArcaneThai_Community_P.pak'))) 'Previous release creates the actual legacy layout'
Check ((Run $current '--inspect' $root).NeedsRefresh -and (Run $current '--game-inspect' $root).NeedsMigration) 'Legacy menu and game pack are detected as needing migration'
$menu=Run $current '--install' $root
$game=Run $current '--game-install' $root
Check ($menu.LocalizedPath -eq (Join-Path $root 'mods/ArcaneThai/ArcaneMPClient.Thai.dll') -and (Run $current '--inspect' $root).LoaderLinked) 'Menu migration stores its payload in mods and links it at the original module path'
$config=Get-Content -Raw -LiteralPath (Join-Path $root 'ArcaneMP_launcher.json') | ConvertFrom-Json
Check ($config.mod_dll_name -eq 'ArcaneMPClient.Thai.dll' -and $config.custom -eq 'keep me') 'Launcher uses the root module path and preserves other settings'
Check ((Hash (Join-Path $root 'ArcaneMPClient.dll')) -eq $originalHash) 'Migration preserves every byte of the official DLL'
Check ((Run $current '--inspect' $root).IsInstalled -and (Run $current '--game-inspect' $root).LoaderLinked) 'Both translated components are installed from the mods folder'
Check ($game.BackupPath -and (Hash $game.BackupPath) -eq $game.Sha256) 'The legacy PAK is retained as a verified backup'

# Exercise Windows' DLL search with a harmless probe, never the real game DLL.
$probeSource=Join-Path $testRoot 'Probe.cs'
[IO.File]::WriteAllText($probeSource,'public static class Probe {}')
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$probeDll=Join-Path $root 'mods/ArcaneThai/Probe.dll'
& $compiler /nologo /target:library ('/out:'+$probeDll) $probeSource
if($LASTEXITCODE -ne 0){throw 'Probe DLL compilation failed'}
$probeAlias=Join-Path $root 'Probe.dll'
New-Item -ItemType HardLink -Path $probeAlias -Target $probeDll | Out-Null
$helper=Join-Path $root 'cef_subprocess.exe'
[IO.File]::WriteAllText($helper,'a harmless CEF helper marker')
$loaderSource=Join-Path $testRoot 'NativeLoaderProbe.cs'
$loaderText=@'
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
public static class NativeLoaderProbe {
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetDefaultDllDirectories(uint flags);
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr AddDllDirectory(string path);
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryW(string path);
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern uint GetModuleFileNameW(IntPtr module,StringBuilder path,int size);
 [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
 public static int Main(string[] args) {
  if(!SetDefaultDllDirectories(0x1000) || AddDllDirectory(args[0])==IntPtr.Zero) return 2;
  Directory.SetCurrentDirectory(args[1]);
  IntPtr module=LoadLibraryW("Probe.dll");
  if(module==IntPtr.Zero){Console.WriteLine("Win32 error "+Marshal.GetLastWin32Error());return 3;}
  var path=new StringBuilder(32768);GetModuleFileNameW(module,path,path.Capacity);
  Console.WriteLine(path.ToString());FreeLibrary(module);
  return File.Exists(Path.Combine(Path.GetDirectoryName(path.ToString()),"cef_subprocess.exe")) ? 0 : 4;
 }
}
'@
[IO.File]::WriteAllText($loaderSource,$loaderText)
$loader=Join-Path $testRoot 'NativeLoaderProbe.exe'
& $compiler /nologo /target:exe ('/out:'+$loader) $loaderSource
if($LASTEXITCODE -ne 0){throw 'Native loader probe compilation failed'}
$otherWorkDir=Join-Path $testRoot 'different game working directory'
New-Item -ItemType Directory -Force -Path $otherWorkDir | Out-Null
$loadedPath=& $loader $root $otherWorkDir
Check ($LASTEXITCODE -eq 0 -and $loadedPath -eq $probeAlias) 'Windows reports the original module directory when loading a root hard link so CEF can find its helper'
Remove-Item -LiteralPath $game.LoaderPath
Check ((Run $current '--game-inspect' $root).NeedsMigration) 'A missing native loader link is detected'
$repaired=Run $current '--game-install' $root
Check ($repaired.Changed -and (Run $current '--game-inspect' $root).LoaderLinked) 'Reinstall repairs the link without duplicating the active payload'
$null=Run $current '--restore' $root
Check ((Run $current '--game-inspect' $root).IsInstalled) 'Removing the menu keeps the game translation installed'
Check ((Get-Content -Raw -LiteralPath (Join-Path $root 'ArcaneMP_launcher.json') | ConvertFrom-Json).mod_dll_name -eq 'ArcaneMPClient.dll') 'Menu restore returns to the official launcher DLL'
$null=Run $current '--game-uninstall' $root
Check (-not (Test-Path -LiteralPath $game.TargetPath) -and -not (Test-Path -LiteralPath $game.LoaderPath)) 'Game uninstall removes both the owned mods PAK and its loader link'

$legacyOnly=New-Client 'legacy game only'
$legacyPack=Run $PreviousInstaller '--game-install' $legacyOnly
Check ((Run $current '--game-inspect' $legacyOnly).CanUninstall) 'A legacy-only PAK can be uninstalled without migrating first'
$legacyRemoved=Run $current '--game-uninstall' $legacyOnly
Check (-not (Test-Path -LiteralPath $legacyPack.TargetPath) -and (Hash $legacyRemoved.BackupPath) -eq $legacyPack.Sha256) 'Uninstall preserves a verified backup of the legacy-only PAK'

$blocked=New-Client 'link target cannot be created'
[IO.File]::WriteAllText((Join-Path $blocked 'files'),'preserve this file')
$null=Run $current '--game-install' $blocked $false
Check (-not (Test-Path -LiteralPath (Join-Path $blocked 'mods/ArcaneThai/zArcaneThai_Community_P.pak')) -and [IO.File]::ReadAllText((Join-Path $blocked 'files')) -eq 'preserve this file') 'An unavailable loader folder rolls back the newly created PAK and preserves the blocker'

$report=[pscustomobject]@{Passed=$checks.Count;Checks=$checks;TestRoot=$testRoot}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repoRoot 'dist/mod-migration-tests.json') -Encoding utf8
$report | ConvertTo-Json -Depth 5
$global:LASTEXITCODE=0
