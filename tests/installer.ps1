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
    $after = [IO.File]::ReadAllBytes($fixture)
    Assert-True ($installed.Changed -and $installed.OutsideMenuUnchanged) ('Install into version ' + $beforeVersion)
    Assert-True ($before.Length -eq $after.Length) 'DLL size is preserved'
    Assert-True ($beforeVersion -eq (Get-Item -LiteralPath $fixture).VersionInfo.FileVersion) ('File version preserved: ' + $beforeVersion)
    for ($index=0; $index -lt $before.Length; $index++) {
        if ($index -ge $inspect.MenuOffset -and $index -lt ($inspect.MenuOffset + $inspect.MenuLength)) { continue }
        if ($before[$index] -ne $after[$index]) { throw ('Outside-menu byte changed at ' + $index) }
    }
    Assert-True $true 'Every byte outside the menu is preserved'
    Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $installed.BackupPath).Hash -eq $beforeHash) 'Original backup is byte-for-byte correct'
    $second = Invoke-Installer '--install' $fixture
    Assert-True (-not $second.Changed) 'Repeated install is idempotent'
    $inspectInstalled = Invoke-Installer '--inspect' $fixture
    Assert-True ($inspectInstalled.IsInstalled -and $inspectInstalled.CanRestore) 'Installed menu and restore availability are detected'
    if (-not (Test-Path -LiteralPath (Join-Path $testRoot 'patched-menu.html'))) {
        & $exe --extract $fixture (Join-Path $testRoot 'patched-menu.html') | Out-Null
        Copy-Item -LiteralPath $fixture -Destination (Join-Path $testRoot 'patched-for-browser.dll')
    }
    $restored = Invoke-Installer '--restore' $fixture
    Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $fixture).Hash -eq $beforeHash) 'Restore returns the exact original DLL'
    Assert-True (Test-Path -LiteralPath $restored.BackupPath) 'Restore retains the previous patched DLL'
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
$legacyInstalled = Invoke-Installer '--install' $legacyCopy
Assert-True ((Invoke-Installer '--inspect' $legacyCopy).IsInstalled) 'The selected client structure is supported, including the older compressed patch'
$null = Invoke-Installer '--restore' $legacyCopy
Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $legacyCopy).Hash -eq $legacySourceHash) 'The selected client copy restores exactly'
Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $ClientDll).Hash -eq $sourceHash) 'The real installed client was never modified by these tests'
Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $LegacyClientDll).Hash -eq $legacySourceHash) 'The older installed patch was never modified by these tests'

$report = [pscustomobject]@{Passed=$passed.Count;Checks=$passed;TestRoot=$testRoot;MenuForBrowser=(Join-Path $testRoot 'patched-menu.html');PatchedDllForBrowser=(Join-Path $testRoot 'patched-for-browser.dll')}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repoRoot 'dist/installer-tests.json') -Encoding utf8
$report | ConvertTo-Json -Depth 5
