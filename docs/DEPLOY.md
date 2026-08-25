# Deploy

Il repository e' neutro: non contiene riferimenti a un ambiente specifico. Tutto cio'
che riguarda il tuo dominio e i tuoi server vive fuori dal versionamento, in
`deploy/.env` e nelle variabili d'ambiente dei siti.

In locale il progetto gira senza modifiche: vedi il README per il Quick Start.

## Topologia

```
CMS del cliente  ◀──legge── Ingestione ──scrive──▶ Corpus ◀──legge── MCP Server ◀──── ChattyDuck
                            (una per CMS)                                        ◀──── Anthropic
                                                                                 ◀──── client di terzi
```

Ogni freccia indica chi chiama chi. L'ingestione e' l'unica che si sveglia da sola.

| Componente | Porta locale | Pubblico | Note |
|---|---|---|---|
| `ChattyDuck.Corpus` | 5200 | no | custodisce i contenuti, multi-ente |
| `Duckburg.Ingestione` | 5250 | no | adattatore per un CMS, temporizzato |
| `ChattyDuck.McpServer` | 5000 | **si'** | i client MCP di Anthropic si collegano da soli |
| `Duckburg.Portal` | 5100 | si' | portale informativo e CMS |
| `Duckburg.ServiziOnline` | 5300 | si' | portale servizi, area personale |
| `Duckburg.Identity` | 8001 | si' | Relying Party OIDC Federation |
| `Duckburg.Valutazione` | 5400 | a scelta | wrapper del validatore ufficiale |
| Trust Anchor, OP CIE | 8000, 8002 | si' | container Django, immagini ufficiali AgID |

Corpus e Ingestione **non vanno esposti**. Il corpus riceve le istantanee dall'adattatore
e le serve al server MCP: entrambi i suoi interlocutori stanno dentro. Esporlo aggiungerebbe
una superficie di scrittura senza dare nulla in cambio.

Le prime sette sono applicazioni .NET: girano ovunque giri ASP.NET Core, IIS compreso.
Le ultime due richiedono Docker.

## Ordine di avvio

Conta, e non e' un dettaglio: **il server MCP non parte se il corpus non risponde**, per
scelta. Rispondere ai cittadini su un corpus vuoto sarebbe peggio che non rispondere.

1. Corpus
2. Ingestione, e una prima esecuzione (`POST /esegui`)
3. Server MCP
4. Portale e servizi online

Al primo deploy il corpus e' vuoto finche' l'ingestione non gira: se avvii il server MCP
prima, si ferma con un errore che dice esattamente questo.

## Corpus

Nessun dato in ingresso senza chiave. Ogni ente ha la propria, e non le permette di
toccare il corpus di un altro.

```jsonc
"Corpus": {
  "Database": { "Provider": "Sqlite", "ConnectionString": "Data Source=App_Data/corpus.db" },
  "ChiaveLettura": "",              // vuota: lettura aperta, i contenuti sono gia' pubblici
  "Enti": [ { "Id": "<ente>", "ChiaveIngestione": "<chiave-lunga-e-casuale>" } ]
}
```

Il database va dove va scritto: permessi di scrittura sulla cartella di `App_Data`, come
per il CMS. Il percorso relativo e' ancorato alla radice del contenuto, non alla directory
del processo, quindi sotto IIS funziona senza accorgimenti.

Lo schema del modello e' servito su `/schema/corpus-1.0.json`: e' quello che legge chi
scrive un adattatore per un altro CMS.

## Ingestione

```jsonc
"Ingestione": {
  "IdEnte": "<ente>",
  "UrlPortale": "https://<dominio-portale>",
  "UrlCorpus": "http://<host-interno-corpus>:5200",
  "ChiaveCorpus": "<la stessa di ChiaveIngestione>",
  "IntervalloMinuti": 15,
  "Cms": { "Provider": "Sqlite", "ConnectionString": "<connessione al CMS>" }
}
```

`UrlPortale` non serve a leggere: serve a costruire le URL pubbliche dei contenuti, quelle
che una risposta offre al cittadino. Se e' sbagliata, l'assistente cita pagine che non
esistono.

Dopo una pubblicazione nel CMS, per non aspettare il giro:

```bash
curl -X POST http://<host-ingestione>:5250/esegui
curl -X POST https://<dominio-mcp>/corpus/reload
```

Il primo rilegge il CMS, il secondo fa riallineare il server MCP. Senza il secondo,
l'aggiornamento arriva comunque entro `Corpus:RiallineamentoMinuti`.

## Server MCP

```jsonc
"Corpus": {
  "Url": "http://<host-interno-corpus>:5200",
  "Ente": "<ente>",
  "Chiave": "",
  "RiallineamentoMinuti": 5
},
"Registry": { "AccessToken": "" }
```

E' l'unico dei tre che va pubblicato: i server di Anthropic si collegano direttamente
all'endpoint `/mcp` per il percorso Claude, e lo stesso endpoint serve a qualunque client
MCP di terzi.

`Registry:AccessToken` chiude `/mcp` a chi non ha il token. Va deciso: chiuderlo impedisce
a un lettore dell'articolo di provare il collegamento, lasciarlo aperto significa che
chiunque puo' interrogare il corpus. I contenuti sono comunque pubblici, quindi e' una
questione di consumo, non di riservatezza.

## Applicazioni .NET

Nessun segreto negli `appsettings.json`: in produzione si passa tutto da variabili
d'ambiente. Sotto IIS si impostano per app pool o in `web.config`.

| Sito | Variabili |
|---|---|
| Portal | `Gemini__ApiKey`, `Anthropic__ApiKey`, `Anthropic__McpEndpoint`, `Cms__Admin__Password`, `Siti__*` |
| ServiziOnline | `Sso__*`, chiavi API, `Siti__Portale` |
| Corpus | `Corpus__Enti__0__ChiaveIngestione` |
| Ingestione | `Ingestione__ChiaveCorpus` |
| MCP Server | `Registry__AccessToken` se lo chiudi |

Tre punti che si scoprono solo sbagliandoli:

**Gli indirizzi degli altri portali** stanno in `Siti:*`. Sono i link incrociati fra
portale, servizi online e valutazione: lasciati ai valori di sviluppo, in produzione
puntano a `localhost`.

**Il CMS e il corpus hanno bisogno di scrivere.** Permessi sull'identita' dell'app pool
per `Duckburg.Portal/App_Data`, `Duckburg.Portal/wwwroot/media` e
`ChattyDuck.Corpus/App_Data`. Senza, il database non viene creato e il sito non parte.

**L'area `/admin` e' raggiungibile da Internet.** Cambia la password di default e, se
puoi, limitala per indirizzo IP.

## Federazione SPID/CIE

```bash
cp deploy/.env.example deploy/.env     # e metti i tuoi hostname
bash deploy/prepara-runtime.sh
docker compose -f deploy/docker-compose.prod.yml up -d --build
```

`prepara-runtime.sh` genera `deploy/runtime/` sostituendo gli hostname di sviluppo con i
tuoi, aggiungendo a Django le impostazioni per stare dietro un reverse proxy, e allineando
il Trust Anchor alle chiavi del Relying Party. Quest'ultimo passo e' quello che si
dimentica: se le chiavi non coincidono la catena di fiducia si rompe al primo login, con
un errore che parla di client non autorizzato invece che di chiavi.

Caddy termina il TLS e chiede i certificati a Let's Encrypt: i record DNS devono gia'
puntare alla macchina e le porte 80 e 443 devono essere raggiungibili.

```bash
curl -s https://$IDENTITY_HOST/.well-known/openid-federation | head -c 20
curl -s https://$TRUST_ANCHOR_HOST/.well-known/openid-federation | head -c 20
curl -s https://$CIE_PROVIDER_HOST/oidc/op/.well-known/openid-federation | head -c 20
```

Ognuna deve restituire un JWT. Nota il percorso diverso per l'OP: l'entity configuration
si serve sotto l'entity id, e quello dell'OP contiene `/oidc/op`.

### Chiavi del Relying Party

`secrets/rp_private_keys.sample.json` e' versionato, quindi pubblico. Va bene in locale.
Per un RP raggiungibile da Internet servono chiavi nuove: quella core firma il token SSO
che apre l'area personale, e chi ha la chiave puo' emetterne uno valido.

```powershell
.\scripts\genera-chiavi-rp.ps1 -Destinazione C:\percorso\fuori\dal\repo\rp_private_keys.json
```

Copia il file in `RP_SECRETS_DIR` con permessi `600`, poi rifai `prepara-runtime.sh` e
ricrea il container del Trust Anchor.

## Verifica

```bash
# la catena, dal basso
curl -s http://<host-corpus>:5200/health         # ente e versione dell'istantanea
curl -s http://<host-ingestione>:5250/           # esito dell'ultima esecuzione
curl -s https://<dominio-mcp>/health             # contenuti e sezioni indicizzati
```

I tre numeri devono coincidere. Se il server MCP ne ha meno del corpus, non si e' ancora
riallineato.

Da browser, il giro che tocca tutto: portale, widget della chat che risponde citando le
fonti, `/admin` con una modifica che compare nelle risposte dopo l'ingestione, servizi
online con login CIE demo, logout.

## Rollback

Ogni sito .NET e' una cartella: si torna indietro rinominando e riavviando l'app pool. La
federazione con `docker compose down` e il commit precedente.

Tre eccezioni:

- **Database del CMS**: lo schema e' creato con `EnsureCreated`, che non versiona le
  migrazioni. Un rollback del codice non riporta indietro il database. Backup prima di
  ogni deploy che tocca `Cms/Entities.cs`.
- **Database del corpus**: conserva la storia delle istantanee, quindi tornare a una
  versione precedente del corpus non richiede un rollback: basta ripubblicare. Ma il
  database va comunque salvato con il resto.
- **Dump della federazione**: cambiare gli entity id richiede di ricreare i container.

## Stato non rigenerabile

Il database del CMS e `wwwroot/media/`. Il corpus si ricostruisce rieseguendo
l'ingestione, quindi non e' stato prezioso: e' una proiezione.
