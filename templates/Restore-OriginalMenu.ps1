param([string]$ClientPath = 'F:\ArcaneMP-1.12.2\client')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ClientPath).TrimEnd('\')
$targetFile = Join-Path $root 'ArcaneMPClient.dll'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'patch-manifest.json') | ConvertFrom-Json
$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $targetFile).Hash.ToLowerInvariant()
if ($actual -eq $manifest.originalSha256) { Write-Output 'The original client menu is already installed.'; return }
if ($actual -ne $manifest.patchedSha256) { throw 'The DLL has other changes or was updated. It was left unchanged.' }
$backups = Join-Path $root 'thai-menu-backups'
$original = Get-ChildItem -LiteralPath $backups -Directory | Sort-Object Name -Descending | ForEach-Object { $candidate = Join-Path $_.FullName 'ArcaneMPClient.dll'; if ((Test-Path -LiteralPath $candidate) -and (Get-FileHash -Algorithm SHA256 -LiteralPath $candidate).Hash.ToLowerInvariant() -eq $manifest.originalSha256) { $candidate } } | Select-Object -First 1
if (-not $original) { throw 'A matching original DLL backup was not found.' }
$temporaryFile = Join-Path $root 'ArcaneMPClient.dll.restore-staged'
Copy-Item -LiteralPath $original -Destination $temporaryFile -Force
$retainedThaiFile = Join-Path (Split-Path -Parent $original) ('ArcaneMPClient.thai-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.dll')
try { [IO.File]::Replace($temporaryFile, $targetFile, $retainedThaiFile) }
catch { throw ('Close Hogwarts Legacy before restoring: ' + $_.Exception.Message) }
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $targetFile).Hash.ToLowerInvariant() -ne $manifest.originalSha256) { throw 'Original DLL verification failed.' }
Write-Output 'Original client menu restored. The game translation PAK and server resources were left in place.'
