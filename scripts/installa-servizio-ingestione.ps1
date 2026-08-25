<#
    installa-servizio-ingestione.ps1

    Installa l'adattatore di ingestione come servizio Windows.

    Perche' non un sito IIS: IIS presume che il lavoro nasca da una richiesta. Carica
    l'applicazione alla prima, la scarica dopo qualche minuto di silenzio, la ricicla
    ogni notte. L'ingestione invece si sveglia da sola e non la chiama nessuno: sotto
    quelle regole non parte affatto, e il corpus resta vuoto senza che niente segnali un
    guasto, perche' guasti non ce ne sono. Come servizio parte al boot, si riavvia se
    cade, e nessuno decide di scaricarla.

    Eseguire come amministratore:
        .\installa-servizio-ingestione.ps1

    E' idempotente: se il servizio esiste gia' lo ferma, lo riconfigura e lo riavvia.
#>

[CmdletBinding()]
param(
    [string]$Cartella = "C:\inetpub\wwwroot\DuckBurgSmartCity\Duckburg.Ingestione",
    [string]$CmsAppData = "C:\inetpub\wwwroot\DuckBurgSmartCity\Duckburg.Portal\App_Data",
    [string]$Nome = "DuckburgIngestione",
    [string]$Etichetta = "Duckburg Ingestione",
    [string]$Account = "NT AUTHORITY\NetworkService",
    # Toglie il vecchio sito IIS, se c'era: due copie che pubblicano lo stesso corpus non
    # si danneggiano a vicenda, ma raddoppiano il lavoro e confondono la diagnosi.
    [switch]$RimuoviSitoIis
)

$ErrorActionPreference = "Stop"

$exe = Join-Path $Cartella "Duckburg.Ingestione.exe"
if (-not (Test-Path $exe)) { throw "Eseguibile non trovato: $exe" }

# ------------------------------------------------------------------ vecchio sito IIS
if ($RimuoviSitoIis) {
    Import-Module WebAdministration
    if (Test-Path "IIS:\Sites\Duckburg.Ingestione") {
        Remove-Website -Name "Duckburg.Ingestione"
        Write-Host "rimosso il sito IIS Duckburg.Ingestione" -ForegroundColor Green
    }
    if (Test-Path "IIS:\AppPools\DuckburgIngestione") {
        Remove-WebAppPool -Name "DuckburgIngestione"
        Write-Host "rimosso l'app pool DuckburgIngestione" -ForegroundColor Green
    }
}

# ------------------------------------------------------------------------- permessi
#
# Modify e non ReadAndExecute, anche se l'ingestione dal CMS legge soltanto.
# Il database del portale e' in modalita' WAL: per leggerlo SQLite apre anche il file
# -shm, e quello lo apre in scrittura. Con i soli permessi di lettura fallisce con
# "unable to open database file", che sembra un percorso sbagliato e non lo e'.
if (Test-Path $CmsAppData) {
    icacls $CmsAppData /grant "${Account}:(OI)(CI)M" | Out-Null
    Write-Host "permessi sul CMS concessi a $Account" -ForegroundColor Green
} else {
    Write-Host "ATTENZIONE: $CmsAppData non esiste. Avvia prima il portale." -ForegroundColor Yellow
}

# Il servizio scrive i propri log dove sta.
icacls $Cartella /grant "${Account}:(OI)(CI)M" | Out-Null

# ------------------------------------------------------------------------- servizio
$esistente = Get-Service -Name $Nome -ErrorAction SilentlyContinue
if ($esistente) {
    if ($esistente.Status -ne "Stopped") {
        Stop-Service -Name $Nome -Force
        Write-Host "servizio fermato" -ForegroundColor Yellow
    }
    sc.exe config $Nome binPath= "`"$exe`"" start= auto obj= "$Account" | Out-Null
    Write-Host "servizio riconfigurato" -ForegroundColor Green
} else {
    sc.exe create $Nome binPath= "`"$exe`"" start= auto obj= "$Account" DisplayName= "$Etichetta" | Out-Null
    Write-Host "servizio creato" -ForegroundColor Green
}

sc.exe description $Nome "Legge il CMS del Comune e pubblica l'istantanea sul corpus." | Out-Null

# Riavvio automatico: primo tentativo dopo 5 secondi, poi 15, poi ogni minuto.
# Il contatore si azzera dopo un giorno di funzionamento regolare.
sc.exe failure $Nome reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

Start-Service -Name $Nome
Write-Host "servizio avviato" -ForegroundColor Green

# ------------------------------------------------------------------------- verifica
Start-Sleep -Seconds 5
Get-Service -Name $Nome | Select-Object Name, Status, StartType | Format-Table -AutoSize

Write-Host "Stato dell'ingestione:" -ForegroundColor Cyan
try {
    $r = Invoke-RestMethod -Uri "http://127.0.0.1:5250/health" -TimeoutSec 10
    Write-Host "  stato: $($r.stato)"
    Write-Host "  $($r.messaggio)"
} catch {
    Write-Host "  non risponde ancora: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "  La prima esecuzione puo' richiedere qualche secondo. Riprova con:"
    Write-Host "    curl.exe -s http://127.0.0.1:5250/health"
}

Write-Host ""
Write-Host "Da qui in avanti non serve nessun intervento manuale:" -ForegroundColor Cyan
Write-Host "  il servizio parte al boot, ritenta finche' la prima ingestione non riesce,"
Write-Host "  poi rilegge il CMS all'intervallo configurato."
Write-Host ""
Write-Host "Per forzare un giro subito dopo una pubblicazione nel CMS:" -ForegroundColor Cyan
Write-Host "  Invoke-RestMethod -Method Post -Uri http://127.0.0.1:5250/esegui"
Write-Host ""
Write-Host "Log del servizio: Visualizzatore eventi, registro Applicazione, origine $Nome"
