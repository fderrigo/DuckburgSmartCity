<#
    analisi-carico-server.ps1

    Fotografia del carico e dei prerequisiti di un server Windows, per decidere
    se puo' ospitare Docker senza penalizzare i siti gia' in produzione.

    SOLA LETTURA. Non installa, non abilita feature, non tocca IIS, non modifica
    il registro. Le uniche scritture sono il file di report sul Desktop.

    Uso (PowerShell come amministratore, sul server):
        .\analisi-carico-server.ps1
        .\analisi-carico-server.ps1 -Minuti 15     # finestra piu' lunga

    Senza privilegi di amministratore funziona lo stesso, ma alcune sezioni
    (IIS, feature di Windows) restano vuote.

    Fai partire lo script in un momento rappresentativo: una finestra di 5 minuti
    alle 3 di notte non dice nulla sul carico di picco.
#>

[CmdletBinding()]
param(
    [int]$Minuti = 5,
    [int]$IntervalloSecondi = 10
)

$ErrorActionPreference = "Continue"
$ProgressPreference = "SilentlyContinue"

$report = New-Object System.Collections.Generic.List[string]

function Sezione($titolo) {
    $report.Add("")
    $report.Add("=" * 72)
    $report.Add("  $titolo")
    $report.Add("=" * 72)
}

function Riga($etichetta, $valore) {
    if ($null -eq $valore -or "$valore" -eq "") { $valore = "n/d" }
    $report.Add(("{0,-40}{1}" -f "$etichetta :", $valore))
}

function Prova($blocco, $etichettaErrore) {
    try { & $blocco } catch { $report.Add("  [!] $etichettaErrore : $($_.Exception.Message)") }
}

$admin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
         ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

<#
    Le classi Win32_PerfFormattedData_* dipendono dalla libreria dei contatori di
    prestazione, che su molti server non e' popolata o e' corrotta. Non e' un sintomo
    di malfunzionamento della macchina, ma rende inutilizzabile la via principale.
    Entrambe le funzioni qui sotto provano il contatore e ripiegano su fonti sempre
    disponibili, annotando quale via ha funzionato: serve a leggere il report senza
    scambiare una misura per un'altra.
#>
$script:MetodoRam = $null
$script:MetodoCpu = $null

<#
    Memoria DISPONIBILE, non "libera". FreePhysicalMemory esclude la cache in
    standby, che Windows cede subito a chi ne ha bisogno: su un server sano puo'
    mostrare percentuali allarmanti che non corrispondono a nessun problema.
#>
function RamDisponibileGB {
    if ($script:MetodoRam -ne "os") {
        try {
            $m = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory -ErrorAction Stop
            if ($null -ne $m -and $null -ne $m.AvailableMBytes) {
                $script:MetodoRam = "perf"
                return [double]$m.AvailableMBytes / 1024
            }
        } catch { }
    }
    $script:MetodoRam = "os"
    return [double](Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
}

<#
    CPU in percentuale. Tre vie in ordine di preferenza:
      perf   contatore formattato, media sull'intervallo, la piu' precisa
      load   Win32_Processor.LoadPercentage, media su circa un secondo
      raw    differenza fra due letture dei contatori grezzi, sempre disponibile
    Restituisce $null se nessuna via risponde.
#>
function CpuPercento {
    if ($script:MetodoCpu -notin @("load", "raw")) {
        try {
            $c = Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'" -ErrorAction Stop
            if ($null -ne $c -and $null -ne $c.PercentProcessorTime) {
                $script:MetodoCpu = "perf"
                return [double]$c.PercentProcessorTime
            }
        } catch { }
    }

    if ($script:MetodoCpu -ne "raw") {
        try {
            $l = Get-CimInstance Win32_Processor -ErrorAction Stop |
                 Measure-Object -Property LoadPercentage -Average
            if ($null -ne $l -and $null -ne $l.Average) {
                $script:MetodoCpu = "load"
                return [double]$l.Average
            }
        } catch { }
    }

    try {
        $a = Get-CimInstance Win32_PerfRawData_PerfOS_Processor -Filter "Name='_Total'" -ErrorAction Stop
        Start-Sleep -Milliseconds 500
        $b = Get-CimInstance Win32_PerfRawData_PerfOS_Processor -Filter "Name='_Total'" -ErrorAction Stop
        $dIdle = [double]$b.PercentIdleTime - [double]$a.PercentIdleTime
        $dTime = [double]$b.TimeStamp_Sys100NS - [double]$a.TimeStamp_Sys100NS
        if ($dTime -gt 0) {
            $script:MetodoCpu = "raw"
            $uso = 100 - (($dIdle / $dTime) * 100)
            if ($uso -lt 0) { $uso = 0 }
            if ($uso -gt 100) { $uso = 100 }
            return [double]$uso
        }
    } catch { }

    return $null
}

$report.Add("ANALISI CARICO SERVER - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$report.Add("Esecuzione come amministratore: $admin")
$report.Add("Finestra di campionamento richiesta: $Minuti minuti ogni $IntervalloSecondi secondi")

# ---------------------------------------------------------------- 1. Macchina
Sezione "1. MACCHINA"
Prova {
    $os  = Get-CimInstance Win32_OperatingSystem
    $cs  = Get-CimInstance Win32_ComputerSystem
    $cpu = @(Get-CimInstance Win32_Processor)

    Riga "Nome"              $cs.Name
    Riga "Sistema operativo" "$($os.Caption) build $($os.BuildNumber)"
    Riga "Ultimo avvio"      $os.LastBootUpTime
    Riga "Uptime"            ("{0:N1} giorni" -f ((Get-Date) - $os.LastBootUpTime).TotalDays)
    Riga "Produttore / modello" "$($cs.Manufacturer) / $($cs.Model)"
    Riga "CPU"               $cpu[0].Name
    $coreFisici = ($cpu | Measure-Object -Property NumberOfCores -Sum).Sum
    Riga "Socket / core / logici" "$($cpu.Count) / $coreFisici / $($cs.NumberOfLogicalProcessors)"
    Riga "RAM totale"        ("{0:N1} GB" -f ($cs.TotalPhysicalMemory / 1GB))
    Riga "Hypervisor presente" $cs.HypervisorPresent
} "macchina"

# ------------------------------------------------------- 2. Virtualizzazione
Sezione "2. PREREQUISITI DI VIRTUALIZZAZIONE (per Docker)"
$report.Add("Docker su Windows con container Linux richiede Hyper-V o WSL2.")
$report.Add("Se questa macchina e' gia' una VM, serve la virtualizzazione annidata,")
$report.Add("che molti provider non abilitano.")
$report.Add("")

Prova {
    $cs = Get-CimInstance Win32_ComputerSystem
    $eVm = $cs.Model -match "Virtual|VMware|KVM|Xen|Droplet" -or $cs.Manufacturer -match "VMware|Microsoft Corporation|QEMU|Xen|innotek"
    Riga "Sembra una VM" $eVm
    Riga "HypervisorPresent" $cs.HypervisorPresent
    if ($cs.HypervisorPresent -and -not $eVm) {
        $report.Add("  Nota: hypervisor presente su hardware fisico, di solito significa Hyper-V gia' attivo.")
    }
    if ($cs.HypervisorPresent -and $eVm) {
        $report.Add("  Nota: la macchina gira sotto un hypervisor. Per Docker serve che il provider")
        $report.Add("        abbia abilitato la virtualizzazione annidata. Da verificare con lui.")
    }
} "virtualizzazione"

Prova {
    $proc = Get-CimInstance Win32_Processor | Select-Object -First 1
    $cs2  = Get-CimInstance Win32_ComputerSystem
    Riga "VirtualizationFirmwareEnabled" $proc.VirtualizationFirmwareEnabled
    Riga "SecondLevelAddressTranslation" $proc.SecondLevelAddressTranslationExtensions
    Riga "VMMonitorModeExtensions"       $proc.VMMonitorModeExtensions
    if ($cs2.HypervisorPresent) {
        $report.Add("  ATTENZIONE: con un hypervisor gia' attivo questi tre valori risultano False")
        $report.Add("  anche su hardware che supporta tutto, perche' il sistema li legge da dentro")
        $report.Add("  una partizione. Qui non significano 'Docker non puo' girare'. Il valore che")
        $report.Add("  conta in quel caso e' HypervisorPresent = True.")
    }
} "capacita' CPU"

if ($admin) {
    Prova {
        Import-Module ServerManager -ErrorAction Stop
        $feature = Get-WindowsFeature -Name Hyper-V, Containers -ErrorAction Stop
        foreach ($f in $feature) { Riga "Feature $($f.Name)" $f.InstallState }
    } "feature Windows (Hyper-V, Containers)"
} else {
    $report.Add("  [salto le feature di Windows: servono privilegi di amministratore]")
}

Prova {
    $wsl = Get-Command wsl.exe -ErrorAction SilentlyContinue
    if ($wsl) {
        Riga "WSL installato" "si"
        # wsl.exe scrive in UTF-16: senza questo scambio l'output esce con uno
        # spazio fra ogni carattere. La codifica viene ripristinata subito dopo.
        $encPrec = [Console]::OutputEncoding
        try {
            [Console]::OutputEncoding = [System.Text.Encoding]::Unicode
            $out = (& wsl.exe --status 2>&1 | Out-String)
        } finally {
            [Console]::OutputEncoding = $encPrec
        }
        $out = ($out -replace "`0", "").Trim()
        foreach ($linea in ($out -split "`r?`n")) {
            if ($linea.Trim()) { $report.Add("  $($linea.Trim())") }
        }
    } else {
        Riga "WSL installato" "no"
    }
} "WSL"

# ------------------------------------------------------------- 3. Memoria ora
Sezione "3. MEMORIA, ISTANTANEA"
Prova {
    $os = Get-CimInstance Win32_OperatingSystem
    $totGB  = $os.TotalVisibleMemorySize / 1MB
    $dispGB = RamDisponibileGB
    Riga "RAM totale"       ("{0:N1} GB" -f $totGB)
    Riga "RAM disponibile"  ("{0:N1} GB ({1:N0}%)" -f $dispGB, ($dispGB / $totGB * 100))
    Riga "RAM impegnata"    ("{0:N1} GB" -f ($totGB - $dispGB))
    Riga "RAM libera (grezza)" ("{0:N1} GB" -f ($os.FreePhysicalMemory / 1MB))

    $pf = Get-CimInstance Win32_PageFileUsage
    if ($pf) {
        Riga "File di paging" ("{0} - allocato {1:N0} MB, picco {2:N0} MB" -f $pf.Name, $pf.AllocatedBaseSize, $pf.PeakUsage)
    } else {
        Riga "File di paging" "gestito dal sistema o assente"
    }
} "memoria"

# ---------------------------------------------------------------- 4. Dischi
Sezione "4. DISCHI"
Prova {
    Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" | ForEach-Object {
        $libero = $_.FreeSpace / 1GB
        $tot    = $_.Size / 1GB
        $pct    = if ($tot -gt 0) { $libero / $tot * 100 } else { 0 }
        Riga "Volume $($_.DeviceID)" ("{0:N1} GB liberi su {1:N1} GB ({2:N0}% libero)" -f $libero, $tot, $pct)
    }
    $report.Add("")
    $report.Add("Riferimento: le immagini della federazione piu' i layer occupano circa 3-4 GB,")
    $report.Add("piu' lo spazio per WSL2 o Hyper-V. Considera 10 GB liberi come minimo prudente.")
} "dischi"

# ------------------------------------------------------------------- 5. IIS
Sezione "5. IIS: SITI, APP POOL, PROCESSI"
if ($admin) {
    Prova {
        Import-Module WebAdministration -ErrorAction Stop
        $siti = @(Get-ChildItem IIS:\Sites -ErrorAction Stop)
        $pool = @(Get-ChildItem IIS:\AppPools -ErrorAction Stop)
        Riga "Siti configurati"   $siti.Count
        Riga "App pool"           $pool.Count
        Riga "Siti avviati"       (@($siti | Where-Object { $_.State -eq 'Started' }).Count)
        $report.Add("")
        $report.Add("  Siti:")
        foreach ($s in $siti) {
            $binding = ($s.Bindings.Collection | ForEach-Object { $_.bindingInformation }) -join " ; "
            $report.Add(("    {0,-30} {1,-9} pool={2,-22} {3}" -f $s.Name, $s.State, $s.ApplicationPool, $binding))
        }
    } "IIS (modulo WebAdministration)"

    Prova {
        $w3 = @(Get-CimInstance Win32_Process -Filter "Name='w3wp.exe'")
        $report.Add("")
        $report.Add("  Worker process attivi: $($w3.Count)")
        if ($w3.Count -gt 0) {
            $report.Add(("    {0,-24} {1,-8} {2,-12} {3}" -f "APP POOL", "PID", "RAM (MB)", "AVVIATO"))
            foreach ($p in $w3) {
                $nome = "?"
                if ($p.CommandLine -match '-ap\s+"([^"]+)"') { $nome = $Matches[1] }
                $proc = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
                $ram  = if ($proc) { "{0:N0}" -f ($proc.WorkingSet64 / 1MB) } else { "n/d" }
                $report.Add(("    {0,-24} {1,-8} {2,-12} {3}" -f $nome, $p.ProcessId, $ram, $p.CreationDate))
            }
        }
    } "worker process IIS"
} else {
    $report.Add("  [salto IIS: servono privilegi di amministratore]")
}

# -------------------------------------------------------- 6. Porte e software
Sezione "6. PORTE E SOFTWARE"
Prova {
    $porte = 80, 443, 5000, 5100, 5300, 5400, 8000, 8001, 8002, 2375, 2376
    $inAscolto = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
                   Where-Object { $porte -contains $_.LocalPort } |
                   Select-Object LocalAddress, LocalPort, OwningProcess -Unique)
    if ($inAscolto.Count -eq 0) {
        Riga "Porte di interesse occupate" "nessuna fra $($porte -join ', ')"
    } else {
        $report.Add("  Porte gia' in ascolto fra quelle che servirebbero:")
        foreach ($c in $inAscolto) {
            $p = Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue
            $report.Add(("    {0,-8} {1,-18} {2}" -f $c.LocalPort, $c.LocalAddress, $(if ($p) { $p.ProcessName } else { "pid $($c.OwningProcess)" })))
        }
    }
} "porte"

$report.Add("")
foreach ($cmd in @("docker", "node", "npm", "git", "dotnet")) {
    $c = Get-Command $cmd -ErrorAction SilentlyContinue
    if ($c) {
        $ver = ""
        try { $ver = (& $cmd --version 2>&1 | Select-Object -First 1) } catch { $ver = "presente" }
        Riga "  $cmd" "$ver"
    } else {
        Riga "  $cmd" "non installato"
    }
}

Prova {
    $bundle = Get-ChildItem "HKLM:\SOFTWARE\Microsoft\Updates\.NET*" -ErrorAction SilentlyContinue |
              Select-Object -ExpandProperty PSChildName
    if ($bundle) { Riga "Aggiornamenti .NET" ($bundle -join ", ") }
    $runtimes = & dotnet --list-runtimes 2>&1 | Where-Object { $_ -match "AspNetCore" } | Select-Object -Last 3
    if ($runtimes) {
        $report.Add("  Runtime ASP.NET Core presenti:")
        foreach ($r in $runtimes) { $report.Add("    $r") }
    }
} "runtime .NET"

# ---------------------------------------------------------- 7. Riavvio in sospeso
Sezione "7. RIAVVIO IN SOSPESO"
Prova {
    $chiavi = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
        "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\PendingFileRenameOperations"
    )
    $sospeso = $false
    foreach ($k in $chiavi) { if (Test-Path $k) { $sospeso = $true } }
    Riga "Riavvio in sospeso" $sospeso
    if ($sospeso) {
        $report.Add("  Nota: abilitare Hyper-V o WSL2 richiede comunque un riavvio.")
        $report.Add("        Un riavvio gia' in sospeso va risolto prima, non durante.")
    }
} "riavvio in sospeso"

# -------------------------------------------------------- 8. Campionamento
Sezione "8. CARICO CAMPIONATO SU $Minuti MINUTI"
$campioni = [math]::Max(1, [int](($Minuti * 60) / $IntervalloSecondi))
$report.Add("Campioni: $campioni, uno ogni $IntervalloSecondi secondi. Attendere...")
Write-Host "Campionamento in corso: $Minuti minuti. Non chiudere la finestra." -ForegroundColor Cyan

$cpuSerie = New-Object System.Collections.Generic.List[double]
$ramSerie = New-Object System.Collections.Generic.List[double]
$totGB = (Get-CimInstance Win32_OperatingSystem).TotalVisibleMemorySize / 1MB

for ($i = 1; $i -le $campioni; $i++) {
    $cpuOra = CpuPercento
    if ($null -ne $cpuOra) { $cpuSerie.Add($cpuOra) }
    try {
        $ramSerie.Add([double]($totGB - (RamDisponibileGB)))
    } catch { }

    Write-Host ("  {0}/{1}  cpu {2}  ram impegnata {3:N1} GB" -f $i, $campioni,
        $(if ($null -ne $cpuOra) { "{0:N0}%" -f $cpuOra } else { "n/d" }),
        $(if ($ramSerie.Count) { $ramSerie[$ramSerie.Count - 1] } else { 0 }))

    if ($i -lt $campioni) { Start-Sleep -Seconds $IntervalloSecondi }
}

function Statistiche($serie, $etichetta, $formato) {
    if ($serie.Count -eq 0) { Riga $etichetta "nessun campione"; return }
    $ordinata = $serie | Sort-Object
    $p95 = $ordinata[[math]::Min($ordinata.Count - 1, [int][math]::Floor($ordinata.Count * 0.95))]
    Riga $etichetta ($formato -f ($serie | Measure-Object -Average).Average, $p95, ($serie | Measure-Object -Maximum).Maximum)
}

$descrizioneCpu = switch ($script:MetodoCpu) {
    "perf" { "contatore formattato" }
    "load" { "Win32_Processor.LoadPercentage (ripiego)" }
    "raw"  { "contatori grezzi (ripiego)" }
    default { "nessuna via disponibile" }
}
$descrizioneRam = switch ($script:MetodoRam) {
    "perf" { "AvailableMBytes, memoria disponibile" }
    "os"   { "FreePhysicalMemory (ripiego): esclude la cache in standby, sottostima il disponibile" }
    default { "n/d" }
}
Riga "Misura CPU" $descrizioneCpu
Riga "Misura RAM" $descrizioneRam
if ($script:MetodoRam -eq "os" -or $script:MetodoCpu -ne "perf") {
    $report.Add("  Nota: le classi Win32_PerfFormattedData_* non rispondono su questa macchina.")
    $report.Add("  I contatori di prestazione si possono ricostruire con 'winmgmt /resyncperf'")
    $report.Add("  da prompt amministrativo, ma e' una modifica: non la fa questo script.")
    $report.Add("")
}
Statistiche $cpuSerie "CPU (media / p95 / max)" "{0:N1}% / {1:N1}% / {2:N1}%"
Statistiche $ramSerie "RAM impegnata (media / p95 / max)" "{0:N1} GB / {1:N1} GB / {2:N1} GB"
Riga "RAM totale" ("{0:N1} GB" -f $totGB)
if ($ramSerie.Count -gt 0) {
    $liberaMin = $totGB - ($ramSerie | Measure-Object -Maximum).Maximum
    Riga "RAM disponibile nel momento peggiore" ("{0:N1} GB" -f $liberaMin)
    $report.Add("")
    $report.Add("Riferimento: i due container Django della federazione stanno in circa 1 GB")
    $report.Add("complessivi a riposo, ma WSL2 o Hyper-V si riservano memoria a parte.")
    $report.Add("Considera 4 GB liberi nel momento peggiore come soglia prudente.")
}

# ------------------------------------------------------- 9. Processi pesanti
Sezione "9. PRIMI 15 PROCESSI PER MEMORIA"
Prova {
    $report.Add(("  {0,-28} {1,-8} {2,-12} {3}" -f "PROCESSO", "PID", "RAM (MB)", "CPU (s)"))
    Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 15 | ForEach-Object {
        $cpuSec = ""
        try { $cpuSec = "{0:N0}" -f $_.CPU } catch { $cpuSec = "n/d" }
        $report.Add(("  {0,-28} {1,-8} {2,-12:N0} {3}" -f $_.ProcessName, $_.Id, ($_.WorkingSet64 / 1MB), $cpuSec))
    }
} "processi"

# ------------------------------------------------------------------ Output
$report.Add("")
$report.Add("=" * 72)
$report.Add("  FINE REPORT")
$report.Add("=" * 72)

$testo = $report -join "`r`n"
$destinazione = Join-Path ([Environment]::GetFolderPath("Desktop")) ("analisi-carico-{0}.txt" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
$testo | Out-File -FilePath $destinazione -Encoding utf8

Write-Host ""
Write-Host $testo
Write-Host ""
Write-Host "Report salvato in: $destinazione" -ForegroundColor Green
