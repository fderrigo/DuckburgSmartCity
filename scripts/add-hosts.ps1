# Adds the federation hostnames to the Windows hosts file. Run as Administrator.
#   PowerShell (admin):  ./scripts/add-hosts.ps1
$ErrorActionPreference = "Stop"
$hostsPath = "$env:SystemRoot\System32\drivers\etc\hosts"
$names = @("trust-anchor.paperopoli.derrigo.it", "cie-provider.paperopoli.derrigo.it", "identity.paperopoli.derrigo.it", "servizionline.paperopoli.org")

$current = Get-Content $hostsPath -Raw
$missing = $names | Where-Object { $current -notmatch [regex]::Escape($_) }
if (-not $missing) {
    Write-Host "Hosts entries already present. Nothing to do." -ForegroundColor Yellow
} else {
    $line = "127.0.0.1`t" + ($missing -join " ")
    Add-Content -Path $hostsPath -Value "`r`n# SPID/CIE OIDC local test (Duckburg)`r`n$line"
    Write-Host "Added to ${hostsPath}: $($missing -join ', ')" -ForegroundColor Green
}
Write-Host "Verify:" -ForegroundColor Cyan
Select-String -Path $hostsPath -Pattern "paperopoli|trust-anchor"
