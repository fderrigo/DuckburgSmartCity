# Deploy

Il repository e' neutro: non contiene riferimenti a un ambiente specifico. Tutto
cio' che riguarda il tuo dominio e i tuoi server vive fuori dal versionamento, in
`deploy/.env` e nelle variabili d'ambiente dei siti.

In locale il progetto gira senza modifiche: vedi il README per il Quick Start e per
la federazione SPID/CIE in Docker.

## Cosa va dove

| Componente | Porta locale | Note |
|---|---|---|
| `Duckburg.Portal` | 5100 | portale informativo, CMS, feed del corpus |
| `Duckburg.Registry` | 5000 | server MCP, deve essere pubblico se usi Claude |
| `Duckburg.ServiziOnline` | 5300 | portale servizi, area personale |
| `Duckburg.Identity` | 8001 | Relying Party OIDC Federation |
| `Duckburg.Valutazione` | 5400 | wrapper del validatore ufficiale |
| Trust Anchor, OP CIE | 8000, 8002 | container Django, immagini ufficiali AgID |

Le prime cinque sono applicazioni .NET: girano ovunque giri ASP.NET Core, IIS
compreso. Le ultime due richiedono Docker.

## Federazione SPID/CIE

```bash
cp deploy/.env.example deploy/.env     # e metti i tuoi hostname
bash deploy/prepara-runtime.sh
docker compose -f deploy/docker-compose.prod.yml up -d --build
```

`prepara-runtime.sh` genera `deploy/runtime/` sostituendo gli hostname di sviluppo
con i tuoi, aggiungendo a Django le impostazioni per stare dietro un reverse proxy,
e allineando il Trust Anchor alla chiave pubblica del Relying Party.

Caddy termina il TLS e chiede i certificati a Let's Encrypt: i record DNS devono
gia' puntare alla macchina e le porte 80 e 443 devono essere raggiungibili.

Verifica:

```bash
curl -s https://$IDENTITY_HOST/.well-known/openid-federation | head -c 20
curl -s https://$TRUST_ANCHOR_HOST/.well-known/openid-federation | head -c 20
curl -s https://$CIE_PROVIDER_HOST/oidc/op/.well-known/openid-federation | head -c 20
```

Ognuna deve restituire un JWT. Nota il percorso diverso per l'OP: l'entity
configuration si serve sotto l'entity id, e quello dell'OP contiene `/oidc/op`.

### Chiavi del Relying Party

`secrets/rp_private_keys.sample.json` e' versionato, quindi pubblico. Va bene per
la demo in locale. Per un RP raggiungibile da Internet servono chiavi nuove: quella
core firma il token SSO che apre l'area personale, e chi ha la chiave puo' emetterne
uno valido.

```powershell
.\scripts\genera-chiavi-rp.ps1 -Destinazione C:\percorso\fuori\dal\repo\rp_private_keys.json
```

Copia il file sulla macchina, in `RP_SECRETS_DIR`, con permessi `600`. Dopo un
cambio di chiavi va rifatto `prepara-runtime.sh` e ricreato il container del Trust
Anchor, altrimenti continua ad attribuire al RP le chiavi vecchie.

## Applicazioni .NET

Nessun segreto negli `appsettings.json`: in produzione si passa tutto da variabili
d'ambiente. Sotto IIS si impostano per app pool o in `web.config`.

| Sito | Variabili |
|---|---|
| Portal | `Gemini__ApiKey`, `Anthropic__ApiKey`, `Anthropic__McpEndpoint`, `Cms__Admin__Password` |
| Registry | `Corpus__FeedUrl`, `Registry__AccessToken` |
| ServiziOnline | `Sso__IdentityBaseUrl`, `Sso__Issuer`, `Sso__CallbackUrl`, `Sso__PostLogoutUrl`, chiavi API |

Tre punti che si scoprono solo sbagliandoli:

**Il feed del corpus va raggiunto per nome pubblico.** Sotto IIS i siti rispondono
per host header, non su `localhost:5100`, quindi `Corpus__FeedUrl` deve puntare al
dominio del portale. Se la macchina non riesce a raggiungere se stessa dall'esterno,
aggiungi al Portal un binding interno e usa quello.

**Il CMS ha bisogno di scrivere.** L'identita' dell'app pool deve avere permessi di
scrittura su `App_Data/` e su `wwwroot/media/`. Senza il primo, il database non
viene creato e il sito non parte.

**L'area `/admin` e' raggiungibile da Internet.** Cambia la password di default e,
se puoi, limitala per indirizzo IP.

## Verifica

```bash
curl -s https://<dominio-mcp>/health
```

Deve riportare `sources` con il feed del CMS e il numero di aree del corpus. Se
mostra il file statico, il Registry non sta raggiungendo il portale.

Il giro completo e' il login: portale dei servizi, area personale, login CIE demo,
logout. Tocca tutti i componenti.

## Rollback

Ogni sito .NET e' una cartella: si torna indietro rinominando e riavviando l'app
pool. La federazione con `docker compose down` e il commit precedente.

Due eccezioni:

- **Database del CMS**: lo schema e' creato con `EnsureCreated`, che non versiona
  le migrazioni. Se una release cambia le entita', il rollback del codice non
  riporta indietro il database. Backup prima di ogni deploy che tocca `Cms/Entities.cs`.
- **Dump della federazione**: cambiare gli entity id richiede di ricreare i
  container, non basta il rollback dei file.

## Stato non rigenerabile

Il database del CMS e `wwwroot/media/`. Tutto il resto si ricostruisce dal
repository e da `deploy/.env`.
