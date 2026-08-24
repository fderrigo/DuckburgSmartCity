# Prepares the RP private keys for local use (Docker and `dotnet run`).
# Copies the committed demo sample to the gitignored real file that the app loads.
#   ./scripts/setup-secrets.ps1
$ErrorActionPreference = "Stop"
$root = Join-Path $PSScriptRoot ".."
$sample = Join-Path $root "secrets/rp_private_keys.sample.json"
$real = Join-Path $root "secrets/rp_private_keys.json"

if (-not (Test-Path $real)) {
    Copy-Item $sample $real
    Write-Host "Created secrets/rp_private_keys.json from sample (demo keys)." -ForegroundColor Green
} else {
    Write-Host "secrets/rp_private_keys.json already exists. Leaving it untouched." -ForegroundColor Yellow
}

Write-Host "Loaded by:" -ForegroundColor Cyan
Write-Host "  - Docker  : mounted at /secrets, env Rp__PrivateKeysFile (docker-compose.yml)"
Write-Host "  - dotnet  : appsettings.Development.json -> Rp:PrivateKeysFile"
