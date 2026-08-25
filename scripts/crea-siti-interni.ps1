<#
    crea-siti-interni.ps1

    Crea in IIS i due siti che NON devono essere raggiungibili da Internet:
    il corpus e l'adattatore di ingestione.

    Il modo giusto di renderli interni non e' una regola sul firewall: e' legare il
    binding al solo indirizzo di loopback. Con IPAddress 127.0.0.1 il socket non viene
    mai aperto sull'interfaccia pubblica, quindi non c'e' nulla da bloccare. Una regola
    del firewall sarebbe una seconda serratura su una porta che non esiste.

    Eseguire come amministratore sul server IIS:
        .\crea-siti-interni.ps1

    E' idempotente: se un sito o un pool esiste gia', lo aggiorna invece di fallire.
#>

[CmdletBinding()]
param(
    [string]$Radice = "C:\inetpub\wwwroot\DuckBurgSmartCity",
    [string]$PoolCorpus = "ChattyDuckCorpus",
    [string]$PoolIngestione = "DuckburgIngestione"
)

$ErrorActionPreference = "Stop"
Import-Module WebAdministration

function CreaPool([string]$nome) {
    if (-not (Test-Path "IIS:\AppPools\$nome")) {
        New-WebAppPool -Name $nome | Out-Null
        Write-Host "  creato app pool $nome" -ForegroundColor Green
    } else {
        Write-Host "  app pool $nome gia' presente" -ForegroundColor Yellow
    }
    # "No Managed Code": ASP.NET Core non gira sul CLR di IIS, ci pensa il modulo ANCM.
    Set-ItemProperty "IIS:\AppPools\$nome" -Name managedRuntimeVersion -Value ""

    # Questi tre valori sono l'intera ragione per cui l'ingestione gira o non gira.
    #
    # Un servizio che lavora da solo, con un BackgroundService, non ha nessuno che lo
    # chiami: nel modello di IIS e' un caso fuori dall'ordinario, perche' IIS presume che
    # il lavoro nasca da una richiesta. Con le impostazioni di default l'applicazione
    # viene caricata alla prima richiesta, scaricata dopo venti minuti di silenzio, e
    # riavviata ogni ventinove ore. Per un sito che serve pagine e' ragionevole. Per un
    # temporizzatore significa che non parte mai, e se parte muore poco dopo.
    Set-ItemProperty "IIS:\AppPools\$nome" -Name startMode -Value "AlwaysRunning"
    Set-ItemProperty "IIS:\AppPools\$nome" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
    Set-ItemProperty "IIS:\AppPools\$nome" -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
}

function AbilitaPreload([string]$nome) {
    # startMode AlwaysRunning avvia il processo, non l'applicazione: quella la carica la
    # prima richiesta. preloadEnabled e' la parte che manca, ed e' quella che conta.
    try {
        Set-ItemProperty "IIS:\Sites\$nome" -Name applicationDefaults.preloadEnabled -Value $true -ErrorAction Stop
        Write-Host "  preload attivo su $nome" -ForegroundColor Green
    } catch {
        Write-Host "  ATTENZIONE: preload non impostabile su $nome." -ForegroundColor Red
        Write-Host "  Serve la funzionalita' Application Initialization di IIS:" -ForegroundColor Red
        Write-Host "    Enable-WindowsOptionalFeature -Online -FeatureName IIS-ApplicationInit -All" -ForegroundColor Red
        Write-Host "  Senza, l'ingestione non parte da sola: va svegliata con POST /esegui." -ForegroundColor Red
    }
}

function CreaSitoInterno([string]$nome, [string]$cartella, [int]$porta, [string]$pool) {
    $percorso = Join-Path $Radice $cartella
    if (-not (Test-Path $percorso)) { throw "Cartella non trovata: $percorso" }

    if (Test-Path "IIS:\Sites\$nome") {
        Write-Host "  sito $nome gia' presente: aggiorno percorso e binding" -ForegroundColor Yellow
        Set-ItemProperty "IIS:\Sites\$nome" -Name physicalPath -Value $percorso
        Set-ItemProperty "IIS:\Sites\$nome" -Name applicationPool -Value $pool
        Get-WebBinding -Name $nome | Remove-WebBinding
        New-WebBinding -Name $nome -Protocol http -IPAddress "127.0.0.1" -Port $porta -HostHeader ""
    } else {
        New-WebSite -Name $nome -PhysicalPath $percorso -ApplicationPool $pool `
                    -IPAddress "127.0.0.1" -Port $porta -HostHeader "" | Out-Null
        Write-Host "  creato sito $nome su 127.0.0.1:$porta" -ForegroundColor Green
    }
}

Write-Host "App pool" -ForegroundColor Cyan
CreaPool $PoolCorpus
CreaPool $PoolIngestione

Write-Host "Siti interni" -ForegroundColor Cyan
CreaSitoInterno "ChattyDuck.Corpus"   "ChattyDuck.Corpus"   5200 $PoolCorpus
CreaSitoInterno "Duckburg.Ingestione" "Duckburg.Ingestione" 5250 $PoolIngestione

Write-Host "Avvio automatico" -ForegroundColor Cyan
AbilitaPreload "ChattyDuck.Corpus"
AbilitaPreload "Duckburg.Ingestione"

Write-Host "Permessi" -ForegroundColor Cyan

# Il corpus scrive il proprio database.
$appData = Join-Path $Radice "ChattyDuck.Corpus\App_Data"
New-Item -ItemType Directory -Force $appData | Out-Null
icacls $appData /grant "IIS AppPool\${PoolCorpus}:(OI)(CI)M" | Out-Null
Write-Host "  scrittura su ChattyDuck.Corpus\App_Data per $PoolCorpus"

# L'ingestione legge il database del CMS, e non ci scrive mai.
$cms = Join-Path $Radice "Duckburg.Portal\App_Data"
if (Test-Path $cms) {
    icacls $cms /grant "IIS AppPool\${PoolIngestione}:(OI)(CI)RX" | Out-Null
    Write-Host "  lettura su Duckburg.Portal\App_Data per $PoolIngestione"
} else {
    Write-Host "  ATTENZIONE: $cms non esiste ancora." -ForegroundColor Yellow
    Write-Host "  Avvia prima il portale, che lo crea, poi rilancia questo script."
}

Write-Host ""
Write-Host "Binding risultanti:" -ForegroundColor Cyan
foreach ($s in "ChattyDuck.Corpus", "Duckburg.Ingestione") {
    $b = (Get-WebBinding -Name $s).bindingInformation
    $interno = $b -like "127.0.0.1:*"
    $colore = if ($interno) { "Green" } else { "Red" }
    Write-Host ("  {0,-22} {1}  {2}" -f $s, $b, $(if ($interno) { "interno" } else { "ESPOSTO: rifare il binding" })) -ForegroundColor $colore
}

Write-Host ""
Write-Host "Prova dalla macchina stessa:" -ForegroundColor Cyan
Write-Host "  curl.exe -s http://127.0.0.1:5200/health"
Write-Host "  curl.exe -s http://127.0.0.1:5250/health"
Write-Host ""
Write-Host "E dall'esterno, per confermare che non rispondano:" -ForegroundColor Cyan
Write-Host "  Test-NetConnection <ip-pubblico> -Port 5200    # deve fallire"
Write-Host ""
Write-Host "Nota: 503 con stato 'allineamento' o 'avvio' e' normale nei primi secondi." -ForegroundColor Yellow
Write-Host "I servizi si allineano da soli, in qualunque ordine siano stati avviati."
Write-Host ""
Write-Host "Se il server MCP resta in 'allineamento' con ultimo_errore 404, il corpus e'" -ForegroundColor Yellow
Write-Host "vivo ma vuoto: e' l'ingestione a non aver mai pubblicato. Sveglia e verifica:" -ForegroundColor Yellow
Write-Host "  curl.exe -X POST http://127.0.0.1:5250/esegui"
Write-Host "  curl.exe -s http://127.0.0.1:5250/health"
