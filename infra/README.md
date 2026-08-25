# infra/ — vendored test federation (Trust Anchor + OpenID Providers)

Configurazione e fixtures **di terze parti**, committate di proposito per rendere la demo
locale riproducibile e autonoma (clone + run, senza dipendere da repo esterni a runtime).

## Provenienza

- Origine: [`italia/spid-cie-oidc-django`](https://github.com/italia/spid-cie-oidc-django)
  (branch `main`, ottenuto il **2026-06-04**).
- Licenza: **Apache-2.0** (vedi `UPSTREAM_LICENSE`). Solo materiale di test/demo.
- Generazione: prodotta dallo script upstream `docker-prepare.sh`, che applica il
  rewrite degli host alle fixtures `examples/`:
  - `127.0.0.1:8000` → `trust-anchor.paperopoli.test:8000`
  - `127.0.0.1:8001` → `relying-party.org:8001`
  - `127.0.0.1:8002` → `cie-provider.paperopoli.test:8002`

## Contenuto

| Path | Ruolo |
|------|-------|
| `federation_authority/dumps/example.json` | `loaddata` del Trust Anchor: discendenti, OP SPID, trust marks, **chiavi pubbliche del RP** |
| `federation_authority/.../settingslocal.py` | settings django del Trust Anchor |
| `provider/dumps/example.json` | `loaddata` dell'OpenID Provider CIE |
| `provider/.../settingslocal.py` | settings django dell'OP |
| `*/logs/README.md` | placeholder (i `.log` runtime sono gitignored) |

## Pin & coerenza

- L'immagine Docker è **pinnata per digest** in `docker-compose.yml`
  (`ghcr.io/italia/spid-cie-oidc-django@sha256:80b594f1…`). I dump qui committati devono
  combaciare con quella immagine.
- Le **chiavi pubbliche del RP** dentro `federation_authority/dumps/example.json`
  (kid `wL_LmP8…`) devono combaciare con le chiavi private demo in
  `../secrets/rp_private_keys.sample.json`. Non aggiornare l'una senza l'altra, altrimenti
  la trust chain si rompe.

## Runtime

I container scrivono `db.sqlite3` e `logs/*.log` dentro queste cartelle (volume mount):
sono **rigenerati a ogni `docker compose up`** (`migrate` + `loaddata`) e **gitignored**.
Non vanno committati.
