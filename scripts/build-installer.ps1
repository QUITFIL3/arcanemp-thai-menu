param([string]$OutputPath = '')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputPath) { $OutputPath = Join-Path $repoRoot 'dist/ArcaneThaiInstaller.exe' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.IO.Compression.dll')
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',('/out:' + $OutputPath),('/win32manifest:' + (Join-Path $repoRoot 'installer/app.manifest')))
foreach ($reference in $references) { $arguments += '/reference:' + $reference }
$arguments += '/resource:' + (Join-Path $repoRoot 'src/translations.tsv') + ',ArcaneThai.Translations'
$arguments += '/resource:' + (Join-Path $repoRoot 'installer/Resources/thai-overlay.js') + ',ArcaneThai.Overlay'
$arguments += '/resource:' + (Join-Path $repoRoot 'assets/ArcaneThai.woff2') + ',ArcaneThai.Font'
$arguments += '/resource:' + (Join-Path $repoRoot 'assets/OFL-NotoSansThai.txt') + ',ArcaneThai.FontLicense'
$arguments += Join-Path $repoRoot 'installer/PatchEngine.cs'
$arguments += Join-Path $repoRoot 'installer/Program.cs'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Write-Output ('Built: ' + $OutputPath)
