param([switch]$SkipPublish, [switch]$Sign, [string]$TimestampUrl = 'http://timestamp.digicert.com')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $env:TEMP) { $env:TEMP = Join-Path $projectRoot '.tools\tmp' }
if (-not $env:TMP) { $env:TMP = $env:TEMP }
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src\ConversorAulas.App\ConversorAulas.App.csproj')
$version = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'A versão deve ser numérica no formato major.minor.patch.' }
if (-not $SkipPublish) { & (Join-Path $PSScriptRoot 'Build.ps1') -Publish }
$publishDirectory = Join-Path $projectRoot 'artifacts\app-win-x64'
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'ConversorAulas.App.exe'))) { throw 'Falta a aplicação publicada.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishDirectory -Force
if ($Sign) { & (Join-Path $PSScriptRoot 'Sign-Installer.ps1') -Path (Join-Path $publishDirectory 'ConversorAulas.App.exe') -TimestampUrl $TimestampUrl }
$releaseDirectory = Join-Path $projectRoot 'artifacts\release'
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$installer = Join-Path $releaseDirectory "Conversor-de-Audio-de-Aulas-$version-win-x64.msi"
wix build (Join-Path $projectRoot 'installer\Package.wxs') -arch x64 -d "ProductVersion=$version" -d "ProjectRoot=$projectRoot" -d "PublishDir=$publishDirectory" -intermediatefolder (Join-Path $projectRoot 'artifacts\wix') -pdbtype none -out $installer
if ($LASTEXITCODE -ne 0) { throw 'A construção do MSI falhou.' }
if ($Sign) { & (Join-Path $PSScriptRoot 'Sign-Installer.ps1') -Path $installer -TimestampUrl $TimestampUrl }
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $releaseDirectory -Force
$checksum = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$checksum  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Instalador criado: $installer"
