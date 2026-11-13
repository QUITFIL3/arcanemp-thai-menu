param([Parameter(Mandatory=$true)][string]$ClientDll,[string]$LegacyClientDll='')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe = Join-Path $repoRoot 'dist/ArcaneThaiInstaller.exe'
$testRoot = Join-Path $repoRoot ('.local/installer-tests/' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$script:reportIndex = 0
$passed = [System.Collections.Generic.List[string]]::new()
$sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ClientDll).Hash

function Invoke-Installer([string]$Operation,[string]$Target,[bool]$ExpectSuccess=$true) {
    $script:reportIndex++
    $reportFile = Join-Path $testRoot ('result-' + $script:reportIndex + '.json')
    & $exe $Operation $Target $reportFile | Out-Null
    $resultCode = $LASTEXITCODE
    if ($ExpectSuccess -and $resultCode -ne 0) { throw (Get-Content -Raw -LiteralPath $reportFile) }
    if (-not $ExpectSuccess -and $resultCode -eq 0) { throw ('Expected rejection: ' + $Operation + ' ' + $Target) }
    return Get-Content -Raw -LiteralPath $reportFile | ConvertFrom-Json
}

function Assert-True([bool]$Value,[string]$Description) {
    if (-not $Value) { throw ('FAILED: ' + $Description) }
    $passed.Add($Description)
}

$originalHtmlFile = Join-Path $testRoot 'original-menu.html'
& $exe --extract $ClientDll $originalHtmlFile | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not read the supplied menu.' }
$htmlBytes = [IO.File]::ReadAllBytes($originalHtmlFile)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'

function Set-UpdaterManifest([string]$Target) {
    $directory = Split-Path -Parent $Target
    New-Item -ItemType Directory -Force -Path (Join-Path $directory '.mafiahub') | Out-Null
    @{version='fixture'; files=@(@{path='ArcaneMPClient.dll';sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $Target).Hash.ToLowerInvariant()})} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory '.mafiahub/build-manifest.json') -Encoding utf8
}

function Set-FixtureConfig([string]$Directory) {
    @{mod_dll_name='ArcaneMPClient.dll';game_path='D:/เกม ทดสอบ';game_path_manual=$true;custom=@{keep=42}} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Directory 'ArcaneMP_launcher.json') -Encoding utf8
}

function New-Fixture([string]$Name,[string]$Version,[int]$Padding,[byte[]]$Html=$htmlBytes) {
    $directory = Join-Path $testRoot $Name
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $source = Join-Path $directory 'Fixture.cs'
    $baseFile = Join-Path $directory 'base.dll'
    $target = Join-Path $directory 'ArcaneMPClient.dll'
    [IO.File]::WriteAllText($source, ('using System.Reflection; [assembly:AssemblyFileVersion("' + $Version + '")] public static class Fixture {}'))
    & $compiler /nologo /target:library ('/out:' + $baseFile) $source
    if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
    $baseBytes = [IO.File]::ReadAllBytes($baseFile)
    $combined = [byte[]]::new($baseBytes.Length + $Padding + $Html.Length + 1)
    [Array]::Copy($baseBytes, 0, $combined, 0, $baseBytes.Length)
    [Array]::Copy($Html, 0, $combined, $baseBytes.Length + $Padding, $Html.Length)
    [IO.File]::WriteAllBytes($target, $combined)
    Set-UpdaterManifest $target
    Set-FixtureConfig $directory
    return $target
}

# Synthetic PE libraries carry different real file-version resources and different menu offsets.
$fixtures = @(
    (New-Fixture 'สำเนาทดสอบ เวอร์ชันเก้า' '9.8.7.6' 137),
    (New-Fixture 'สำเนาทดสอบ เวอร์ชันเจ็ด' '7.6.5.4' 829)
)
$offsets = @()
foreach ($fixture in $fixtures) {
    $before = [IO.File]::ReadAllBytes($fixture)
    $beforeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $fixture).Hash
    $beforeVersion = (Get-Item -LiteralPath $fixture).VersionInfo.FileVersion
    $inspect = Invoke-Installer '--inspect' (Split-Path -Parent $fixture)
    $offsets += $inspect.MenuOffset
    Assert-True (-not $inspect.IsInstalled) ('Detect unpatched menu at ' + $inspect.MenuOffset)
    $installed = Invoke-Installer '--install' $fixture
    $after = [IO.File]::ReadAllBytes($installed.LocalizedPath)
    Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $fixture).Hash -eq $beforeHash) 'Updater-managed official DLL remains byte-for-byte unchanged'
    Assert-True $installed.OfficialFileUnchanged 'Installer reports untouched official DLL'
    Assert-True ($installed.Changed -and $installed.OutsideMenuUnchanged) ('Install into version ' + $beforeVersion)
    Assert-True ($before.Length -eq $after.Length) 'DLL size is preserved'
    Assert-True ($beforeVersion -eq (Get-Item -LiteralPath $installed.LocalizedPath).VersionInfo.FileVersion) ('File version preserved: ' + $beforeVersion)
    for ($index=0; $index -lt $before.Length; $index++) {
        if ($index -ge $inspect.MenuOffset -and $index -lt ($inspect.MenuOffset + $inspect.MenuLength)) { continue }
        if ($before[$index] -ne $after[$index]) { throw ('Outside-menu byte changed at ' + $index) }
    }
    Assert-True $true 'Every byte outside the menu is preserved'
    Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $installed.BackupPath).Hash -eq $beforeHash) 'Original backup is byte-for-byte correct'
    $directory = Split-Path -Parent $fixture
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $directory '.mafiahub/build-manifest.json') | ConvertFrom-Json
    Assert-True ($manifest.files[0].sha256 -eq $beforeHash.ToLowerInvariant()) 'Official DLL still matches the updater checksum after installation'
    $configPath = Join-Path $directory 'ArcaneMP_launcher.json'
    $config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
    Assert-True ($config.mod_dll_name -eq 'ArcaneMPClient.Thai.dll' -and $config.custom.keep -eq 42 -and $config.game_path_manual) 'Only the intended launcher setting changes; custom fields survive'
    $second = Invoke-Installer '--install' $fixture
    Assert-True (-not $second.Changed) 'Repeated install is idempotent'
    $inspectInstalled = Invoke-Installer '--inspect' $fixture
    Assert-True ($inspectInstalled.IsInstalled -and $inspectInstalled.CanRestore) 'Installed menu and restore availability are detected'
    if (-not (Test-Path -LiteralPath (Join-Path $testRoot 'patched-menu.html'))) {
        # Extract the localized copy under the expected fixture filename for the diagnostic CLI.
        $extractDirectory = Join-Path $testRoot 'extract-copy'
        New-Item -ItemType Directory -Force -Path $extractDirectory | Out-Null
        Copy-Item -LiteralPath $installed.LocalizedPath -Destination (Join-Path $extractDirectory 'ArcaneMPClient.dll')
        & $exe --extract $extractDirectory (Join-Path $testRoot 'patched-menu.html') | Out-Null
        Copy-Item -LiteralPath $installed.LocalizedPath -Destination (Join-Path $testRoot 'patched-for-browser.dll')
    }
    $config.game_path = 'E:/changed after install'
    $config | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configPath -Encoding utf8
    $restored = Invoke-Installer '--restore' $fixture
    Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $fixture).Hash -eq $beforeHash) 'Restore returns the exact original DLL'
    Assert-True (Test-Path -LiteralPath $restored.BackupPath) 'Restore retains the previous patched DLL'
    $restoredConfig = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json
    Assert-True ($restoredConfig.mod_dll_name -eq 'ArcaneMPClient.dll' -and $restoredConfig.game_path -eq 'E:/changed after install') 'Restore preserves launcher changes made after installation'
    Assert-True (-not (Test-Path -LiteralPath $installed.LocalizedPath)) 'Restore removes the separate localized copy after backing it up'
}
Assert-True ($offsets[0] -ne $offsets[1]) 'Menu detection works at different offsets without a version/hash list'

$smallHtml = [Text.Encoding]::UTF8.GetBytes('<!DOCTYPE html><html><head><title>ArcaneMP</title></head><body><script>window.arcanemp={};/*menu:ready*/</script></body></html>')
$small = New-Fixture 'พื้นที่ไม่พอ' '2.3.4.5' 47 $smallHtml
$smallBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath $small).Hash
$null = Invoke-Installer '--install' $small $false
Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $small).Hash -eq $smallBefore) 'Insufficient menu capacity is rejected without changing the file'

$locked = $fixtures[0]
$lockStream = [IO.File]::Open($locked,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try { $null = Invoke-Installer '--install' $locked $false }
finally { $lockStream.Dispose() }
Assert-True $true 'A locked client is rejected'

$null = Invoke-Installer '--install' $fixtures[0]
$changedOfficial = [IO.File]::ReadAllBytes($fixtures[1])
[IO.File]::WriteAllBytes($fixtures[0], $changedOfficial)
Set-UpdaterManifest $fixtures[0]
Assert-True ((Invoke-Installer '--inspect' $fixtures[0]).NeedsRefresh) 'A real client update is detected as needing a translation refresh'
$refresh = Invoke-Installer '--install' $fixtures[0]
Assert-True ((Invoke-Installer '--inspect' $fixtures[0]).IsInstalled -and $refresh.FileVersionAfter -eq '7.6.5.4') 'Refreshing uses the newly updated client version'
$null = Invoke-Installer '--restore' $fixtures[0]
Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $fixtures[0]).Hash -eq (Get-FileHash -Algorithm SHA256 -LiteralPath $fixtures[1]).Hash) 'Restore after an official update keeps the new official client'

$configLock = Join-Path (Split-Path -Parent $fixtures[0]) 'ArcaneMP_launcher.json'
$configStream = [IO.File]::Open($configLock,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try { $null = Invoke-Installer '--install' $fixtures[0] $false }
finally { $configStream.Dispose() }
Assert-True (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $fixtures[0]) 'ArcaneMPClient.Thai.dll'))) 'A locked launcher config leaves no partial installation'

$collision = Join-Path (Split-Path -Parent $fixtures[0]) 'ArcaneMPClient.Thai.dll'
[IO.File]::WriteAllText($collision, 'unrelated user file')
$collisionHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $collision).Hash
$null = Invoke-Installer '--install' $fixtures[0] $false
Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $collision).Hash -eq $collisionHash) 'An untracked file with the localized DLL name is never overwritten'
Remove-Item -LiteralPath $collision

$unverified = New-Fixture 'checksum ไม่ตรง' '8.8.8.8' 109
$unverifiedBytes = [IO.File]::ReadAllBytes($unverified)
$unverifiedBytes[200] = $unverifiedBytes[200] -bxor 1
[IO.File]::WriteAllBytes($unverified, $unverifiedBytes)
$null = Invoke-Installer '--install' $unverified $false
Assert-True (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $unverified) 'ArcaneMPClient.Thai.dll'))) 'A changed official file that is not our legacy patch is rejected'

$duplicate = New-Fixture 'หลายหน้าเมนู' '3.4.5.6' 71 ([byte[]]($htmlBytes + [byte]0 + $htmlBytes))
$duplicateBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath $duplicate).Hash
$null = Invoke-Installer '--install' $duplicate $false
Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $duplicate).Hash -eq $duplicateBefore) 'Ambiguous embedded menus are rejected without changing the file'

$legacyDirectory = Join-Path $testRoot 'สำเนา client ที่เลือกจริง'
New-Item -ItemType Directory -Force -Path $legacyDirectory | Out-Null
$legacyCopy = Join-Path $legacyDirectory 'ArcaneMPClient.dll'
if (-not $LegacyClientDll) { $LegacyClientDll = $ClientDll }
$legacySourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $LegacyClientDll).Hash
Copy-Item -LiteralPath $LegacyClientDll -Destination $legacyCopy
Set-FixtureConfig $legacyDirectory
$officialDirectory = Join-Path $testRoot 'official-manifest-source'
New-Item -ItemType Directory -Force -Path $officialDirectory | Out-Null
Copy-Item -LiteralPath $ClientDll -Destination (Join-Path $officialDirectory 'ArcaneMPClient.dll')
Set-UpdaterManifest (Join-Path $officialDirectory 'ArcaneMPClient.dll')
New-Item -ItemType Directory -Force -Path (Join-Path $legacyDirectory '.mafiahub') | Out-Null
Copy-Item -LiteralPath (Join-Path $officialDirectory '.mafiahub/build-manifest.json') -Destination (Join-Path $legacyDirectory '.mafiahub/build-manifest.json')
$recovery = Join-Path $legacyDirectory 'thai-menu-backups/official'
New-Item -ItemType Directory -Force -Path $recovery | Out-Null
Copy-Item -LiteralPath $ClientDll -Destination (Join-Path $recovery 'ArcaneMPClient.dll')
$legacyInstalled = Invoke-Installer '--install' $legacyCopy
Assert-True ((Invoke-Installer '--inspect' $legacyCopy).IsInstalled) 'The selected client structure is supported, including the older compressed patch'
Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $legacyCopy).Hash -eq $sourceHash) 'Migrating an old patch restores the official checksum before using a separate translated copy'
$null = Invoke-Installer '--restore' $legacyCopy
Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $legacyCopy).Hash -eq $sourceHash) 'Removing the new translation keeps the recovered official file'
Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $ClientDll).Hash -eq $sourceHash) 'The real installed client was never modified by these tests'
Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $LegacyClientDll).Hash -eq $legacySourceHash) 'The older installed patch was never modified by these tests'

$report = [pscustomobject]@{Passed=$passed.Count;Checks=$passed;TestRoot=$testRoot;MenuForBrowser=(Join-Path $testRoot 'patched-menu.html');PatchedDllForBrowser=(Join-Path $testRoot 'patched-for-browser.dll')}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repoRoot 'dist/installer-tests.json') -Encoding utf8
$report | ConvertTo-Json -Depth 5
