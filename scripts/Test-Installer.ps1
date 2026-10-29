param([Parameter(Mandatory)][string]$Path, [switch]$Install, [string]$UpgradeFrom)
$ErrorActionPreference = 'Stop'
$installerPath = (Resolve-Path -LiteralPath $Path).Path
$windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
$database = $windowsInstaller.OpenDatabase($installerPath, 0)
function Read-MsiProperty([string]$Name, $Source = $database) {
    $view = $Source.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$Name'")
    [void]$view.Execute()
    $record = $view.Fetch()
    if (-not $record) { throw "Propriedade MSI em falta: $Name" }
    $value = $record.StringData(1)
    [void]$view.Close()
    return $value
}
$productCode = Read-MsiProperty 'ProductCode'
$version = Read-MsiProperty 'ProductVersion'
$upgradeCode = Read-MsiProperty 'UpgradeCode'
if ($upgradeCode -ne '{D4201946-4CED-452D-9B56-87A07565B6CA}') { throw 'UpgradeCode alterado: incompatível com a versão 0.1.1.' }
if ((Read-MsiProperty 'ProductName') -ne 'Conversor de Audio de Aulas') { throw 'Nome incorreto no MSI.' }
if ((Read-MsiProperty 'Manufacturer') -ne 'Gonçalo Barreiros') { throw 'Fabricante incorreto no MSI.' }
$summary = $database.SummaryInformation(0)
if ($summary.Property(7) -notlike 'x64;*') { throw 'O instalador não é x64.' }
Write-Host "Metadados MSI verificados: Conversor de Audio de Aulas $version, x64, $productCode"
$oldProductCode = $null
if ($UpgradeFrom) {
    $previousPath = (Resolve-Path -LiteralPath $UpgradeFrom).Path
    $previousDatabase = $windowsInstaller.OpenDatabase($previousPath, 0)
    $oldProductCode = Read-MsiProperty 'ProductCode' $previousDatabase
    $oldVersion = Read-MsiProperty 'ProductVersion' $previousDatabase
    if ((Read-MsiProperty 'UpgradeCode' $previousDatabase) -ne $upgradeCode -or $oldProductCode -eq $productCode -or
        [version]$oldVersion -ge [version]$version -or (Read-MsiProperty 'ProductLanguage' $previousDatabase) -ne (Read-MsiProperty 'ProductLanguage')) {
        throw 'Os MSI não são compatíveis para uma atualização principal.'
    }
    $view = $database.OpenView('SELECT `UpgradeCode`, `VersionMax`, `Attributes`, `ActionProperty` FROM `Upgrade`')
    [void]$view.Execute()
    $canUpgrade = $false
    while ($record = $view.Fetch()) {
        if ($record.StringData(1) -eq $upgradeCode -and $record.StringData(4) -eq 'WIX_UPGRADE_DETECTED' -and
            [version]$record.StringData(2) -gt [version]$oldVersion -and ($record.IntegerData(3) -band 2) -eq 0) { $canUpgrade = $true }
    }
    [void]$view.Close()
    if (-not $canUpgrade) { throw 'A tabela Upgrade não remove a versão anterior.' }
    $view = $database.OpenView('SELECT `Action`, `Sequence` FROM `InstallExecuteSequence`')
    [void]$view.Execute()
    $sequence = @{}
    while ($record = $view.Fetch()) { $sequence[$record.StringData(1)] = $record.IntegerData(2) }
    [void]$view.Close()
    if ($sequence.RemoveExistingProducts -le $sequence.InstallInitialize -or $sequence.RemoveExistingProducts -ge $sequence.InstallFiles) { throw 'A remoção anterior deve estar dentro da transação com rollback.' }
    Write-Host "Atualização $oldVersion -> ${version}: identidade, idioma, tabela Upgrade e rollback verificados."
}
if (-not $Install) { return }
# Use only on disposable CI runners. Installation is never performed by Build-Installer.
if (@($windowsInstaller.RelatedProducts($upgradeCode)).Count -gt 0) { throw 'Já existe uma instalação. Executa este teste apenas numa máquina descartável limpa.' }
$logDirectory = Join-Path (Split-Path $installerPath -Parent) 'logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$installed = $false
$previousInstalled = $false
try {
    if ($UpgradeFrom) {
        $process = Start-Process msiexec.exe -ArgumentList @('/i', "`"$previousPath`"", '/qn', '/norestart', '/l*v', "`"$(Join-Path $logDirectory 'install-previous.log')`"") -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -notin @(0, 3010)) { throw "Instalação da versão anterior falhou: $($process.ExitCode)" }
        $previousInstalled = $true
        if ($windowsInstaller.ProductState($oldProductCode) -ne 5) { throw 'A versão anterior não ficou instalada.' }
    }
    $process = Start-Process msiexec.exe -ArgumentList @('/i', "`"$installerPath`"", '/qn', '/norestart', '/l*v', "`"$(Join-Path $logDirectory 'install.log')`"") -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -notin @(0, 3010)) {
        Get-Content -LiteralPath (Join-Path $logDirectory 'install.log') -Tail 100 | Write-Host
        throw "Instalação falhou: $($process.ExitCode)"
    }
    $installed = $true
    if ($UpgradeFrom) {
        if ($windowsInstaller.ProductState($oldProductCode) -ne -1) { throw 'A versão anterior continua instalada após a atualização.' }
        $previousInstalled = $false
        $related = @($windowsInstaller.RelatedProducts($upgradeCode))
        if ($related.Count -ne 1 -or $related[0] -ne $productCode) { throw 'A atualização deixou produtos duplicados.' }
        Write-Host "Atualização real $oldVersion -> ${version}: versão anterior removida, sem instalações duplicadas."
    }
    $installDirectory = Join-Path $env:ProgramFiles 'Conversor de Audio de Aulas'
    foreach ($relativePath in @('ConversorAulas.App.exe', 'Assets\AppIcon.ico', 'ffmpeg\ffmpeg.exe', 'ffmpeg\ffprobe.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $installDirectory $relativePath))) { throw "Falta o ficheiro instalado $relativePath" }
    }
    $programsDirectory = [Environment]::GetFolderPath('CommonPrograms')
    if (-not (Test-Path -LiteralPath (Join-Path $programsDirectory 'Conversor de Audio de Aulas\Conversor de Audio de Aulas.lnk'))) { throw 'Falta o atalho do menu Iniciar.' }
    $uninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$productCode"
    $registration = Get-ItemProperty -LiteralPath $uninstallKey
    if ($registration.DisplayName -ne 'Conversor de Audio de Aulas' -or $registration.DisplayVersion -ne $version) { throw 'Registo de instalação incorreto.' }
    $assemblyVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $installDirectory 'ConversorAulas.App.dll')).ProductVersion.Split('+')[0]
    if ([version]$assemblyVersion -ne [version]$version) { throw 'A aplicação instalada não corresponde à nova versão.' }
    if ($UpgradeFrom) {
        $process = Start-Process msiexec.exe -ArgumentList @('/i', "`"$previousPath`"", '/qn', '/norestart', '/l*v', "`"$(Join-Path $logDirectory 'downgrade.log')`"") -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 1603 -or $windowsInstaller.ProductState($productCode) -ne 5) { throw 'O downgrade não foi bloqueado corretamente.' }
        Write-Host 'Downgrade para a versão anterior bloqueado.'
    }
    Write-Host 'Instalação, ficheiros, atalho e registo verificados.'
}
finally {
    if ($previousInstalled) {
        $process = Start-Process msiexec.exe -ArgumentList @('/x', $oldProductCode, '/qn', '/norestart') -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -notin @(0, 3010)) { Write-Warning "Falhou a limpeza da instalação anterior: $($process.ExitCode)" }
    }
    if ($installed) {
        $process = Start-Process msiexec.exe -ArgumentList @('/x', $productCode, '/qn', '/norestart', '/l*v', "`"$(Join-Path $logDirectory 'uninstall.log')`"") -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -notin @(0, 3010)) { throw "Desinstalação falhou: $($process.ExitCode)" }
        if (Test-Path -LiteralPath "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$productCode") { throw 'O produto continua registado após a desinstalação.' }
        Write-Host 'Desinstalação verificada.'
    }
}
