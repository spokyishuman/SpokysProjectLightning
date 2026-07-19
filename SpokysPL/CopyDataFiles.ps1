# Spoky's PL - Data File Copier
# Copies original Project Lightning data files into the app directory
# Run this after building the app

$sourceDir = "$env:USERPROFILE\Downloads\PL_Extracted"
$destDir = "D:\Spoky's Project Lightning\SpokysPL\bin\Debug\net8.0-windows"

Write-Host "📋 Spoky's PL - Data File Copier" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan

if (-not (Test-Path $sourceDir)) {
    Write-Host "❌ Source directory not found: $sourceDir" -ForegroundColor Red
    Write-Host ""
    Write-Host "Looking in alternate locations..." -ForegroundColor Yellow
    
    $altPaths = @(
        "$env:USERPROFILE\Downloads\PL_Extracted_2",
        "$env:USERPROFILE\Downloads\Project-Lightning-4.2.0.0",
        "D:\Spoky's Project Lightning\original_data"
    )
    
    foreach ($path in $altPaths) {
        if (Test-Path $path) {
            $sourceDir = $path
            Write-Host "✅ Found at: $path" -ForegroundColor Green
            break
        }
    }
    
    if (-not (Test-Path $sourceDir)) {
        Write-Host "❌ Could not find PL_Extracted directory." -ForegroundColor Red
        Write-Host "Please download the original Project Lightning from:" -ForegroundColor Yellow
        Write-Host "https://github.com/LightnigFast/Project-Lightning/releases" -ForegroundColor Yellow
        exit 1
    }
}

if (-not (Test-Path $destDir)) {
    Write-Host "⚠️  Build output directory not found. Creating..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
}

$filesToCopy = @("data.json", "data-fix.json", "shop.json")

foreach ($file in $filesToCopy) {
    $src = Join-Path $sourceDir $file
    $dst = Join-Path $destDir $file
    
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $dst -Force
        Write-Host "✅ Copied: $file" -ForegroundColor Green
    } else {
        Write-Host "⚠️  Not found: $file" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "⚡ Data files copied successfully!" -ForegroundColor Cyan
Write-Host "You can now run Spoky's PL from: $destDir\SpokysPL.exe" -ForegroundColor Cyan
