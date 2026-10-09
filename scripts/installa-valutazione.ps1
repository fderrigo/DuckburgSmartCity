<#
    installa-valutazione.ps1

    Mette in piedi Duckburg.Valutazione su un server IIS, dalla cartella pubblicata fino
    al sito raggiungibile: validatore ufficiale, app pool, sito, binding, permessi.

    Valutazione non e' un sito .NET qualunque: lancia `node` sul validatore ufficiale
    (italia/pa-website-validator-ng), che a sua volta apre un Chrome headless. Tre cose
    quindi devono stare sul server e vedersi fra loro:
      1. la cartella pubblicata (dotnet publish), gia' copiata sotto $Radice
      2. il validatore, clonato e compilato dentro tool/ della cartella pubblicata
      3. il Chrome di Puppeteer, in una cache che l'identita' dell'app pool possa leggere.
         Per default Puppeteer lo mette nel profilo di chi lancia l'installazione, e
         l'app pool non ci arriva: per questo la cache sta dentro tool/.puppeteer e il
         percorso viene passato all'app pool come variabile d'ambiente.

    Prerequisiti sul server: IIS con ASP.NET Core Hosting Bundle, Node.js 18+ e git nel
    PATH di sistema. Il certificato TLS per l'hostname deve gia' stare in
    Cert:\LocalMachine\My (win-acme, Certify, o importato a mano): lo script lo cerca
    per nome, anche wildcard, oppure lo prendi con -Thumbprint.

    Eseguire come amministratore:
        .\installa-valutazione.ps1
        .\installa-valutazione.ps1 -SitoPortale "https://paperopoli.derrigo.it" -Thumbprint "ABC..."

    E' idempotente: rilanciato aggiorna invece di fallire. Il clone del validatore lo
    salta se c'e' gia', -RicompilaValidatore lo ricompila comunque.
#>

[CmdletBinding()]
param(
    [string]$Radice = "C:\inetpub\wwwroot\DuckBurgSmartCity",
    [string]$Cartella = "Duckburg.Valutazione",
    [string]$Hostname = "valutazione.paperopoli.derrigo.it",
    [string]$NomeSito = "Duckburg.Valutazione",
    [string]$Pool = "DuckburgValutazione",
    [string]$SitoPortale = "https://paperopoli.derrigo.it",
    [string]$Thumbprint = "",
    [string]$VersioneChrome = "129.0.6668.89",
    [switch]$RicompilaValidatore
)

$ErrorActionPreference = "Stop"

function Passo([string]$testo) { Write-Host ""; Write-Host ">> $testo" -ForegroundColor Cyan }
function Ok([string]$testo)    { Write-Host "  $testo" -ForegroundColor Green }
function Nota([string]$testo)  { Write-Host "  $testo" -ForegroundColor Yellow }

function Esegui([string]$cmd, [string[]]$argomenti) {
    # I .cmd di npm vanno lanciati per nome completo, altrimenti PowerShell prova
    # l'eseguibile sbagliato. L'exit code va controllato a mano: $ErrorActionPreference
    # non ferma i programmi nativi.
    & $cmd @argomenti
    if ($LASTEXITCODE -ne 0) { throw "$cmd $($argomenti -join ' ') fallito con codice $LASTEXITCODE" }
}

# ---------------------------------------------------------------- controlli preliminari

Passo "Controlli preliminari"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Serve una console amministratore."
}

$app = Join-Path $Radice $Cartella
if (-not (Test-Path (Join-Path $app "Duckburg.Valutazione.dll"))) {
    throw "In $app manca Duckburg.Valutazione.dll: copia prima l'output di 'dotnet publish -c Release'."
}
if (-not (Test-Path (Join-Path $app "web.config"))) {
    throw "In $app manca web.config: la cartella non e' un publish per IIS."
}
Ok "cartella pubblicata: $app"

foreach ($exe in "node", "npm.cmd", "npx.cmd", "git") {
    if (-not (Get-Command $exe -ErrorAction SilentlyContinue)) {
        throw "'$exe' non trovato nel PATH. Installa Node.js 18+ e git, poi riapri la console."
    }
}
$nodeDir = Split-Path (Get-Command node).Source
$pathMacchina = [Environment]::GetEnvironmentVariable("Path", "Machine")
if ($pathMacchina -notlike "*$nodeDir*") {
    Nota "node sta in $nodeDir ma non nel PATH di sistema: l'app pool non lo troverebbe."
    Nota "Lo aggiungo al PATH di sistema."
    [Environment]::SetEnvironmentVariable("Path", "$pathMacchina;$nodeDir", "Machine")
}
Ok "node $(node --version), git presente"

Import-Module WebAdministration

# ---------------------------------------------------------------- validatore ufficiale

Passo "Validatore ufficiale (pa-website-validator-ng)"

$toolDir = Join-Path $app "tool"
$repoDir = Join-Path $toolDir "pa-website-validator-ng"
$cacheDir = Join-Path $toolDir ".puppeteer"
New-Item -ItemType Directory -Force $toolDir, $cacheDir | Out-Null

# La cartella puo' arrivare copiata dalla macchina di sviluppo invece che clonata. Va
# bene solo se e' completa: senza src/ tsc compila i soli .d.ts di lighthouse, esce
# con 0 e non produce nulla. Una copia incompleta si butta e si clona.
$completo = (Test-Path (Join-Path $repoDir "package.json")) -and
            (Test-Path (Join-Path $repoDir "tsconfig.json")) -and
            (Test-Path (Join-Path $repoDir "src\index.ts"))
if ($completo) {
    Nota "validatore gia' presente in $repoDir, salto il clone"
} else {
    if (Test-Path $repoDir) {
        Nota "in $repoDir mancano src\index.ts o tsconfig.json: copia incompleta, la rimuovo e clono"
        Remove-Item -Recurse -Force $repoDir
    }
    Esegui git @("clone", "--depth", "1", "https://github.com/italia/pa-website-validator-ng", $repoDir)
    Ok "clonato in $repoDir"
}

$compilato = Test-Path (Join-Path $repoDir "dist\index.js")
if ($RicompilaValidatore -or -not $compilato) {
    Push-Location $repoDir
    try {
        # Lo script 'prepare' del pacchetto usa rm -rf e fallisce su Windows: si fanno i
        # suoi passi a mano. La versione di Chrome deve combaciare con la major di
        # Puppeteer del pacchetto (v23 -> 129), non con quella che suggerirebbe npm.
        $env:PUPPETEER_CACHE_DIR = $cacheDir
        # --include=dev: tsc e copyfiles sono devDependencies, e un NODE_ENV=production
        # sul server le farebbe saltare in silenzio.
        Esegui npm.cmd @("install", "--ignore-scripts", "--include=dev")

        # I binari locali, non npx: se typescript manca, `npx tsc` scarica dal registro
        # un pacchetto omonimo che stampa un avviso ed esce con 0 senza compilare nulla.
        $bin = Join-Path $repoDir "node_modules\.bin"
        foreach ($b in "tsc.cmd", "copyfiles.cmd", "puppeteer.cmd") {
            if (-not (Test-Path (Join-Path $bin $b))) {
                throw "manca $bin\$b dopo npm install: controlla l'output di npm qui sopra."
            }
        }
        Esegui (Join-Path $bin "puppeteer.cmd") @("browsers", "install", "chrome-headless-shell@$VersioneChrome")
        Esegui (Join-Path $bin "puppeteer.cmd") @("browsers", "install", "chrome@$VersioneChrome")
        Esegui (Join-Path $bin "tsc.cmd") @()
        Esegui (Join-Path $bin "copyfiles.cmd") @("-u", "1", "src/**/*.{ejs,json,scss,css,map}", "dist/")
        if (-not (Test-Path (Join-Path $repoDir "dist\index.js"))) {
            throw "tsc e' terminato senza produrre dist\index.js: rilancia 'node_modules\.bin\tsc.cmd' a mano in $repoDir e leggi gli errori."
        }
        Esegui node @("dist", "--version")
        Ok "validatore compilato"
    } finally {
        Pop-Location
    }
} else {
    Nota "validatore gia' compilato (dist\index.js presente), usa -RicompilaValidatore per rifarlo"
}

# ---------------------------------------------------------------- app pool

Passo "App pool $Pool"

if (-not (Test-Path "IIS:\AppPools\$Pool")) {
    New-WebAppPool -Name $Pool | Out-Null
    Ok "creato"
} else {
    Nota "gia' presente, aggiorno"
}
Set-ItemProperty "IIS:\AppPools\$Pool" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$Pool" -Name startMode -Value "AlwaysRunning"
Set-ItemProperty "IIS:\AppPools\$Pool" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
# Una scansione dura minuti: un riciclo a meta' la butterebbe via.
Set-ItemProperty "IIS:\AppPools\$Pool" -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)

# Variabili d'ambiente del processo: sovrascrivono appsettings.json. Urls non serve
# sotto IIS, decide il modulo ANCM.
$filtro = "system.applicationHost/applicationPools/add[@name='$Pool']/environmentVariables"
$variabili = @{
    "ASPNETCORE_ENVIRONMENT"  = "Production"
    "Siti__Portale"           = $SitoPortale
    "PUPPETEER_CACHE_DIR"     = $cacheDir
    "Valutazione__ToolDir"    = $repoDir
    "Valutazione__ReportsDir" = (Join-Path $app "reports")
}
foreach ($nome in $variabili.Keys) {
    Remove-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter $filtro -Name "." `
        -AtElement @{ name = $nome } -ErrorAction SilentlyContinue
    Add-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Filter $filtro -Name "." `
        -Value @{ name = $nome; value = $variabili[$nome] }
}
Ok "variabili d'ambiente: $($variabili.Keys -join ', ')"

# ---------------------------------------------------------------- sito e binding

Passo "Sito $NomeSito su $Hostname"

if (Test-Path "IIS:\Sites\$NomeSito") {
    Nota "gia' presente, aggiorno percorso, pool e binding"
    Set-ItemProperty "IIS:\Sites\$NomeSito" -Name physicalPath -Value $app
    Set-ItemProperty "IIS:\Sites\$NomeSito" -Name applicationPool -Value $Pool
    Get-WebBinding -Name $NomeSito | Remove-WebBinding
    New-WebBinding -Name $NomeSito -Protocol http -Port 80 -HostHeader $Hostname
} else {
    New-WebSite -Name $NomeSito -PhysicalPath $app -ApplicationPool $Pool -Port 80 -HostHeader $Hostname | Out-Null
    Ok "creato"
}
# SslFlags 1 = SNI: sulla stessa 443 convivono piu' hostname con certificati diversi.
New-WebBinding -Name $NomeSito -Protocol https -Port 443 -HostHeader $Hostname -SslFlags 1
Ok "binding http :80 e https :443 (SNI) per $Hostname"

try {
    Set-ItemProperty "IIS:\Sites\$NomeSito" -Name applicationDefaults.preloadEnabled -Value $true -ErrorAction Stop
} catch {
    Nota "preload non impostabile: manca Application Initialization, non e' bloccante"
}

# ---------------------------------------------------------------- certificato

Passo "Certificato TLS"

$cert = $null
if ($Thumbprint) {
    $cert = Get-ChildItem "Cert:\LocalMachine\My\$Thumbprint" -ErrorAction SilentlyContinue
    if (-not $cert) { throw "Nessun certificato con thumbprint $Thumbprint in Cert:\LocalMachine\My" }
} else {
    # Cerco un certificato valido che copra l'hostname, esatto o wildcard del dominio padre.
    $wildcard = "*." + ($Hostname -replace '^[^.]+\.', '')
    $cert = Get-ChildItem Cert:\LocalMachine\My |
        Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
        Where-Object {
            $nomi = @($_.DnsNameList | ForEach-Object { $_.Unicode })
            ($nomi -contains $Hostname) -or ($nomi -contains $wildcard)
        } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}

if ($cert) {
    $binding = Get-WebBinding -Name $NomeSito -Protocol https -HostHeader $Hostname
    $binding.AddSslCertificate($cert.Thumbprint, "My")
    Ok "assegnato: $($cert.Subject), scade $($cert.NotAfter.ToShortDateString())"
} else {
    Nota "nessun certificato per $Hostname in Cert:\LocalMachine\My."
    Nota "Senza certificato la 443 resetta la connessione. Ottienilo con win-acme:"
    Nota "    wacs.exe --target iis --siteid <id> --host $Hostname"
    Nota "oppure importa un certificato e rilancia con -Thumbprint <thumbprint>."
}

# ---------------------------------------------------------------- permessi

Passo "Permessi per IIS AppPool\$Pool"

$identita = "IIS AppPool\$Pool"
$reportsDir = Join-Path $app "reports"
New-Item -ItemType Directory -Force $reportsDir | Out-Null

# Lettura ed esecuzione su tutta l'app: dll, web.config, validatore, node_modules.
icacls $app /grant "${identita}:(OI)(CI)RX" /T /Q | Out-Null
# Scrittura dove serve davvero: i report generati e la cache di Chrome, che scrive
# crash dump e profili accanto al binario.
icacls $reportsDir /grant "${identita}:(OI)(CI)M" /Q | Out-Null
icacls $cacheDir  /grant "${identita}:(OI)(CI)M" /Q | Out-Null
Ok "RX su $app, M su reports e cache Puppeteer"

# ---------------------------------------------------------------- avvio e verifica

Passo "Avvio"

# Il PATH di sistema modificato lo vede solo un w3wp nuovo: riciclo il pool.
Restart-WebAppPool -Name $Pool
Start-Website -Name $NomeSito -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

# Chiedo la home a IIS in locale con l'Host header giusto: separa un problema
# dell'app da un problema di DNS o firewall.
try {
    $req = [Net.HttpWebRequest]::Create("http://127.0.0.1/")
    $req.Host = $Hostname
    $req.Timeout = 30000
    $resp = $req.GetResponse()
    Ok "http://$Hostname risponde $([int]$resp.StatusCode) in locale"
    $resp.Close()
} catch {
    Nota "la home non risponde in locale: $($_.Exception.Message)"
    Nota "guarda il log ANCM in $app\logs (stdoutLogEnabled in web.config) e l'Event Viewer."
}

Write-Host ""
Write-Host "Fatto. Da fuori: curl -I https://$Hostname/" -ForegroundColor Cyan
if (-not $cert) { Write-Host "Ricorda il certificato: finche' manca, https non funziona." -ForegroundColor Yellow }
