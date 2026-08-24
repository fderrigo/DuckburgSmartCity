# Builds and starts the whole local federation (Trust Anchor + CIE OP + .NET RP).
#   ./scripts/start.ps1
$ErrorActionPreference = "Stop"
Push-Location (Join-Path $PSScriptRoot "..")
try {
    Write-Host "Building and starting federation (docker compose up --build)..." -ForegroundColor Cyan
    docker compose up --build
} finally {
    Pop-Location
}
