$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$privateDirectory = Join-Path $projectRoot '.private\signing'
$publicDirectory = Join-Path $projectRoot 'signing'
$metadataPath = Join-Path $publicDirectory 'certificate.json'
if (Test-Path -LiteralPath $metadataPath) {
    throw 'Já existe um certificado configurado. Não é criado outro para evitar trocar a identidade de assinatura.'
}
New-Item -ItemType Directory -Path $privateDirectory, $publicDirectory -Force | Out-Null
$certificate = New-SelfSignedCertificate -Type CodeSigningCert `
    -Subject 'CN=Gonçalo Barreiros' `
    -FriendlyName 'Gonçalo Barreiros - Conversor de Audio de Aulas' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable -KeyUsage DigitalSignature `
    -NotAfter (Get-Date).AddYears(3)

# Generate the PFX password in memory. Persist only a Windows DPAPI-protected copy,
# decryptable by this Windows user on this computer. Never print the password.
$passwordBytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48)
$securePassword = ConvertTo-SecureString ([Convert]::ToBase64String($passwordBytes)) -AsPlainText -Force
$pfxPath = Join-Path $privateDirectory 'Goncalo-Barreiros.pfx'
Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $securePassword -CryptoAlgorithmOption AES256_SHA256 | Out-Null
$securePassword | Export-Clixml -LiteralPath (Join-Path $privateDirectory 'pfx-password.dpapi.xml')
$publicPath = Join-Path $publicDirectory 'Goncalo-Barreiros.cer'
Export-Certificate -Cert $certificate -FilePath $publicPath -Type CERT | Out-Null
[ordered]@{
    subject = $certificate.Subject
    thumbprint = $certificate.Thumbprint
    notBefore = $certificate.NotBefore.ToUniversalTime().ToString('O')
    notAfter = $certificate.NotAfter.ToUniversalTime().ToString('O')
    purpose = 'Code signing - Conversor de Audio de Aulas MSI'
    algorithm = 'RSA 3072 / SHA-256'
    publicCertificateSha256 = (Get-FileHash -LiteralPath $publicPath -Algorithm SHA256).Hash
} | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8
Write-Host "Criado: $($certificate.Subject)"
Write-Host "Thumbprint: $($certificate.Thumbprint)"
Write-Host "Validade até: $($certificate.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host 'Certificado público em signing; PFX e senha protegida em .private/signing (ignorados pelo Git).'
