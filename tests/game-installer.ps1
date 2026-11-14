param([Parameter(Mandatory=$true)][string]$ClientDll)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script:exe = Join-Path $repoRoot 'dist/ArcaneThaiInstaller.exe'
$testRoot = Join-Path $repoRoot ('.local/game-tests/' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$script:resultNumber = 0
$checks = [System.Collections.Generic.List[string]]::new()
$expected = '164f13205cd5d9968856a3ce7d3dd7b749c55779705a2610a76a9fecff37a565'
$realHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ClientDll).Hash

function Assert-True([bool]$Condition,[string]$Description) {
    if (-not $Condition) { throw ('FAILED: ' + $Description) }
    $checks.Add($Description)
}
function Invoke-Game([string]$Action,[string]$Path,[string]$Mode,[bool]$Success=$true) {
    $script:resultNumber++
    $report = Join-Path $testRoot ('result-' + $script:resultNumber + '.json')
    & $script:exe $Action $Path $report $Mode | Out-Null
    $code = $LASTEXITCODE
    if ($Success -and $code -ne 0) { throw (Get-Content -Raw -LiteralPath $report) }
    if (-not $Success -and $code -eq 0) { throw ('Expected rejection: ' + $Action) }
    Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
}
function Hash([string]$Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }

$client = Join-Path $testRoot 'ArcaneMP ทดสอบ'
New-Item -ItemType Directory -Force -Path (Join-Path $client '.mafiahub') | Out-Null
Copy-Item -LiteralPath $ClientDll -Destination (Join-Path $client 'ArcaneMPClient.dll')
$config = Join-Path $client 'ArcaneMP_launcher.json'
@{game_path=(Join-Path $testRoot 'fake game');mod_dll_name='ArcaneMPClient.Thai.dll';custom='keep'} | ConvertTo-Json | Set-Content -LiteralPath $config -Encoding utf8
$manifest = Join-Path $client '.mafiahub/build-manifest.json'
@{version='1.13.0';files=@(@{path='ArcaneMPClient.dll';sha256=$realHash})} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifest -Encoding utf8
$configHash = Hash $config
$manifestHash = Hash $manifest
$before = Invoke-Game '--game-inspect' $client 'arcanemp'
Assert-True (-not $before.IsInstalled -and -not $before.HasConflict) 'Detect a client without a game translation'
$installed = Invoke-Game '--game-install' $client 'arcanemp'
Assert-True ($installed.Changed -and (Hash $installed.TargetPath) -eq $expected) 'Install the embedded PAK into the selected client'
Assert-True ($installed.TargetPath -eq (Join-Path $client 'mods/ArcaneThai/zArcaneThai_Community_P.pak')) 'ArcaneMP mode stores the pack in the mods folder'
Assert-True ($installed.LoaderPath -eq (Join-Path $client 'files/paks/zArcaneThai_Community_P.pak') -and (Invoke-Game '--game-inspect' $client 'arcanemp').LoaderLinked) 'The native scan path is a hard link to the same file in mods'
Assert-True ((Hash (Join-Path $client 'ArcaneMPClient.dll')) -eq $realHash.ToLowerInvariant()) 'Game translation leaves the official client DLL unchanged'
Assert-True ((Hash $config) -eq $configHash -and (Hash $manifest) -eq $manifestHash) 'Game translation leaves launcher settings and updater manifest unchanged'
$again = Invoke-Game '--game-install' $client 'arcanemp'
Assert-True (-not $again.Changed) 'Repeated game-pack installation is idempotent'
Assert-True ((Invoke-Game '--game-inspect' $client 'arcanemp').IsInstalled) 'Installed game pack is detected by its checksum'
$otherPak = Join-Path $client 'files/paks/OtherMod.pak'
[IO.File]::WriteAllText($otherPak, 'another mod')
$removed = Invoke-Game '--game-uninstall' $client 'arcanemp'
Assert-True (-not (Test-Path -LiteralPath $installed.TargetPath) -and (Hash $removed.BackupPath) -eq $expected) 'Uninstall moves the exact owned pack into a backup'
Assert-True (-not (Test-Path -LiteralPath $installed.LoaderPath)) 'Uninstall also removes the owned loader hard link'
Assert-True ([IO.File]::ReadAllText($otherPak) -eq 'another mod') 'Uninstall preserves unrelated mods'
Assert-True (-not (Invoke-Game '--game-uninstall' $client 'arcanemp').Changed) 'Repeated uninstall is harmless'

[IO.File]::WriteAllText($installed.TargetPath, 'foreign file with same name')
$foreignHash = Hash $installed.TargetPath
Assert-True ((Invoke-Game '--game-inspect' $client 'arcanemp').HasConflict) 'A different PAK with the same name is reported as a conflict'
$null = Invoke-Game '--game-install' $client 'arcanemp' $false
$null = Invoke-Game '--game-uninstall' $client 'arcanemp' $false
Assert-True ((Hash $installed.TargetPath) -eq $foreignHash) 'Install and uninstall never overwrite or remove a different PAK'
Remove-Item -LiteralPath $installed.TargetPath

[IO.File]::WriteAllText($installed.LoaderPath, 'another mod owns the loader filename')
$null = Invoke-Game '--game-install' $client 'arcanemp' $false
Assert-True (-not (Test-Path -LiteralPath $installed.TargetPath) -and [IO.File]::ReadAllText($installed.LoaderPath) -eq 'another mod owns the loader filename') 'A foreign file at the loader path is preserved'
Remove-Item -LiteralPath $installed.LoaderPath

# Compile a harmless executable with the real game filename to test the process guard.
$helperSource = Join-Path $testRoot 'GameFixture.cs'
[IO.File]::WriteAllText($helperSource, 'public static class GameFixture { [System.STAThread] public static void Main() { System.Threading.Thread.Sleep(60000); } }')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$helper = Join-Path $testRoot 'HogwartsLegacy.exe'
& $compiler /nologo /target:winexe ('/out:' + $helper) $helperSource
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed' }
$gameRoots = @((Join-Path $testRoot 'SteamLibrary/steamapps/common/Hogwarts Legacy'),(Join-Path $testRoot 'Epic Games/Hogwarts Legacy'))
foreach ($gameRoot in $gameRoots) {
    $exeDirectory = Join-Path $gameRoot 'Phoenix/Binaries/Win64'
    $pakDirectory = Join-Path $gameRoot 'Phoenix/Content/Paks'
    New-Item -ItemType Directory -Force -Path $exeDirectory,$pakDirectory | Out-Null
    $gameExe = Join-Path $exeDirectory 'HogwartsLegacy.exe'
    Copy-Item -LiteralPath $helper -Destination $gameExe
    $basePak = Join-Path $pakDirectory 'pakchunk0-WindowsNoEditor.pak'
    [IO.File]::WriteAllText($basePak, 'game base content')
    $gameExeHash = Hash $gameExe
    $baseHash = Hash $basePak
    $gameInspect = Invoke-Game '--game-inspect' $gameExe 'game'
    Assert-True ($gameInspect.RootPath -eq $gameRoot) 'Game root resolves from Phoenix/Binaries/Win64/HogwartsLegacy.exe'
    $gameInstall = Invoke-Game '--game-install' $gameRoot 'game'
    Assert-True ($gameInstall.TargetPath -eq (Join-Path $pakDirectory '~mods/zArcaneThai_Community_P.pak') -and (Hash $gameInstall.TargetPath) -eq $expected) 'Direct game mode installs under Paks/~mods for both store layouts'
    Assert-True ((Hash $gameExe) -eq $gameExeHash -and (Hash $basePak) -eq $baseHash) 'Direct install leaves the game executable and original PAK unchanged'
    $stream = [IO.File]::Open($gameInstall.TargetPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try { $null = Invoke-Game '--game-uninstall' $gameRoot 'game' $false }
    finally { $stream.Dispose() }
    Assert-True ((Hash $gameInstall.TargetPath) -eq $expected) 'A locked game pack remains intact'
    $null = Invoke-Game '--game-uninstall' $gameRoot 'game'
}

$ownedProcess = Start-Process -FilePath (Join-Path $gameRoots[1] 'Phoenix/Binaries/Win64/HogwartsLegacy.exe') -WindowStyle Hidden -PassThru
try {
    $null = Invoke-Game '--game-install' $gameRoots[1] 'game' $false
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $gameRoots[1] 'Phoenix/Content/Paks/~mods/zArcaneThai_Community_P.pak'))) 'A running game is rejected without installing a pack'
} finally { if (-not $ownedProcess.HasExited) { $ownedProcess.Kill(); $ownedProcess.WaitForExit() }; $ownedProcess.Dispose() }

$empty = Join-Path $testRoot 'wrong folder'
New-Item -ItemType Directory -Force -Path $empty | Out-Null
$null = Invoke-Game '--game-install' $empty 'game' $false
$null = Invoke-Game '--game-install' $empty 'arcanemp' $false
Assert-True (@(Get-ChildItem -LiteralPath $empty -Force).Count -eq 0) 'An invalid target is rejected without creating game folders'

$isolated = Join-Path $testRoot 'single exe only'
New-Item -ItemType Directory -Force -Path $isolated | Out-Null
Copy-Item -LiteralPath $script:exe -Destination (Join-Path $isolated 'ArcaneThaiInstaller.exe')
$originalExe = $script:exe
$script:exe = Join-Path $isolated 'ArcaneThaiInstaller.exe'
$isolatedInstall = Invoke-Game '--game-install' $client 'arcanemp'
$script:exe = $originalExe
Assert-True ((Hash $isolatedInstall.TargetPath) -eq $expected) 'A single copied EXE installs without payload sidecars or a network download'
$null = Invoke-Game '--game-uninstall' $client 'arcanemp'
Assert-True ((Hash $ClientDll) -eq $realHash.ToLowerInvariant()) 'Tests never modify the real installed client'

$report = [pscustomobject]@{Passed=$checks.Count;Checks=$checks;TestRoot=$testRoot;PayloadSHA256=$expected}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repoRoot 'dist/game-installer-tests.json') -Encoding utf8
$report | ConvertTo-Json -Depth 5
