$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$dependency = Get-Content -LiteralPath (Join-Path $projectRoot 'vendor\ffmpeg\dependency.json') -Raw | ConvertFrom-Json
$downloadDirectory = Join-Path $projectRoot '.tools\downloads'
New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
$archive = Join-Path $downloadDirectory "ffmpeg-$($dependency.version)-essentials_build.zip"
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $dependency.url -OutFile $archive }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $dependency.sha256) { throw 'O hash do FFmpeg não corresponde à dependência fixada.' }
$extractedDirectory = Join-Path $downloadDirectory $dependency.directory
if (-not (Test-Path -LiteralPath (Join-Path $extractedDirectory 'bin\ffmpeg.exe'))) {
    Expand-Archive -LiteralPath $archive -DestinationPath $downloadDirectory -Force
}
& (Join-Path $PSScriptRoot 'Prepare-FFmpeg.ps1') -SourceDirectory (Join-Path $extractedDirectory 'bin')
