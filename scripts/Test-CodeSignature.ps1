param([Parameter(Mandatory)][string]$Path, [switch]$RequireTimestamp)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$metadata = Get-Content -LiteralPath (Join-Path $projectRoot 'signing\certificate.json') -Raw | ConvertFrom-Json
$signature = Get-AuthenticodeSignature -LiteralPath $Path
if ($signature.SignerCertificate.Thumbprint -ne $metadata.thumbprint) { throw 'A assinatura não corresponde ao certificado configurado.' }
if ($RequireTimestamp -and -not $signature.TimeStamperCertificate) { throw 'Falta o carimbo temporal.' }
if ($signature.Status -eq 'Valid') { Write-Host 'Assinatura válida e de confiança neste computador.'; return }
# Windows reports an untrusted self-signed root as UnknownError on some versions.
# Accept that case only when the chain has exactly that trust error. Never accept
# HashMismatch, missing signatures, expired certificates or unrelated chain errors.
if ($signature.Status -notin @('NotTrusted', 'UnknownError')) { throw "Assinatura inválida: $($signature.Status) - $($signature.StatusMessage)" }
$chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
try {
    $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    $chainValid = $chain.Build($signature.SignerCertificate)
    $errors = @($chain.ChainStatus | Where-Object { $_.Status -ne [System.Security.Cryptography.X509Certificates.X509ChainStatusFlags]::NoError })
    if ($chainValid -or $errors.Count -ne 1 -or $errors[0].Status -ne [System.Security.Cryptography.X509Certificates.X509ChainStatusFlags]::UntrustedRoot) {
        throw "A assinatura tem um erro diferente da raiz autoassinada: $($signature.StatusMessage)"
    }
    Write-Host "Assinatura de $($signature.SignerCertificate.Subject) verificada; raiz autoassinada sem confiança local."
}
finally { $chain.Dispose() }
