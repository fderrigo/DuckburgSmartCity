# Adds the federation hostnames to the Windows hosts file. Run as Administrator.
#   PowerShell (admin):  ./scripts/add-hosts.ps1
#
# I nomi usano il dominio .test, riservato dalla RFC 6761 e per definizione non
# risolvibile su Internet: cosi' l'ambiente locale non puo' finire per sbaglio
# contro un server reale.
$ErrorActionPreference = "Stop"
$hostsPath = "$env:SystemRoot\System32\drivers\etc\hosts"
$names = @("trust-anchor.paperopoli.test", "cie-provider.paperopoli.test", "identity.paperopoli.test", "servizionline.paperopoli.test")

# --- Pulizia delle voci del vecchio schema ------------------------------------
# Prima gli hostname locali erano sotto un dominio reale. Se restano nel file,
# dirottano su 127.0.0.1 anche il dominio vero, rendendo irraggiungibile un
# eventuale deploy. Si rimuovono solo le righe i cui hostname appartengono tutti
# al progetto: qualunque altra voce resta intatta.
$righe = Get-Content $hostsPath
$obsolete = @()
$tenute = foreach ($riga in $righe) {
    $senzaCommento = ($riga -split "#")[0].Trim()
    if ($senzaCommento -match "paperopoli\.(derrigo\.it|org)") {
        $campi = $senzaCommento -split "\s+" | Where-Object { $_ }
        $hostnames = $campi | Select-Object -Skip 1
        $tuttiDelProgetto = $hostnames.Count -gt 0 -and
            -not ($hostnames | Where-Object { $_ -notmatch "paperopoli\.(derrigo\.it|org)$" })
        if ($tuttiDelProgetto) { $obsolete += $riga; continue }
    }
    # Via anche il commento che introduceva quel blocco, se resta orfano.
    if ($riga -match "^\s*#\s*SPID/CIE OIDC local test \(Duckburg\)\s*$") { continue }
    $riga
}

if ($obsolete.Count -gt 0) {
    Set-Content -Path $hostsPath -Value $tenute -Encoding ASCII
    Write-Host "Rimosse $($obsolete.Count) voci del vecchio schema:" -ForegroundColor Yellow
    $obsolete | ForEach-Object { Write-Host "  $_" }
}

# --- Voci correnti -------------------------------------------------------------
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
