param(
    [switch]$SingleExe = $true,
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$rootDir = $PSScriptRoot
$releaseDir = Join-Path $rootDir "Release"
$appStagingDir = Join-Path $releaseDir "App"
$binDir = Join-Path $rootDir "ShareX\bin\Release\win-x64"

$versionArgs = @()
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $versionArgs += "-p:Version=$Version"
    $versionArgs += "-p:InformationalVersion=$Version"
    Write-Host "Building version: $Version" -ForegroundColor Cyan
}

Write-Host "Publishing debloated Blink application for win-x64..." -ForegroundColor Cyan
if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $appStagingDir -Force | Out-Null

dotnet publish (Join-Path $rootDir "ShareX\ShareX.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -p:EnableWindowsTargeting=true @versionArgs -o $appStagingDir
if ($LASTEXITCODE -ne 0) {
    throw "ShareX publish failed."
}

Write-Host "Publishing Blink Updater..." -ForegroundColor Cyan
dotnet publish (Join-Path $rootDir "ShareX.Updater\ShareX.Updater.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -p:EnableWindowsTargeting=true @versionArgs -o $appStagingDir
if ($LASTEXITCODE -ne 0) {
    throw "Updater publish failed."
}

# Clean PDB and XML files from staging
Get-ChildItem -Path $appStagingDir -Recurse -File | Where-Object { $_.Extension -eq ".pdb" -or $_.Extension -eq ".xml" } | Remove-Item -Force

Write-Host "Generating file SHA-256 manifest for differential updates..." -ForegroundColor Cyan
$manifestPath = Join-Path $appStagingDir "manifest.json"
$files = Get-ChildItem -Path $appStagingDir -Recurse -File | Where-Object { $_.Name -ne "manifest.json" }
$manifestData = @{}

foreach ($f in $files) {
    $relPath = $f.FullName.Substring($appStagingDir.Length + 1).Replace("\", "/")
    $hash = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestData[$relPath] = @{
        "hash" = $hash
        "size" = $f.Length
    }
}

$manifestJson = $manifestData | ConvertTo-Json -Depth 5
Set-Content -Path $manifestPath -Value $manifestJson -Encoding UTF8

$payloadZip = Join-Path $rootDir "ShareX.Launcher\Blink_Payload.zip"
Write-Host "Creating compressed payload archive at $payloadZip..." -ForegroundColor Cyan
if (Test-Path $payloadZip) {
    Remove-Item $payloadZip -Force
}

[System.IO.Compression.ZipFile]::CreateFromDirectory($appStagingDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$zipSize = (Get-Item $payloadZip).Length / 1MB
Write-Host ("Payload archive created. Size: {0:N2} MB" -f $zipSize) -ForegroundColor Green

Write-Host "Building Blink single-executable launcher..." -ForegroundColor Cyan
dotnet publish (Join-Path $rootDir "ShareX.Launcher\ShareX.Launcher.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -p:EnableWindowsTargeting=true @versionArgs -o (Join-Path $rootDir "ShareX.Launcher\publish")
if ($LASTEXITCODE -ne 0) {
    throw "Launcher publish failed."
}

$launcherBuilt = Join-Path $rootDir "ShareX.Launcher\publish\Blink.exe"
$distSingleExe = Join-Path $releaseDir "Blink.exe"
Copy-Item $launcherBuilt -Destination $distSingleExe -Force
Copy-Item $launcherBuilt -Destination (Join-Path $rootDir "Blink.exe") -Force

# Ensure no legacy ShareX.exe remains
Remove-Item (Join-Path $releaseDir "ShareX.exe") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $rootDir "ShareX.exe") -Force -ErrorAction SilentlyContinue

# Clean staging App directory so Release directory only has the single Blink.exe
Remove-Item $appStagingDir -Recurse -Force

$singleExeSize = (Get-Item $distSingleExe).Length / 1MB
Write-Host "==================================================" -ForegroundColor Green
Write-Host ("Production single-executable ready at: {0}" -f $distSingleExe) -ForegroundColor Green
Write-Host ("Single Blink.exe size: {0:N2} MB" -f $singleExeSize) -ForegroundColor Green
Write-Host ("Root launcher ready at: {0}" -f (Join-Path $rootDir "Blink.exe")) -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
