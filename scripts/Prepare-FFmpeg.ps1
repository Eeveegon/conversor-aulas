param([string]$SourceDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $projectRoot 'vendor\ffmpeg'
if (-not $SourceDirectory) {
    $command = Get-Command ffmpeg.exe -ErrorAction Stop
    $file = Get-Item -LiteralPath $command.Source
    $resolved = if ($file.LinkType) { $file.ResolveLinkTarget($true).FullName } else { $file.FullName }
    $SourceDirectory = Split-Path $resolved -Parent
}
$SourceDirectory = (Resolve-Path -LiteralPath $SourceDirectory).Path
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($name in @('ffmpeg.exe', 'ffprobe.exe')) {
    $source = Join-Path $SourceDirectory $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Não foi encontrado $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $destination $name) -Force
}
$distributionRoot = Split-Path $SourceDirectory -Parent
if (-not (Test-Path -LiteralPath (Join-Path $distributionRoot 'LICENSE'))) {
    throw 'A distribuição deve incluir LICENSE na pasta acima de bin. Usa uma distribuição completa do FFmpeg.'
}
Copy-Item -LiteralPath (Join-Path $distributionRoot 'LICENSE') -Destination (Join-Path $destination 'LICENSE-FFmpeg.txt') -Force
if (Test-Path -LiteralPath (Join-Path $distributionRoot 'README.txt')) {
    Copy-Item -LiteralPath (Join-Path $distributionRoot 'README.txt') -Destination (Join-Path $destination 'README-distribution.txt') -Force
}
$encoderList = & (Join-Path $destination 'ffmpeg.exe') -hide_banner -encoders 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $encoderList -notmatch '\blibopus\b') { throw 'Esta versão do FFmpeg não inclui libopus.' }
$version = & (Join-Path $destination 'ffmpeg.exe') -version | Select-Object -First 3
$manifest = [ordered]@{
    version = $version
    source = 'Local FFmpeg distribution; see README-distribution.txt for upstream and build information.'
    binaries = @(Get-ChildItem -LiteralPath $destination -Filter '*.exe' | ForEach-Object {
        @{ file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'build-info.json') -Encoding utf8
Write-Host "FFmpeg e FFprobe preparados em $destination"
