# secrets/

RP **private keys** live here. They are NOT committed (see root `.gitignore`).

- `rp_private_keys.sample.json` — committed **demo/test** keys (the public italia fixtures).
  Safe to share; use only for the local demo.
- `rp_private_keys.json` — the file actually loaded (gitignored). Create it from the sample:
  ```powershell
  ./scripts/setup-secrets.ps1
  ```

## Produzione

Non usare queste chiavi. Fornisci le chiavi reali tramite uno dei meccanismi di
configurazione .NET (in ordine di precedenza in `RpConfig.ResolvePrivateKeys`):

1. `Rp:PrivateKeysFile` — percorso a un file secret **montato** (Docker/K8s secret).
2. `Rp:PrivateKeys` — JSON inline da **env** `Rp__PrivateKeys` o **Azure Key Vault**.

Formato atteso:
```json
{ "jwks_fed": { "keys": [ ... ] }, "jwks_core": { "keys": [ ... ] } }
```
