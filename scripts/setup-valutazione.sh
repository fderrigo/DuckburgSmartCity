#!/usr/bin/env bash
# Installa il validatore ufficiale della misura 1.4.1 (italia/pa-website-validator-ng)
# dentro Duckburg.Valutazione/tool. Richiede Node.js 18+, npm e git.
#
# Uso:  bash scripts/setup-valutazione.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TOOL_DIR="$ROOT/Duckburg.Valutazione/tool"
REPO_DIR="$TOOL_DIR/pa-website-validator-ng"

mkdir -p "$TOOL_DIR"

if [ ! -d "$REPO_DIR/.git" ]; then
    echo ">> Clono italia/pa-website-validator-ng…"
    git clone --depth 1 https://github.com/italia/pa-website-validator-ng "$REPO_DIR"
fi

cd "$REPO_DIR"

echo ">> Installo le dipendenze (senza script: lo script prepare non è compatibile con cmd.exe)…"
npm install --ignore-scripts

echo ">> Installo i browser richiesti da Puppeteer…"
# Le versioni devono combaciare con la major di puppeteer usata dal pacchetto (v23 → 129).
npx puppeteer browsers install chrome-headless-shell@129.0.6668.89
npx puppeteer browsers install chrome@129.0.6668.89

echo ">> Compilo TypeScript e copio gli asset…"
npx tsc
npx copyfiles -u 1 "src/**/*.{ejs,json,scss,css,map}" dist/

echo ">> Verifica:"
node dist --version

echo
echo "Fatto. Avvia Duckburg.Valutazione (porta 5400) e lancia una scansione dal browser."
