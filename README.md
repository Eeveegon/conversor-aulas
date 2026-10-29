# Conversor de Audio de Aulas

Aplicação pessoal para Windows x64, em C# / .NET 10 e WinUI 3. Converte gravações em cópias Opus mais pequenas, na mesma pasta dos originais. Funciona localmente, sem contas, serviços nem envio de áudio.

## Instalação

Obtém o MSI na [release 0.1.1](https://github.com/Eeveegon/conversor-aulas/releases/tag/v0.1.1).
O nome no menu Iniciar e nas aplicações instaladas é **Conversor de Audio de Aulas**.
Requer Windows 10 versão 2004 ou superior / Windows 11, x64. O pacote instala em Program Files para todos os utilizadores e inclui os runtimes .NET / Windows App SDK e o FFmpeg. Pode pedir autorização de administrador.

MSI e executável são assinados por **Gonçalo Barreiros** com um certificado autoassinado. Não é publicado um ficheiro de certificado separado. Não são instalados certificados de confiança automaticamente. Windows e SmartScreen podem apresentar avisos. Ver `signing/README.md`.

## Funcionalidades

- Vários ficheiros, pastas, subpastas opcionais e arrastar e largar.
- Entrada M4A, AAC, WAV, MP3, FLAC e outros formatos suportados pelo FFmpeg.
- Saída Opus a 64 kbps VBR e 48 kHz por defeito; opções 24 e 16 kHz para voz.
- Tema claro/escuro conforme o Windows, incluindo a barra de título.
- Progresso, cancelamento e erros por ficheiro com possibilidade de repetir.
- Nome `Aula - comprimido.opus`, com numeração quando já existe uma cópia.
- Verificação de codec/duração antes de publicar a cópia; originais preservados.

Compressão com perdas, mantendo boa qualidade de audição. Uma entrada já muito comprimida pode não ficar menor. A frequência escolhida é a de entrada no codificador; alguns leitores apresentam sempre a base temporal Opus de 48 kHz.

## Desenvolvimento e build

Windows x64 e SDK .NET 10.0.403. Os pacotes NuGet incluem as ferramentas Windows SDK utilizadas pelo projeto. Não são necessários workloads MAUI.

```powershell
./scripts/Get-FFmpeg.ps1
./scripts/Build.ps1
dotnet run --project src/ConversorAulas.App -p:Platform=x64
dotnet run --project tests/ConversorAulas.IntegrationTests -- .
```

FFmpeg 9.0.2 Essentials está fixado por URL e SHA-256 em `vendor/ffmpeg/dependency.json`. O script valida o arquivo antes da extração. Ver `THIRD-PARTY-NOTICES.md` para licenças e fontes.

Para gerar o MSI com WiX 6.0.2:

```powershell
dotnet tool install --global wix --version 6.0.2
./scripts/Build-Installer.ps1
# Na máquina com a chave privada configurada:
./scripts/Build-Installer.ps1 -Sign
```

O pacote fica em `artifacts/release`, acompanhado pelos avisos de terceiros e SHA-256. `.private`, binários obtidos, amostras e resultados de compilação são ignorados pelo Git.

## GitHub Actions

`.github/workflows/windows-release.yml` compila e testa em Windows x64. Em tags `v*`, valida a versão, importa o certificado através de Actions Secrets, assina o executável e o MSI com timestamp, testa instalação/desinstalação no runner descartável e publica a release com os ficheiros de distribuição. Pull requests não recebem os secrets de assinatura.

Secrets necessários: `WINDOWS_SIGNING_CERTIFICATE_BASE64` e `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`. Nunca guardar a chave ou a senha no Git.

## Verificação

18 verificações de integração com FFmpeg real: M4A/AAC/WAV, duração, integridade dos originais, colisões, perfis 48/24/16 kHz, erro e cancelamento. O teste MSI valida metadados e, com `-Install`, instalação, ficheiros, atalho, registo e desinstalação. `-Install` destina-se ao runner descartável.

Para diagnóstico opcional de exceções da interface, define `CONVERSOR_DIAGNOSTICS` com um caminho gravável antes de abrir a aplicação.
