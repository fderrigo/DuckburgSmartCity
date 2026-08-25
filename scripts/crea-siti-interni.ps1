<#
    crea-siti-interni.ps1

    Crea in IIS il corpus, che non deve essere raggiungibile da Internet.

    Il modo giusto di renderlo interno non e' una regola sul firewall: e' legare il
    binding al solo indirizzo di loopback. Con IPAddress 127.0.0.1 il socket non viene
    mai aperto sull'interfaccia pubblica, quindi non c'e' nulla da bloccare. Una regola
    del firewall sarebbe una seconda serratura su una porta che non esiste.

    L'ingestione non e' qui, e non e' una dimenticanza: non e' un sito. Si installa con
    installa-servizio-ingestione.ps1, che ne fa un servizio Windows. La ragione sta in
    quello script.

    Eseguire come amministratore sul server IIS:
        .\crea-siti-interni.ps1

    E' idempotente: se il sito o il pool esistono gia', li aggiorna invece di fallire.
#>

[CmdletBinding()]
param(
    [string]$Radice = "C:\inetpub\wwwroot\DuckBurgSmartCity",
    [string]$PoolCorpus = "ChattyDuckCorpus"
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
    Set-ItemProperty "IIS:\AppPools\$nome" -Name startMode -Value "AlwaysRunning"

    # Il corpus riceve richieste, quindi si sveglierebbe comunque. Ma la prima chiamata
    # dopo un riciclo pagherebbe l'avvio a freddo, e chi la fa e' il server MCP mentre
    # un cittadino aspetta una risposta.
    Set-ItemProperty "IIS:\AppPools\$nome" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
    Set-ItemProperty "IIS:\AppPools\$nome" -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
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

    # startMode AlwaysRunning avvia il processo, non l'applicazione: quella la carica la
    # prima richiesta, o il preload. Sono due cose diverse e la seconda si dimentica.
    try {
        Set-ItemProperty "IIS:\Sites\$nome" -Name applicationDefaults.preloadEnabled -Value $true -ErrorAction Stop
        Write-Host "  preload attivo su $nome" -ForegroundColor Green
    } catch {
        Write-Host "  preload non impostabile: manca Application Initialization." -ForegroundColor Yellow
        Write-Host "    Enable-WindowsOptionalFeature -Online -FeatureName IIS-ApplicationInit -All" -ForegroundColor Yellow
    }
}

Write-Host "App pool" -ForegroundColor Cyan
CreaPool $PoolCorpus

Write-Host "Sito interno" -ForegroundColor Cyan
CreaSitoInterno "ChattyDuck.Corpus" "ChattyDuck.Corpus" 5200 $PoolCorpus

Write-Host "Permessi" -ForegroundColor Cyan
$appData = Join-Path $Radice "ChattyDuck.Corpus\App_Data"
New-Item -ItemType Directory -Force $appData | Out-Null
icacls $appData /grant "IIS AppPool\${PoolCorpus}:(OI)(CI)M" | Out-Null
Write-Host "  scrittura su ChattyDuck.Corpus\App_Data per $PoolCorpus"

# Un residuo del deploy precedente, quando l'ingestione era un sito. Se e' rimasto,
# convive con il servizio senza rompere nulla, ma pubblica anche lui: due sorgenti per
# lo stesso corpus rendono illeggibile qualunque diagnosi.
if (Test-Path "IIS:\Sites\Duckburg.Ingestione") {
    Write-Host ""
    Write-Host "ATTENZIONE: esiste ancora il sito IIS Duckburg.Ingestione." -ForegroundColor Yellow
    Write-Host "L'ingestione ora e' un servizio Windows. Rimuovi il sito con:" -ForegroundColor Yellow
    Write-Host "  .\installa-servizio-ingestione.ps1 -RimuoviSitoIis" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Binding risultante:" -ForegroundColor Cyan
$b = (Get-WebBinding -Name "ChattyDuck.Corpus").bindingInformation
$interno = $b -like "127.0.0.1:*"
Write-Host ("  {0,-22} {1}  {2}" -f "ChattyDuck.Corpus", $b,
    $(if ($interno) { "interno" } else { "ESPOSTO: rifare il binding" })) `
    -ForegroundColor $(if ($interno) { "Green" } else { "Red" })

Write-Host ""
Write-Host "Prova dalla macchina stessa:" -ForegroundColor Cyan
Write-Host "  curl.exe -s http://127.0.0.1:5200/health"
Write-Host ""
Write-Host "E dall'esterno, per confermare che non risponda:" -ForegroundColor Cyan
Write-Host "  Test-NetConnection <ip-pubblico> -Port 5200    # deve fallire"
Write-Host ""
Write-Host "Poi installa l'ingestione, che non e' un sito:" -ForegroundColor Cyan
Write-Host "  .\installa-servizio-ingestione.ps1"
