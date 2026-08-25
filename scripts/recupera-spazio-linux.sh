#!/usr/bin/env bash
#
# recupera-spazio-linux.sh
#
# Recupera spazio su disco con interventi a rischio nullo: journal di systemd,
# revisioni snap disattivate, immagini Docker orfane, cache dei pacchetti.
#
# NON tocca:
#   - nulla sotto /var/opt/mssql (dati, log e transaction log di SQL Server)
#   - /swap.img e le altre aree di swap
#   - container, volumi e immagini Docker in uso
#   - file di log attivi tenuti aperti da un processo
#
# Uso:
#   sudo bash recupera-spazio-linux.sh              # prova a vuoto, non modifica niente
#   sudo bash recupera-spazio-linux.sh --applica    # esegue davvero
#
# La prova a vuoto e' il default di proposito: su una macchina con il disco pieno
# la fretta e' la causa piu' comune dei danni.

set -u

APPLICA=0
for arg in "$@"; do
    case "$arg" in
        --applica) APPLICA=1 ;;
        -h|--help) sed -n '2,22p' "$0"; exit 0 ;;
        *) echo "Argomento non riconosciuto: $arg"; exit 1 ;;
    esac
done

if [ "$(id -u)" -ne 0 ]; then
    echo "Serve root: usa sudo."
    exit 1
fi

titolo() {
    echo
    echo "------------------------------------------------------------------------"
    echo "  $1"
    echo "------------------------------------------------------------------------"
}

# Esegue solo se --applica, altrimenti mostra il comando.
azione() {
    if [ "$APPLICA" -eq 1 ]; then
        echo "  eseguo: $*"
        "$@" 2>&1 | sed 's/^/    /'
    else
        echo "  [prova] eseguirei: $*"
    fi
}

liberi_kb() { df -Pk / | awk 'NR==2 {print $4}'; }

PRIMA=$(liberi_kb)

echo "RECUPERO SPAZIO - $(date '+%Y-%m-%d %H:%M:%S')"
if [ "$APPLICA" -eq 1 ]; then
    echo "MODALITA': APPLICA (le modifiche vengono eseguite)"
else
    echo "MODALITA': PROVA A VUOTO (nessuna modifica; aggiungi --applica per eseguire)"
fi
echo
df -h /

# ------------------------------------------------------------------ 1. Journal
titolo "1. JOURNAL DI SYSTEMD"
if command -v journalctl >/dev/null 2>&1; then
    journalctl --disk-usage 2>&1 | sed 's/^/  /'
    echo "  Obiettivo: ridurre a 200 MB."
    azione journalctl --vacuum-size=200M
else
    echo "  [journalctl non disponibile]"
fi

# --------------------------------------------------------------------- 2. Snap
titolo "2. REVISIONI SNAP DISATTIVATE"
if command -v snap >/dev/null 2>&1; then
    trovate=0
    # In 'snap list --all' la nota "disabled" marca le revisioni vecchie:
    # rimuoverle non disinstalla il pacchetto, resta quella attiva.
    while read -r nome revisione; do
        [ -z "${nome:-}" ] && continue
        trovate=$((trovate + 1))
        echo "  $nome revisione $revisione"
        azione snap remove "$nome" --revision="$revisione"
    done < <(snap list --all 2>/dev/null | awk '/disabled/ {print $1, $3}')
    [ "$trovate" -eq 0 ] && echo "  Nessuna revisione disattivata."
else
    echo "  [snap non installato]"
fi

# ------------------------------------------------------------------- 3. Docker
titolo "3. DOCKER: IMMAGINI ORFANE E CACHE DI BUILD"
if command -v docker >/dev/null 2>&1; then
    docker system df 2>&1 | sed 's/^/  /'
    echo
    echo "  Vengono rimosse solo le immagini senza tag e non usate da alcun container,"
    echo "  piu' la cache di build. Container e volumi non vengono toccati."
    azione docker image prune -f
    azione docker builder prune -f
else
    echo "  [docker non installato]"
fi

# ------------------------------------------------------------- 4. Cache apt
titolo "4. CACHE DEI PACCHETTI"
du -xsh /var/cache/apt 2>/dev/null | sed 's/^/  /'
azione apt-get clean

# --------------------------------------------------------- 5. Cosa NON tocco
titolo "5. COSA QUESTO SCRIPT NON TOCCA, E PERCHE'"
echo "  /var/opt/mssql        dati e transaction log di SQL Server: spetta a chi"
echo "                        conosce il recovery model dei singoli database"
echo "  /swap.img             swap attiva: rimuoverla mentre SQL Server ha pagine"
echo "                        li' dentro manda il database in OOM"
echo "  container e volumi    il container n8n non ha volumi, quindi i suoi dati"
echo "                        stanno nel layer scrivibile: un docker rm li cancella"
echo "  file cancellati       lo spazio trattenuto da un processo si libera solo"
echo "    ma ancora aperti    riavviando quel processo, non con rm"

# ------------------------------------------------------------------- Risultato
titolo "RISULTATO"
DOPO=$(liberi_kb)
df -h /
if [ "$APPLICA" -eq 1 ]; then
    RECUPERATO=$(( (DOPO - PRIMA) / 1024 ))
    echo
    echo "  Spazio recuperato: ${RECUPERATO} MB"
    if command -v docker >/dev/null 2>&1; then
        echo
        echo "  Container ancora attivi (verifica che n8n ci sia):"
        docker ps --format '    {{.Names}}  {{.Status}}' 2>&1
    fi
else
    echo
    echo "  Nessuna modifica effettuata. Rilancia con --applica per eseguire."
fi
