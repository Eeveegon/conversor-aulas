# Assinatura do instalador

Identidade pessoal: **Gonçalo Barreiros**, sem associação à ACSS.
Certificado autoassinado de assinatura de código, RSA 3072 / SHA-256, válido por
três anos. `certificate.json` identifica o certificado; o `.cer` contém apenas
a chave pública. O ficheiro de certificado não é publicado nos anexos da release
nem incluído como ficheiro separado nos próximos instaladores. A identidade do
signatário continua visível na assinatura digital do MSI e do executável.

O certificado com chave privada fica no repositório **Pessoal do utilizador atual**
do Windows. A cópia PFX e a senha protegida por Windows DPAPI estão em
`.private/signing`, ignorados pelo Git. Não distribuir nem adicionar estes ficheiros
ao repositório. A proteção DPAPI só pode ser aberta pelo mesmo utilizador Windows
no mesmo computador; não constitui um backup portátil da senha.

## Assinar o MSI

```powershell
./scripts/Sign-Installer.ps1 -Path ./artifacts/ConversorAulas.msi -TimestampUrl '<serviço RFC 3161>'
```

Requer o SignTool do Windows SDK, ou o caminho explícito em `-SignToolPath`.
O script usa o certificado no repositório Pessoal e não coloca a senha PFX nos
argumentos do processo. O carimbo temporal deve ser configurado ao distribuir.

## Confiança no computador de destino

O certificado é autoassinado: a assinatura identifica o autor e permite verificar
a integridade, mas não ganha confiança pública. A importação do `.cer` em
Autoridades de Certificação de Raiz Fidedigna no computador de destino é uma
operação manual e deliberada, após confirmar a impressão digital do certificado.
Não se instala o PFX no computador de destino. Não foi alterado nenhum repositório
de confiança automaticamente. SmartScreen pode continuar a apresentar avisos.

## GitHub Actions

O PFX codificado em Base64 e a senha estão configurados em GitHub Actions Secrets:
`WINDOWS_SIGNING_CERTIFICATE_BASE64` e `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`.
O workflow importa o PFX no repositório Pessoal temporário do runner,
assina o executável e o MSI com timestamp e remove o certificado no final.
Não guardar PFX, senha ou o ficheiro DPAPI em artefactos, logs ou no Git.
A configuração dos secrets foi autorizada pelo titular do certificado.

Para obter a senha local quando for necessário configurar o secret, importa
`pfx-password.dpapi.xml` com `Import-Clixml` numa sessão PowerShell tua e usa o
`SecureString` resultante num mecanismo seguro de transferência. Não imprimir
a senha em logs nem a colocar em comandos guardados no histórico.
