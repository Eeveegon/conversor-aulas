# FFmpeg incluído

Executa `./scripts/Get-FFmpeg.ps1` para obter a distribuição Essentials 9.0.2,
fixada por URL e SHA-256 em `dependency.json`. O arquivo é validado antes da extração.
Para desenvolvimento também é possível usar `./scripts/Prepare-FFmpeg.ps1`
com `-SourceDirectory C:\caminho\ffmpeg\bin` para copiar uma distribuição local.

São necessários os executáveis Windows x64 autónomos `ffmpeg.exe` e `ffprobe.exe`,
com o codificador `libopus`. O script copia a licença e os dados da distribuição,
regista a versão/configuração e calcula os hashes SHA-256 dos executáveis.
Os binários não são guardados no Git; são copiados para a compilação e publicação.

O workflow GitHub/MSI utiliza a mesma dependência fixada e verificada. Não usa
downloads de `latest`. As fontes e licenças estão identificadas em
`THIRD-PARTY-NOTICES.md`, incluído na distribuição.
