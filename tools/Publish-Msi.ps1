[CmdletBinding()]
param(
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsRoot = Join-Path $repositoryRoot 'artifacts\release'
$publishRoot = Join-Path $artifactsRoot 'NetOpsTerminal-1.1.0-win-x64'
$installerProject = Join-Path $repositoryRoot 'installer\NetOpsTerminal.Installer\NetOpsTerminal.Installer.wixproj'
$artworkScript = Join-Path $PSScriptRoot 'Generate-InstallerArtwork.ps1'
$licenseScript = Join-Path $PSScriptRoot 'Generate-InstallerLicense.ps1'
$builtMsi = Join-Path $repositoryRoot 'installer\NetOpsTerminal.Installer\bin\x64\Release\NetOpsTerminal-1.1.0-win-x64.msi'
$releaseMsi = Join-Path $artifactsRoot 'NetOpsTerminal-1.1.0-win-x64.msi'
$hashPath = "$releaseMsi.sha256"
$localDotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
$dotnetPath = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

# Publish-Release rebuilds the complete self-contained application and checks it.
$publishScript = Join-Path $PSScriptRoot 'Publish-Release.ps1'
& $publishScript -Version '1.1.0' -SkipTests:$SkipTests
if (-not $?) {
    throw 'The Version 1.1 application publish failed.'
}
if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'NetOpsTerminal.exe'))) {
    throw 'The application publish did not contain NetOpsTerminal.exe.'
}

& $artworkScript
if (-not $?) { throw 'Installer artwork generation failed.' }
& $licenseScript
if (-not $?) { throw 'Installer license generation failed.' }

& $dotnetPath build $installerProject -c Release "-p:PublishRoot=$publishRoot" "-p:InstallerVersion=1.1.0"
if ($LASTEXITCODE -ne 0) {
    throw "The MSI build failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $builtMsi)) {
    throw "The MSI build did not produce $builtMsi."
}

# Check the approved machine-wide scope and installer identity before publishing it.
$windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
$database = $windowsInstaller.OpenDatabase($builtMsi, 0)
function Get-MsiProperty {
    param([Parameter(Mandatory)][string]$Name)
    $view = $database.OpenView("SELECT Value FROM Property WHERE Property = '$Name'")
    try {
        $null = $view.Execute()
        $record = $view.Fetch()
        if ($null -eq $record) { return $null }
        return $record.StringData(1)
    }
    finally {
        $null = $view.Close()
    }
}
if ((Get-MsiProperty 'ALLUSERS') -ne '1' -or
    $null -ne (Get-MsiProperty 'MSIINSTALLPERUSER') -or
    (Get-MsiProperty 'Manufacturer') -ne 'skadooooooooooosh' -or
    (Get-MsiProperty 'WIXUI_INSTALLDIR') -ne 'INSTALLFOLDER' -or
    (Get-MsiProperty 'ProductVersion') -ne '1.1.0' -or
    (Get-MsiProperty 'ProductLanguage') -ne '1033' -or
    (Get-MsiProperty 'ProductCode') -ne '{8F3F600B-B428-4832-B5D1-85FE100FEF02}' -or
    (Get-MsiProperty 'UpgradeCode') -ne '{3536CB34-CDD4-435C-B72E-8E7E1EEB259E}') {
    throw 'The MSI metadata does not match the approved Version 1.1 all-users wizard.'
}
foreach ($dialogName in @('WelcomeDlg', 'LicenseAgreementDlg', 'NetOpsInstallDirDlg', 'VerifyReadyDlg', 'ExitDialog')) {
    $dialogView = $database.OpenView("SELECT Dialog FROM Dialog WHERE Dialog = '$dialogName'")
    try {
        $null = $dialogView.Execute()
        if ($null -eq $dialogView.Fetch()) { throw "MSI wizard is missing $dialogName." }
    }
    finally { $null = $dialogView.Close() }
}
$licenseView = $database.OpenView("SELECT Text FROM Control WHERE Dialog_ = 'LicenseAgreementDlg' AND Control = 'LicenseText'")
try {
    $null = $licenseView.Execute()
    $licenseRecord = $licenseView.Fetch()
    if ($null -eq $licenseRecord -or
        $licenseRecord.StringData(1) -notlike '*skadooooooooooosh*' -or
        $licenseRecord.StringData(1) -notlike '*lansangan888@gmail.com*') {
        throw 'The MSI does not contain the approved user agreement.'
    }
}
finally { $null = $licenseView.Close() }

function Assert-MsiRow {
    param([Parameter(Mandatory)][string]$Query, [Parameter(Mandatory)][string]$Description)
    $rowView = $database.OpenView($Query)
    try {
        $null = $rowView.Execute()
        if ($null -eq $rowView.Fetch()) { throw "MSI is missing $Description." }
    }
    finally { $null = $rowView.Close() }
}
foreach ($shortcutOption in @(
    @{ Property = 'NETOPS_DESKTOP_SHORTCUT'; Control = 'DesktopShortcut'; Feature = 'DesktopShortcutFeature'; Shortcut = 'NetOpsDesktopShortcutLink'; Directory = 'DesktopFolder' },
    @{ Property = 'NETOPS_STARTMENU_SHORTCUT'; Control = 'StartMenuShortcut'; Feature = 'StartMenuShortcutFeature'; Shortcut = 'NetOpsStartMenuShortcutLink'; Directory = 'NetOpsProgramMenuFolder' }
)) {
    if ((Get-MsiProperty $shortcutOption.Property) -ne '1') {
        throw "MSI shortcut option $($shortcutOption.Property) must default to checked."
    }
    Assert-MsiRow "SELECT Control FROM Control WHERE Dialog_ = 'NetOpsInstallDirDlg' AND Control = '$($shortcutOption.Control)' AND Type = 'CheckBox' AND Property = '$($shortcutOption.Property)'" "$($shortcutOption.Control) checkbox"
    Assert-MsiRow "SELECT Feature FROM Feature WHERE Feature = '$($shortcutOption.Feature)'" "$($shortcutOption.Feature) feature"
    Assert-MsiRow "SELECT Shortcut FROM Shortcut WHERE Shortcut = '$($shortcutOption.Shortcut)' AND Directory_ = '$($shortcutOption.Directory)'" "$($shortcutOption.Shortcut) shortcut"
    foreach ($choiceEvent in @('AddLocal', 'Remove')) {
        Assert-MsiRow "SELECT Event FROM ControlEvent WHERE Dialog_ = 'NetOpsInstallDirDlg' AND Control_ = 'Next' AND Event = '$choiceEvent' AND Argument = '$($shortcutOption.Feature)'" "$choiceEvent event for $($shortcutOption.Feature)"
    }
}
Assert-MsiRow "SELECT Event FROM ControlEvent WHERE Dialog_ = 'NetOpsInstallDirDlg' AND Control_ = 'Next' AND Event = 'DoAction' AND Argument = 'WixUIValidatePath_X64'" 'install-folder validation'
Assert-MsiRow "SELECT Event FROM ControlEvent WHERE Dialog_ = 'NetOpsInstallDirDlg' AND Control_ = 'Next' AND Event = 'NewDialog' AND Argument = 'VerifyReadyDlg'" 'Next navigation after shortcut choices'
$view = $database.OpenView('SELECT File FROM File')
try {
    $null = $view.Execute()
    $msiFileCount = 0
    while ($null -ne ($record = $view.Fetch())) { $msiFileCount++ }
}
finally {
    $null = $view.Close()
}
$publishedFileCount = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File).Count
if ($msiFileCount -ne $publishedFileCount) {
    throw "MSI file count ($msiFileCount) does not match publish file count ($publishedFileCount)."
}
$view = $null
$database = $null
$windowsInstaller = $null

Copy-Item -LiteralPath $builtMsi -Destination $releaseMsi -Force
$msiHash = (Get-FileHash -LiteralPath $releaseMsi -Algorithm SHA256).Hash
"$msiHash  $([System.IO.Path]::GetFileName($releaseMsi))" |
    Set-Content -LiteralPath $hashPath -Encoding ascii
Write-Host "MSI: $releaseMsi"
Write-Host "Payload files: $msiFileCount"
Write-Host "SHA256: $msiHash"

