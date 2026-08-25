#!/usr/bin/env bash
#
# analisi-server-linux.sh
#
# Fotografia di un server Linux: spazio disco, memoria, Docker, servizi.
# Nato per capire cosa ha riempito il disco e se la macchina puo' ospitare
# altri container.
#
# SOLA LETTURA. Non cancella, non installa, non modifica configurazioni, e non
# scrive nemmeno il proprio report: tutto va su stdout, perche' su un filesystem
# pieno anche un file di log e' spazio che non c'e'.
#
# Uso:
#   sudo bash analisi-server-linux.sh
#   sudo bash analisi-server-linux.sh > /dev/shm/report.txt   # se serve un file, in RAM
#
# Senza privilegi di root alcune sezioni restano incomplete (LVM, lsof, docker).

set -u

TOP=${TOP:-20}

titolo() {
    echo
    echo "========================================================================"
    echo "  $1"
    echo "========================================================================"
}

nota() { echo "  -> $1"; }

# Esegue un comando solo se esiste, altrimenti lo dice e prosegue.
esegui() {
    local cmd="$1"; shift
    if command -v "$cmd" >/dev/null 2>&1; then
        "$cmd" "$@" 2>&1 || echo "  [comando uscito con errore: $cmd $*]"
    else
        echo "  [$cmd non installato]"
    fi
}

echo "ANALISI SERVER LINUX - $(date '+%Y-%m-%d %H:%M:%S')"
echo "Host: $(hostname)   Utente: $(id -un)   Root: $([ "$(id -u)" -eq 0 ] && echo si || echo no)"

# --------------------------------------------------------------- 1. Macchina
titolo "1. MACCHINA"
if [ -r /etc/os-release ]; then
    . /etc/os-release
    echo "Distribuzione : ${PRETTY_NAME:-n/d}"
fi
echo "Kernel        : $(uname -r)"
echo "Architettura  : $(uname -m)"
echo "Uptime        : $(uptime -p 2>/dev/null || uptime)"
echo "Virtualizzato : $(command -v systemd-detect-virt >/dev/null 2>&1 && systemd-detect-virt || echo 'n/d')"
echo "CPU           : $(grep -m1 'model name' /proc/cpuinfo 2>/dev/null | cut -d: -f2- | sed 's/^ *//')"
echo "Core          : $(nproc 2>/dev/null || echo n/d)"
echo "Load average  : $(cut -d' ' -f1-3 /proc/loadavg 2>/dev/null)"
nota "load average va confrontato con il numero di core: sopra il numero di core la macchina e' satura"

# ---------------------------------------------------------------- 2. Memoria
titolo "2. MEMORIA"
esegui free -h
echo
echo "Swap in uso per processo (primi 10):"
if [ -r /proc/1/status ]; then
    for f in /proc/[0-9]*/status; do
        awk '/^Name:/{n=$2} /^VmSwap:/{if ($2+0 > 0) print $2, n}' "$f" 2>/dev/null
    done | sort -rn | head -10 | awk '{printf "  %-10s kB  %s\n", $1, $2}'
fi
nota "la colonna 'available' e' quella che conta, non 'free'"

# ----------------------------------------------------------------- 3. Dischi
titolo "3. SPAZIO DISCO"
echo "Filesystem reali (senza snap e tmpfs):"
df -hT -x squashfs -x tmpfs -x devtmpfs -x overlay 2>/dev/null || df -h
echo
echo "Inode (un filesystem puo' essere pieno anche con spazio libero):"
df -i -x squashfs -x tmpfs -x devtmpfs -x overlay 2>/dev/null | head -10

titolo "4. LVM E DISCHI FISICI"
nota "se il volume group ha spazio libero (VFree), estendere il volume e' la via pulita"
esegui pvs
echo
esegui vgs
echo
esegui lvs
echo
esegui lsblk -o NAME,SIZE,FSTYPE,MOUNTPOINT,TYPE

# --------------------------------------------------- 5. Chi occupa lo spazio
titolo "5. CHI OCCUPA LO SPAZIO"
echo "Livello 1 di / (solo filesystem root, puo' richiedere qualche minuto):"
du -xh --max-depth=1 / 2>/dev/null | sort -h | tail -"$TOP"
echo
echo "Livello 1 di /var:"
du -xh --max-depth=1 /var 2>/dev/null | sort -h | tail -15
echo
echo "Livello 2 di /var/lib:"
du -xh --max-depth=1 /var/lib 2>/dev/null | sort -h | tail -15
echo
echo "File singoli piu' grandi sotto / (primi $TOP):"
find / -xdev -type f -size +100M -printf '%s\t%p\n' 2>/dev/null |
    sort -rn | head -"$TOP" |
    awk -F'\t' '{printf "  %8.1f MB  %s\n", $1/1024/1024, $2}'

# ------------------------------------------- 6. File cancellati ma ancora aperti
titolo "6. FILE CANCELLATI MA ANCORA APERTI"
nota "causa classica di un disco pieno: un log cancellato che un processo tiene aperto."
nota "lo spazio torna solo riavviando quel processo, non con rm."
if command -v lsof >/dev/null 2>&1; then
    lsof -nP +L1 2>/dev/null | head -25
    echo
    echo "Totale spazio trattenuto da file cancellati:"
    lsof -nP +L1 2>/dev/null |
        awk 'NR>1 && $7 ~ /^[0-9]+$/ {s+=$7} END {printf "  %.1f MB\n", s/1024/1024}'
else
    echo "  [lsof non installato: 'apt install lsof' lo aggiunge, ma richiede spazio]"
fi

# -------------------------------------------------------------------- 7. Log
titolo "7. LOG"
esegui journalctl --disk-usage
echo
echo "Dimensione di /var/log (primi 15):"
du -xh --max-depth=1 /var/log 2>/dev/null | sort -h | tail -15

# ------------------------------------------------------------------- 8. Snap
titolo "8. SNAP"
if command -v snap >/dev/null 2>&1; then
    echo "Spazio occupato da /var/lib/snapd:"
    du -xsh /var/lib/snapd 2>/dev/null
    echo
    echo "Revisioni disattivate (rimovibili senza perdere funzionalita'):"
    snap list --all 2>/dev/null | awk '$0 ~ /disabled/ {print "  " $1, $2, $3}'
    echo
    nota "si rimuovono una per una con: snap remove <nome> --revision=<numero>"
else
    echo "  [snap non installato]"
fi

# ----------------------------------------------------------------- 9. Docker
titolo "9. DOCKER"
if command -v docker >/dev/null 2>&1; then
    docker version --format 'Client {{.Client.Version}} / Server {{.Server.Version}}' 2>&1 | head -2
    echo
    echo "Container:"
    docker ps -a --format '  {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}' 2>&1 | head -25
    echo
    echo "Spazio occupato:"
    docker system df 2>&1
    echo
    echo "Dettaglio (immagini e volumi piu' pesanti):"
    docker system df -v 2>&1 | head -45
    echo
    echo "Volumi:"
    docker volume ls 2>&1 | head -20
    echo
    nota "docker builder prune e docker image prune recuperano spazio senza toccare i dati"
    nota "NON usare docker system prune -a --volumes: cancella i volumi, quindi i database"
else
    echo "  [docker non installato]"
fi

# ------------------------------------------------------------- 10. In ascolto
titolo "10. SERVIZI IN ASCOLTO"
if command -v ss >/dev/null 2>&1; then
    ss -tlnp 2>/dev/null | head -30
elif command -v netstat >/dev/null 2>&1; then
    netstat -tlnp 2>/dev/null | head -30
else
    echo "  [ne' ss ne' netstat disponibili]"
fi
echo
echo "Porte che servirebbero alla federazione (8000, 8001, 8002):"
if command -v ss >/dev/null 2>&1; then
    occupate=$(ss -tln 2>/dev/null | awk '{print $4}' | grep -Eo ':(8000|8001|8002)$' | sort -u)
    if [ -z "$occupate" ]; then echo "  tutte libere"; else echo "$occupate" | sed 's/^/  occupata /'; fi
fi

# ------------------------------------------------------- 11. Processi pesanti
titolo "11. PRIMI 15 PROCESSI"
echo "Per memoria:"
ps -eo pid,comm,rss,pcpu --sort=-rss 2>/dev/null | head -16 |
    awk 'NR==1 {printf "  %-8s %-24s %10s %6s\n", "PID", "COMANDO", "RSS (MB)", "CPU%"; next}
         {printf "  %-8s %-24s %10.0f %6s\n", $1, $2, $3/1024, $4}'
echo
echo "Per CPU:"
ps -eo pid,comm,rss,pcpu --sort=-pcpu 2>/dev/null | head -6 |
    awk 'NR>1 {printf "  %-8s %-24s %10.0f %6s\n", $1, $2, $3/1024, $4}'

# ------------------------------------------------------------- 12. Pacchetti
titolo "12. CACHE PACCHETTI"
du -xsh /var/cache/apt 2>/dev/null
nota "apt clean svuota la cache dei .deb: sicuro, recupera spazio subito"

titolo "FINE REPORT"
echo "Nessuna modifica effettuata: questo script e' in sola lettura."
