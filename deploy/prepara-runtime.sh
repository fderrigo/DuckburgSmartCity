#!/usr/bin/env bash
#
# prepara-runtime.sh
#
# Genera deploy/runtime/ a partire dai file neutri del repository e dagli hostname
# in deploy/.env. La cartella runtime/ non e' versionata: e' li' che finiscono i
# riferimenti al tuo ambiente, cosi' il repository resta deployabile da chiunque.
#
# Cosa fa:
#   1. copia infra/ in runtime/, senza database e log di esecuzioni precedenti
#   2. sostituisce gli hostname di sviluppo con quelli configurati, in https
#   3. aggiunge alle impostazioni Django cio' che serve dietro un reverse proxy
#   4. genera runtime/rp_public.json con gli entity id di questo ambiente
#   5. allinea il Trust Anchor alla chiave pubblica del RP in uso
#
# Il punto 5 e' quello che si dimentica sempre: se il RP usa chiavi diverse da
# quelle che il Trust Anchor gli attribuisce, la catena di fiducia si rompe al
# primo login con un errore che non parla di chiavi.
#
# Uso:  bash deploy/prepara-runtime.sh
# E' idempotente: si puo' rilanciare a ogni deploy.

set -euo pipefail

QUI="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RADICE="$(cd "$QUI/.." && pwd)"
RUNTIME="$QUI/runtime"

if [ ! -f "$QUI/.env" ]; then
    echo "Manca deploy/.env. Parti da .env.example:"
    echo "    cp deploy/.env.example deploy/.env"
    exit 1
fi

set -a
# shellcheck disable=SC1091
. "$QUI/.env"
set +a

for v in IDENTITY_HOST TRUST_ANCHOR_HOST CIE_PROVIDER_HOST SERVIZIONLINE_URL RP_SECRETS_DIR; do
    if [ -z "${!v:-}" ]; then echo "Variabile mancante in deploy/.env: $v"; exit 1; fi
done

echo "Hostname configurati:"
echo "  identity      $IDENTITY_HOST"
echo "  trust anchor  $TRUST_ANCHOR_HOST"
echo "  cie provider  $CIE_PROVIDER_HOST"
echo

# --- 1. copia pulita di infra/ -------------------------------------------------
rm -rf "$RUNTIME"
mkdir -p "$RUNTIME"
cp -r "$RADICE/infra/federation_authority" "$RUNTIME/federation_authority"
cp -r "$RADICE/infra/provider"             "$RUNTIME/provider"

# I database e i log sono artefatti di esecuzioni precedenti: si rigenerano.
find "$RUNTIME" -name "db.sqlite3" -delete
find "$RUNTIME" -path "*/logs/*" -type f -delete
echo "Copiata infra/ in runtime/, senza database e log."

# --- 2. hostname di questo ambiente -------------------------------------------
sostituisci() {
    local f="$1"
    sed -i \
        -e "s|http://identity.paperopoli.test:8001|https://$IDENTITY_HOST|g" \
        -e "s|http://trust-anchor.paperopoli.test:8000|https://$TRUST_ANCHOR_HOST|g" \
        -e "s|http://cie-provider.paperopoli.test:8002|https://$CIE_PROVIDER_HOST|g" \
        "$f"
}

while IFS= read -r f; do sostituisci "$f"; done < <(
    find "$RUNTIME" -type f \( -name "*.json" -o -name "*.py" \)
)
echo "Sostituiti gli hostname negli entity id."

# --- 3. Django dietro reverse proxy -------------------------------------------
# Senza queste righe Django si crede su http, genera URL http negli endpoint OIDC
# e i redirect si rompono con errori che sembrano di trust chain ma sono di schema.
for s in "$RUNTIME/federation_authority/federation_authority/settingslocal.py" \
         "$RUNTIME/provider/provider/settingslocal.py"; do
    cat >> "$s" <<PYEOF


# ---- Aggiunto da deploy/prepara-runtime.sh: esecuzione dietro reverse proxy ----
SECURE_PROXY_SSL_HEADER = ("HTTP_X_FORWARDED_PROTO", "https")
USE_X_FORWARDED_HOST = True
CSRF_TRUSTED_ORIGINS = [
    "https://$TRUST_ANCHOR_HOST",
    "https://$CIE_PROVIDER_HOST",
    "https://$IDENTITY_HOST",
]
ALLOWED_HOSTS = [
    "$TRUST_ANCHOR_HOST",
    "$CIE_PROVIDER_HOST",
    "$IDENTITY_HOST",
]
SESSION_COOKIE_SECURE = True
CSRF_COOKIE_SECURE = True
# Con DEBUG attivo una pagina di errore mostra configurazione e variabili
# d'ambiente a chiunque la provochi.
DEBUG = False
PYEOF
done
echo "Aggiunte le impostazioni per il reverse proxy."

# --- 4. configurazione pubblica del RP ----------------------------------------
cp "$RADICE/Duckburg.Identity/rp_public.json" "$RUNTIME/rp_public.json"
sostituisci "$RUNTIME/rp_public.json"
echo "Generato runtime/rp_public.json."

# --- 5. allineamento della catena di fiducia ----------------------------------
CHIAVI="$RP_SECRETS_DIR/rp_private_keys.json"
if [ -f "$CHIAVI" ]; then
    python3 - "$CHIAVI" "$RUNTIME/federation_authority/dumps/example.json" "https://$IDENTITY_HOST" <<'PY'
import json, sys
chiavi, dump, rp = sys.argv[1], sys.argv[2], sys.argv[3]
fed = json.load(open(chiavi))["jwks_fed"]["keys"][0]
pub = {"kty": fed["kty"], "e": fed["e"], "n": fed["n"], "kid": fed["kid"]}
d = json.load(open(dump))
n = 0
for r in d:
    f = r["fields"]
    if f.get("sub") == rp and "jwks" in f:
        f["jwks"] = [pub] if isinstance(f["jwks"], list) else {"keys": [pub]}
        n += 1
json.dump(d, open(dump, "w"), indent=2, ensure_ascii=False)
print(f"  Trust Anchor allineato alla chiave del RP ({n} record), kid {pub['kid']}")
PY
else
    echo "  ATTENZIONE: $CHIAVI non trovato."
    echo "  Il Trust Anchor restera' sulle chiavi demo, che sono versionate e quindi"
    echo "  pubbliche. Genera chiavi nuove con scripts/genera-chiavi-rp.ps1 prima di"
    echo "  esporre il Relying Party su Internet."
fi

echo
echo "Pronto. Avvia con:"
echo "    docker compose -f deploy/docker-compose.prod.yml up -d --build"
