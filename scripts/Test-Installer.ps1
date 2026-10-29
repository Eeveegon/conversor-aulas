param([Parameter(Mandatory)][string]$Path, [switch]$Install)
$ErrorActionPreference = 'Stop'
$installerPath = (Resolve-Path -LiteralPath $Path).Path
$windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
$database = $windowsInstaller.OpenDatabase($installerPath, 0)
function Read-MsiProperty([string]$Name) {
    $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$Name'")
    [void]$view.Execute()
    $record = $view.Fetch()
    if (-not $record) { throw "Propriedade MSI em falta: $Name" }
    $value = $record.StringData(1)
    [void]$view.Close()
    return $value
}
$productCode = Read-MsiProperty 'ProductCode'
$version = Read-MsiProperty 'ProductVersion'
if ((Read-MsiProperty 'ProductName') -ne 'Conversor de Audio de Aulas') { throw 'Nome incorreto no MSI.' }
if ((Read-MsiProperty 'Manufacturer') -ne 'Gonçalo Barreiros') { throw 'Fabricante incorreto no MSI.' }
$summary = $database.SummaryInformation(0)
if ($summary.Property(7) -notlike 'x64;*') { throw 'O instalador não é x64.' }
Write-Host "Metadados MSI verificados: Conversor de Audio de Aulas $version, x64, $productCode"
if (-not $Install) { return }
# Use only on disposable CI runners. Installation is never performed by Build-Installer.
$logDirectory = Join-Path (Split-Path $installerPath -Parent) 'logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$installed = $false
try {
    $process = Start-Process msiexec.exe -ArgumentList @('/i', "`"$installerPath`"", '/qn', '/norestart', '/l*v', "`"$(Join-Path $logDirectory 'install.log')`"") -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -notin @(0, 3010)) { throw "Instalação falhou: $($process.ExitCode)" }
    $installed = $true
    $installDirectory = Join-Path $env:ProgramFiles 'Conversor de Audio de Aulas'
    foreach ($relativePath in @('ConversorAulas.App.exe', 'Assets\AppIcon.ico', 'ffmpeg\ffmpeg.exe', 'ffmpeg\ffprobe.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $installDirectory $relativePath))) { throw "Falta o ficheiro instalado $relativePath" }
    }
    $programsDirectory = [Environment]::GetFolderPath('CommonPrograms')
    if (-not (Test-Path -LiteralPath (Join-Path $programsDirectory 'Conversor de Audio de Aulas\Conversor de Audio de Aulas.lnk'))) { throw 'Falta o atalho do menu Iniciar.' }
    $uninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$productCode"
    $registration = Get-ItemProperty -LiteralPath $uninstallKey
    if ($registration.DisplayName -ne 'Conversor de Audio de Aulas' -or $registration.DisplayVersion -ne $version) { throw 'Registo de instalação incorreto.' }
    Write-Host 'Instalação, ficheiros, atalho e registo verificados.'
}
finally {
    if ($installed) {
        $process = Start-Process msiexec.exe -ArgumentList @('/x', $productCode, '/qn', '/norestart', '/l*v', "`"$(Join-Path $logDirectory 'uninstall.log')`"") -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -notin @(0, 3010)) { throw "Desinstalação falhou: $($process.ExitCode)" }
        if (Test-Path -LiteralPath "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$productCode") { throw 'O produto continua registado após a desinstalação.' }
        Write-Host 'Desinstalação verificada.'
    }
}
