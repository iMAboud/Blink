param(
    [switch]$SingleExe = $true
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$rootDir = $PSScriptRoot
$releaseDir = Join-Path $rootDir "Release"
$appStagingDir = Join-Path $releaseDir "App"
$binDir = Join-Path $rootDir "ShareX\bin\Release\win-x64"

Write-Host "Publishing debloated Blink application for win-x64..." -ForegroundColor Cyan
if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $appStagingDir -Force | Out-Null

dotnet publish (Join-Path $rootDir "ShareX\ShareX.csproj") -c Release -r win-x64 --self-contained false -p:EnableWindowsTargeting=true -o $appStagingDir
if ($LASTEXITCODE -ne 0) {
    throw "ShareX publish failed."
}

Write-Host "Publishing Blink Updater..." -ForegroundColor Cyan
dotnet publish (Join-Path $rootDir "ShareX.Updater\ShareX.Updater.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:EnableWindowsTargeting=true -o $appStagingDir
if ($LASTEXITCODE -ne 0) {
    throw "Updater publish failed."
}

# Clean PDB and XML files from staging
Get-ChildItem -Path $appStagingDir -Recurse -File | Where-Object { $_.Extension -eq ".pdb" -or $_.Extension -eq ".xml" } | Remove-Item -Force

$payloadZip = Join-Path $rootDir "ShareX.Launcher\Blink_Payload.zip"
Write-Host "Creating compressed payload archive at $payloadZip..." -ForegroundColor Cyan
if (Test-Path $payloadZip) {
    Remove-Item $payloadZip -Force
}

[System.IO.Compression.ZipFile]::CreateFromDirectory($appStagingDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$zipSize = (Get-Item $payloadZip).Length / 1MB
Write-Host ("Payload archive created. Size: {0:N2} MB" -f $zipSize) -ForegroundColor Green

Write-Host "Building Blink single-executable launcher (.NET Framework 4.8)..." -ForegroundColor Cyan
dotnet build (Join-Path $rootDir "ShareX.Launcher\ShareX.Launcher.csproj") -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Launcher build failed."
}

$launcherBuilt = Join-Path $rootDir "ShareX.Launcher\bin\Release\Blink.exe"
$launcherConfig = Join-Path $rootDir "ShareX.Launcher\bin\Release\Blink.exe.config"
$distSingleExe = Join-Path $releaseDir "Blink.exe"
Copy-Item $launcherBuilt -Destination $distSingleExe -Force
Copy-Item $launcherBuilt -Destination (Join-Path $rootDir "Blink.exe") -Force
if (Test-Path $launcherConfig) {
    Copy-Item $launcherConfig -Destination (Join-Path $releaseDir "Blink.exe.config") -Force
    Copy-Item $launcherConfig -Destination (Join-Path $rootDir "Blink.exe.config") -Force
}

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
