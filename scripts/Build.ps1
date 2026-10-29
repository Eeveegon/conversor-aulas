param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
# Only affects this PowerShell process and its child processes.
if (-not $env:TEMP) { $env:TEMP = Join-Path $projectRoot '.tools\tmp' }
if (-not $env:TMP) { $env:TMP = $env:TEMP }
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
$project = Join-Path $projectRoot 'src\ConversorAulas.App\ConversorAulas.App.csproj'
if ($Publish) {
    dotnet publish $project -c Release -p:Platform=x64 -o (Join-Path $projectRoot 'artifacts\app-win-x64')
} else {
    dotnet build $project -c Debug -p:Platform=x64
}
if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }
