# FFmpeg incluído

Executa `./scripts/Prepare-FFmpeg.ps1` para copiar a distribuição local para esta pasta.
Também é possível indicar `-SourceDirectory C:\caminho\ffmpeg\bin`.

São necessários os executáveis Windows x64 autónomos `ffmpeg.exe` e `ffprobe.exe`,
com o codificador `libopus`. O script copia a licença e os dados da distribuição,
regista a versão/configuração e calcula os hashes SHA-256 dos executáveis.
Os binários não são guardados no Git; são copiados para a compilação e publicação.

Para o futuro fluxo GitHub/MSI, fixar a distribuição, URL, hashes e respetivo
código-fonte/licenças antes de automatizar a obtenção. Não usar downloads de
`latest` sem validação. O build local atual usa a distribuição já instalada.
