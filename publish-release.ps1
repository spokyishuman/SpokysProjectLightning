<#
.SYNOPSIS
    Builds SpokysPL in Release, zips output, generates update-manifest.json.
    Upload the .zip AND update-manifest.json to your web host, then every
    running copy of the app will see the update.
.EXAMPLE
    .\publish-release.ps1 -Version 4.3.0.0 -ManifestUrl "https://example.com/updates/update-manifest.json"
#>

param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$ManifestUrl,
    [string]$ReleaseNotes = "Bug fixes and improvements",
    [string]$OutputDir = "$PSScriptRoot\Release"
)

$ErrorActionPreference = "Stop"
$projectDir = "$PSScriptRoot\SpokysPL"
$zipName = "SpokysPL-$Version.zip"
$zipPath = "$OutputDir\$zipName"

# 1. Update version in .csproj
$csproj = "$projectDir\SpokysPL.csproj"
$content = Get-Content $csproj -Raw
$content = $content -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
$content = $content -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
$content = $content -replace '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$Version</InformationalVersion>"
Set-Content $csproj $content

# 2. Restore & build main project
Write-Host "`nBuilding SpokysPL v$Version..." -ForegroundColor Cyan
& dotnet build "$projectDir\SpokysPL.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# 3. Build the updater
Write-Host "Publishing updater..." -ForegroundColor Cyan
& dotnet publish "$PSScriptRoot\SpokysPL.Updater\SpokysPL.Updater.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Updater publish failed" }

# 4. Copy updater into main output
$publishDir = "$projectDir\bin\Release\net8.0-windows"
$updaterSrc = "$PSScriptRoot\SpokysPL.Updater\bin\Release\net8.0\win-x64\publish\SpokysPL.Updater.exe"
Copy-Item $updaterSrc "$publishDir\SpokysPL.Updater.exe" -Force

# 5. Create output dir & zip
New-Item $OutputDir -ItemType Directory -Force | Out-Null

# Use .NET ZIP to avoid compression issues
Add-Type -Assembly System.IO.Compression.FileSystem
if (Test-Path $zipPath) { Remove-Item $zipPath }
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
foreach ($file in Get-ChildItem $publishDir -File) {
    $entry = $zip.CreateEntry($file.Name, [System.IO.Compression.CompressionLevel]::Optimal)
    $entry.WriteAllBytes([System.IO.File]::ReadAllBytes($file.FullName))
}
$zip.Dispose()

# 6. Generate update manifest
$manifest = @{
    version      = $Version
    downloadUrl  = "$([System.IO.Path]::GetDirectoryName($ManifestUrl))/SpokysPL-$Version.zip"
    releaseNotes = $ReleaseNotes
    mandatory    = $false
} | ConvertTo-Json

$manifest | Set-Content "$OutputDir\update-manifest.json"

Write-Host "`nDone!" -ForegroundColor Green
Write-Host "  Zip:      $zipPath" -ForegroundColor Yellow
Write-Host "  Manifest: $OutputDir\update-manifest.json" -ForegroundColor Yellow
Write-Host "`nUpload both files to your web host." -ForegroundColor Cyan
Write-Host "Set the manifest URL in the app's Settings > Updates, then press 'Check for Updates'." -ForegroundColor Cyan
