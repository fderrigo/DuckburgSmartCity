<#
    permessi-dati.ps1

    Concede la scrittura sulle cartelle dei dati alle applicazioni ospitate in IIS.

    Va rilanciato dopo ogni pubblicazione che ricopia le cartelle delle applicazioni.
    Non e' una dimenticanza di chi ha scritto lo script: copiare una cartella sopra
    un'altra sostituisce anche le sue liste di controllo, quindi i permessi concessi a
    mano spariscono insieme ai file vecchi. E' la ragione per cui lo stesso errore di
    accesso negato torna a ogni deploy.

    Il modo per non ripetere questo passo e' spostare i dati fuori dall'albero delle
    applicazioni: vedi -FuoriDallAlbero in fondo.

    Eseguire come amministratore:
        .\permessi-dati.ps1
#>

[CmdletBinding()]
param(
    [string]$Radice = "C:\inetpub\wwwroot\DuckBurgSmartCity",
    # IIS_IUSRS comprende tutte le identita' degli app pool. Piu' comodo di una
    # concessione per pool, e su una macchina con un solo progetto la differenza e'
    # teorica. Per separare davvero, passa "IIS AppPool\<NomeDelPool>".
    [string]$Identita = "IIS_IUSRS"
)

$ErrorActionPreference = "Stop"

# Cartelle che le applicazioni devono poter scrivere, con il motivo.
$cartelle = @(
    @{ Percorso = "Duckburg.Portal\App_Data";      Motivo = "database del CMS" },
    @{ Percorso = "Duckburg.Portal\wwwroot\media"; Motivo = "file caricati dalla redazione" },
    @{ Percorso = "ChattyDuck.Corpus\App_Data";    Motivo = "database del corpus" }
)

foreach ($c in $cartelle) {
    $percorso = Join-Path $Radice $c.Percorso
    if (-not (Test-Path $percorso)) {
        New-Item -ItemType Directory -Force $percorso | Out-Null
        Write-Host "creata  $($c.Percorso)" -ForegroundColor Yellow
    }
    icacls $percorso /grant "${Identita}:(OI)(CI)M" | Out-Null
    Write-Host "scrittura per $Identita su $($c.Percorso)  ($($c.Motivo))" -ForegroundColor Green
}

# Verifica vera: non che il comando sia riuscito, ma che si riesca a scrivere.
Write-Host ""
Write-Host "Verifica:" -ForegroundColor Cyan
foreach ($c in $cartelle) {
    $percorso = Join-Path $Radice $c.Percorso
    $prova = Join-Path $percorso ".prova-permessi"
    try {
        [System.IO.File]::WriteAllText($prova, "")
        Remove-Item $prova -Force
        Write-Host "  scrivibile   $($c.Percorso)" -ForegroundColor Green
    } catch {
        Write-Host "  NON scrivibile $($c.Percorso): $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Nota sui file caricati dalla redazione:" -ForegroundColor Cyan
Write-Host "  wwwroot\media sta dentro la cartella dell'applicazione. Una pubblicazione che"
Write-Host "  ricopia quella cartella puo' cancellare le immagini caricate dal CMS. Copia i"
Write-Host "  file nuovi dentro l'albero esistente invece di sostituirlo, oppure sposta i"
Write-Host "  dati fuori come descritto sotto."
Write-Host ""
Write-Host "Per non rifare questo passo a ogni deploy:" -ForegroundColor Cyan
Write-Host "  sposta i dati fuori dall'albero delle applicazioni e puntaci la configurazione."
Write-Host "  Le cartelle fuori non vengono toccate dalle pubblicazioni, quindi i permessi"
Write-Host "  restano. Per esempio:"
Write-Host ""
Write-Host '    New-Item -ItemType Directory -Force C:\ProgramData\DuckburgSmartCity\Portal'
Write-Host '    New-Item -ItemType Directory -Force C:\ProgramData\DuckburgSmartCity\Corpus'
Write-Host '    icacls C:\ProgramData\DuckburgSmartCity /grant "IIS_IUSRS:(OI)(CI)M"'
Write-Host ""
Write-Host "  e poi, nelle variabili d'ambiente dei siti:"
Write-Host '    Cms__Database__ConnectionString    = Data Source=C:\ProgramData\DuckburgSmartCity\Portal\paperopoli-cms.db'
Write-Host '    Corpus__Database__ConnectionString = Data Source=C:\ProgramData\DuckburgSmartCity\Corpus\corpus.db'
