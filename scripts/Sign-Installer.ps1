param(
    [Parameter(Mandatory)][string]$Path,
    [string]$SignToolPath,
    [string]$TimestampUrl
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$installerPath = (Resolve-Path -LiteralPath $Path).Path
if ([IO.Path]::GetExtension($installerPath) -notin @('.msi', '.exe')) { throw 'Indica um instalador .msi ou executável .exe.' }
$metadata = Get-Content -LiteralPath (Join-Path $projectRoot 'signing\certificate.json') -Raw | ConvertFrom-Json
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$($metadata.thumbprint)" -ErrorAction Stop
if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date)) { throw 'O certificado não tem chave privada ou já expirou.' }
if (-not $SignToolPath) {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { $SignToolPath = $command.Source }
    else {
        $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        $candidate = Get-ChildItem -LiteralPath $sdkRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        $SignToolPath = $candidate
    }
    if (-not $SignToolPath) {
        $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } elseif ($env:DOTNET_CLI_HOME) { Join-Path $env:DOTNET_CLI_HOME '.nuget\packages' } else { Join-Path $env:USERPROFILE '.nuget\packages' }
        $SignToolPath = Get-ChildItem -Path (Join-Path $nugetRoot 'microsoft.windows.sdk.buildtools\*\bin\*\x64\signtool.exe') -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
}
if (-not $SignToolPath) { throw 'Não foi encontrado o SignTool do Windows SDK. Indica -SignToolPath.' }
$signArguments = @('sign', '/s', 'My', '/sha1', $metadata.thumbprint, '/fd', 'SHA256', '/d', 'Conversor de Audio de Aulas')
if ($TimestampUrl) { $signArguments += @('/tr', $TimestampUrl, '/td', 'SHA256') }
else { Write-Warning 'Sem timestamp: a assinatura depende da validade do certificado. Para distribuição, indica um serviço RFC 3161 com -TimestampUrl.' }
$signArguments += $installerPath
& $SignToolPath @signArguments
if ($LASTEXITCODE -ne 0) { throw 'A assinatura do MSI falhou.' }
& (Join-Path $PSScriptRoot 'Test-CodeSignature.ps1') -Path $installerPath -RequireTimestamp:([bool]$TimestampUrl)
