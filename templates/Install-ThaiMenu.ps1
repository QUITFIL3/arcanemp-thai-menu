param([string]$ClientPath = 'F:\ArcaneMP-1.12.2\client')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ClientPath).TrimEnd('\')
$targetFile = Join-Path $root 'ArcaneMPClient.dll'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'patch-manifest.json') | ConvertFrom-Json
$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $targetFile).Hash.ToLowerInvariant()
if ($actual -eq $manifest.patchedSha256) { Write-Output 'The Thai client menu is already installed.'; return }
if ($actual -ne $manifest.originalSha256) { throw 'This client DLL is a different version or has other modifications. No files were changed.' }
$payloadFile = Join-Path $PSScriptRoot 'menu-payload.bin'
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $payloadFile).Hash.ToLowerInvariant() -ne $manifest.payloadSha256) { throw 'Menu payload checksum mismatch.' }
$payload = [IO.File]::ReadAllBytes($payloadFile)
if ($payload.Length -ne $manifest.length) { throw 'Menu payload length mismatch.' }
$clientBytes = [IO.File]::ReadAllBytes($targetFile)
[Array]::Copy($payload, 0, $clientBytes, [long]$manifest.offset, $payload.Length)
$temporaryFile = Join-Path $root 'ArcaneMPClient.dll.thai-staged'
[IO.File]::WriteAllBytes($temporaryFile, $clientBytes)
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $temporaryFile).Hash.ToLowerInvariant() -ne $manifest.patchedSha256) { throw 'Staged DLL verification failed. The installed DLL was left unchanged.' }
$backupFolder = Join-Path $root ('thai-menu-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
New-Item -ItemType Directory -Force -Path $backupFolder | Out-Null
$backupFile = Join-Path $backupFolder 'ArcaneMPClient.dll'
try { [IO.File]::Replace($temporaryFile, $targetFile, $backupFile) }
catch { throw ('Close Hogwarts Legacy and run this installer again. The replacement could not complete: ' + $_.Exception.Message) }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'patch-manifest.json') -Destination (Join-Path $backupFolder 'patch-manifest.json')
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $targetFile).Hash.ToLowerInvariant() -ne $manifest.patchedSha256) { throw 'Installed DLL verification failed. Restore the original from the backup folder.' }
Write-Output ('Installed Thai client menu: ' + $targetFile)
Write-Output ('Original DLL backup: ' + $backupFile)
