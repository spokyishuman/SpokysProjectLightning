<# 
.SYNOPSIS
    Spoky's Project Vercel v1.1.0 Installer
.DESCRIPTION
    Installs Spoky's Project Vercel application with Windows Defender exclusions
#>

param(
    [string]$InstallPath = "$env:ProgramFiles\Spoky's Project Vercel",
    [switch]$CreateDesktopShortcut,
    [switch]$CreateStartMenuShortcut,
    [switch]$LaunchAfterInstall,
    [switch]$Silent
)

function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "[$timestamp] $Message"
}

function Add-DefenderExclusion {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return }
    try {
        $exclusions = Get-MpPreference -ErrorAction SilentlyContinue
        if ($exclusions.ExclusionPath -notcontains $Path) {
            Add-MpPreference -ExclusionPath $Path -ErrorAction SilentlyContinue
            Write-Log "Added Defender exclusion: $Path"
        }
    } catch {
        Write-Log "Warning: Could not add Defender exclusion for $Path"
    }
}

function Remove-DefenderExclusion {
    param([string]$Path)
    try {
        Remove-MpPreference -ExclusionPath $Path -ErrorAction SilentlyContinue
        Write-Log "Removed Defender exclusion: $Path"
    } catch { }
}

function Create-Shortcut {
    param(
        [string]$TargetPath,
        [string]$ShortcutPath,
        [string]$WorkingDirectory,
        [string]$Arguments = ""
    )
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($ShortcutPath)
        $shortcut.TargetPath = $TargetPath
        $shortcut.WorkingDirectory = $WorkingDirectory
        $shortcut.Arguments = $Arguments
        $shortcut.Save()
        Write-Log "Created shortcut: $ShortcutPath"
    } catch {
        Write-Log "Warning: Could not create shortcut $ShortcutPath"
    }
}

# Main installation
Write-Log "=== Spoky's Project Vercel v1.1.0 Installer ==="
Write-Log "Install path: $InstallPath"

if (-not $Silent) {
    $confirm = Read-Host "Install to '$InstallPath'? (Y/N)"
    if ($confirm -notmatch '^[Yy]') { Write-Log "Installation cancelled"; exit 1 }
}

# Create install directory
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
    Write-Log "Created directory: $InstallPath"
}

# Copy application files
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$sourceFiles = Get-ChildItem "$scriptDir\*" -Exclude "install.ps1", "uninstall.ps1"
foreach ($file in $sourceFiles) {
    $dest = Join-Path $InstallPath $file.Name
    if ($file.PSIsContainer) {
        Copy-Item $file.FullName $dest -Recurse -Force
    } else {
        Copy-Item $file.FullName $dest -Force
    }
}
Write-Log "Application files copied"

# Add Defender exclusions
Add-DefenderExclusion $InstallPath
Add-DefenderExclusion "$env:LOCALAPPDATA\SpokysPL"
Add-DefenderExclusion "$env:TEMP\SpokysPL"

# Create shortcuts
$exePath = Join-Path $InstallPath "SpokysProjectVercel.exe"
if ($CreateDesktopShortcut -or -not $Silent) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    Create-Shortcut $exePath (Join-Path $desktop "Spoky's Project Vercel.lnk") $InstallPath
}
if ($CreateStartMenuShortcut -or -not $Silent) {
    $startMenu = [Environment]::GetFolderPath("StartMenu")
    $progDir = Join-Path $startMenu "Programs\Spoky's Project Vercel"
    New-Item -ItemType Directory -Path $progDir -Force | Out-Null
    Create-Shortcut $exePath (Join-Path $progDir "Spoky's Project Vercel.lnk") $InstallPath
    Create-Shortcut $exePath (Join-Path $progDir "Uninstall Spoky's Project Vercel.lnk") $InstallPath "/uninstall"
}

# Write uninstaller
$uninstallScript = @"
param(`$Silent)
`$installPath = "`$env:ProgramFiles\Spoky's Project Vercel"
if (-not `$Silent) { `$confirm = Read-Host "Uninstall Spoky's Project Vercel? (Y/N)"; if (`$confirm -notmatch '^[Yy]') { exit 1 } }
Remove-DefenderExclusion `$installPath
Remove-DefenderExclusion "`$env:LOCALAPPDATA\SpokysPL"
Remove-DefenderExclusion "`$env:TEMP\SpokysPL"
if (Test-Path `\$installPath) { Remove-Item `\$installPath -Recurse -Force }
`$startMenu = [Environment]::GetFolderPath("StartMenu")
`$progDir = Join-Path `\$startMenu "Programs\Spoky's Project Vercel"
if (Test-Path `\$progDir) { Remove-Item `\$progDir -Recurse -Force }
`$desktop = [Environment]::GetFolderPath("Desktop")
`$shortcut = Join-Path `\$desktop "Spoky's Project Vercel.lnk"
if (Test-Path `\$shortcut) { Remove-Item `\$shortcut -Force }
Write-Host "Uninstall complete"
"@

$uninstallPath = Join-Path $InstallPath "uninstall.ps1"
$uninstallScript | Set-Content -Path $uninstallPath -Encoding UTF8
Write-Log "Uninstaller created"

Write-Log "=== Installation Complete ==="
Write-Log "Installed to: $InstallPath"

if ($LaunchAfterInstall -or (-not $Silent -and (Read-Host "Launch now? (Y/N)") -match '^[Yy]')) {
    Start-Process $exePath -WorkingDirectory $InstallPath
}