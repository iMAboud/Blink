# install.ps1 — hosted at da.gd/blinks
$dest = "$env:LOCALAPPDATA\Blink"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$url = "https://github.com/iMAboud/Blink/releases/download/1.3/Blink.exe"
Invoke-WebRequest $url -OutFile "$dest\Blink.exe"
# Optionally add to PATH
$path = [Environment]::GetEnvironmentVariable("Path", "User")
if ($path -notlike "*$dest*") {
    [Environment]::SetEnvironmentVariable("Path", "$path;$dest", "User")
}
Write-Host "Blink installed to $dest" -ForegroundColor Green
