# ChattyDuck - Duckburg Smart City

> **TL;DR**: Assistente al cittadino che risponde esclusivamente sui contenuti certificati dell'ente, esposti tramite **Model Context Protocol (MCP)**. Il contenuto vive nel server MCP, mai nel prompt del modello.

Prototipo dimostrativo sul Comune di Paperopoli (fittizio). L'ente pubblica i propri contenuti attraverso un server MCP; i modelli AI (Gemini e Claude) rispondono solo sulla base di quei contenuti, citando id e versione dei passaggi recuperati.

## Architettura

<!-- Punto ideale per un diagramma dell'architettura (Registry ↔ Portal ↔ modelli) -->

| Progetto | Ruolo |
|---|---|
| `Duckburg.Registry` | Server MCP dell'ente (porta 5000). Minimal API, trasporto Streamable HTTP su `/mcp`. Espone il tool `cerca(query, limite)` e le risorse del corpus. Il corpus è caricato in memoria in sola lettura dal feed del CMS del portale, con `corpus/out/corpus.json` come ripiego. Access token opzionale. |
| `Duckburg.Portal` | Portale del Comune (porta 5100), Razor Pages in stile Designers Italia. Assistente come widget su tutte le pagine e a pagina intera su `/assistente`. Il "cittadino informato". |
| `Duckburg.ServiziOnline` | Portale dei servizi online (porta 5300), stesso layout e assistente del Portal. Il "cittadino attivo" (PNRR Missione 1): Area personale accessibile solo con SPID/CIE tramite Duckburg.Identity. |
| `Duckburg.Identity` | Sistema di accesso del Comune: Relying Party OpenID Connect Federation 1.0 (profilo SPID/CIE), fork di [SPID-CIE-OIDC](https://github.com/fderrigo/SPID-CIE-OIDC) in stile Paperopoli. Entity id `http://identity.paperopoli.derrigo.it:8001`; dopo il login rimanda al portale chiamante con un token firmato (SSO). |
| `Duckburg.Valutazione` | Modulo di valutazione (porta 5400): wrapper web del validatore ufficiale del modello Comuni. |
| `Duckburg.DockerLaunch` | Helper di avvio: esegue `docker compose up -d --build` per la federazione SPID/CIE prima degli altri progetti. Se Docker non è disponibile, avvisa e prosegue. |
| `ChattyDuck.Quack` | Razor Class Library dell'assistente: UI chat, endpoint `POST /chat`, `GET /chat/usage`, `GET /debug/tools`, orchestrazione dei modelli. |
| `ChattyDuck.Models` | Implementazioni intercambiabili di `IModelService` (Gemini, Claude), tracking dei consumi. |
| `ChattyDuck.Mcp` | Client MCP verso il Registry, usato dal bridge Gemini. |

**Principio architetturale**: il system prompt definisce solo il comportamento del modello; i contenuti risiedono unicamente nel corpus del server MCP. Il corpus, a sua volta, è alimentato dal CMS del portale: la redazione scrive in un posto solo, e l'assistente risponde sugli stessi contenuti che il cittadino legge sulle pagine. Vedi [Dal CMS al corpus MCP](#dal-cms-al-corpus-mcp).

I due modelli si collegano al corpus in modo diverso:

- **Gemini**: non supporta MCP nativamente: il portale fa da bridge, traducendo i tool MCP in `functionDeclarations` ed eseguendo le chiamate come `functionResponse`.
- **Claude**: supporta MCP nativamente tramite il connettore della Messages API (parametro `mcp_servers`, header beta `mcp-client-2025-11-20`): si collega direttamente all'endpoint pubblico del server, senza bridge.

## Quick Start

```powershell
# 1. Configurazione: copia i template e inserisci le API key
Copy-Item Duckburg.Portal\appsettings.template.json Duckburg.Portal\appsettings.json
Copy-Item Duckburg.Registry\appsettings.template.json Duckburg.Registry\appsettings.json

# 2. Server MCP (porta 5000)
dotnet run --project Duckburg.Registry

# 3. Portale (porta 5100) -> http://localhost:5100
dotnet run --project Duckburg.Portal
```

Avviando il Registry per primo, il portale non è ancora in ascolto: il Registry parte sul corpus statico e passa a quello del CMS al primo riallineamento (entro `Corpus:RefreshMinutes`, oppure subito con `curl -X POST http://localhost:5000/corpus/reload`).

In Visual Studio i profili di avvio multiplo sono in `DuckburgSmartCity.slnLaunch`:

- **Portal + MCP**: solo Registry e Portal, sufficiente per l'assistente.
- **Tutto (Docker + servizi)**: `Duckburg.DockerLaunch` (federazione SPID/CIE in Docker) più Registry, Portal, Identity, ServiziOnline e Valutazione.

Verifica senza API key:

- `GET http://localhost:5100/debug/tools`: tool MCP visibili al bridge
- `GET http://localhost:5100/chat/usage`: stato dei consumi per modello

## Configurazione

I file `appsettings*.json` reali sono esclusi dal versioning: nel repository ci sono solo i template. In alternativa: variabili d'ambiente o `dotnet user-secrets`.

**Portal**

| Chiave | Variabile d'ambiente | Note |
|---|---|---|
| `Gemini:ApiKey` | `Gemini__ApiKey` | Google AI Studio, free tier |
| `Gemini:Model` | - | default `gemini-2.5-flash` |
| `Anthropic:ApiKey` | `Anthropic__ApiKey` | Anthropic Console, a consumo |
| `Anthropic:Model` | - | es. `claude-haiku-4-5` |
| `Anthropic:McpEndpoint` | `Anthropic__McpEndpoint` | URL **pubblico** del server MCP: deve essere raggiungibile dai server Anthropic (localhost non funziona) |
| `Registry:McpEndpoint` | - | endpoint del bridge Gemini (default `http://localhost:5000/mcp`) |
| `Cms:Database:Provider` | - | `Sqlite` (default), `SqlServer`, `PostgreSql`, `MySql`, `Oracle`; vedi [CMS del portale](#cms-del-portale) |
| `Cms:Admin:Password` | `Cms__Admin__Password` | password dell'area di amministrazione (l'utente è in `Cms:Admin:Username`) |

**Registry**

| Chiave | Note |
|---|---|
| `Corpus:Path` | corpus statico su file (default `../corpus/out/corpus.json`), sorgente di ripiego |
| `Corpus:FeedUrl` | feed del CMS del portale (default `http://localhost:5100/api/corpus`); vuoto per disattivarlo |
| `Corpus:Merge` | `Replace` (default): vince la sorgente disponibile più autorevole. `Merge`: le sorgenti si sommano |
| `Corpus:RefreshMinutes` | intervallo di riallineamento alle sorgenti (default 5, `0` per disattivare) |
| `Registry:AccessToken` | opzionale; se valorizzato richiede `Authorization: Bearer <token>` o `X-Access-Token` |

### Endpoint pubblico

Il percorso Claude e i client MCP esterni richiedono un endpoint raggiungibile da Internet:

- **Sviluppo**: `ngrok http 5000` → `https://<sottodominio>.ngrok-free.dev/mcp` (da riportare in `Anthropic:McpEndpoint`; cambia a ogni riavvio del tunnel).
- **Produzione**: dominio dedicato dietro reverse proxy, ambienti separati.

## Utilizzo

L'assistente è disponibile su `http://localhost:5100` (widget) e su `/assistente` (pagina intera), con selettore del modello.

**Client MCP esterni**: qualunque client MCP può consumare il corpus. La voce "Configura il tuo chatbot" in `/assistente` mostra la configurazione:

```json
{
  "mcpServers": {
    "comune-paperopoli": {
      "type": "http",
      "url": "https://<dominio-pubblico>/mcp"
    }
  }
}
```

**Verifica funzionale**: domande di controllo, valide su ogni client:

1. "Quando scade la prima rata della TARI?" → 30 aprile, cita un passaggio dell'area `tari`
2. "Quali sono le aliquote IMU?" → valori di Paperopoli, dall'area `imu`, citati
3. "Che giorno passa l'umido nel quartiere Vesuvio?" → "Questa informazione non è nelle fonti."
4. "Come prenoto la carta d'identità?" → procedura dall'area della carta d'identità
5. La stessa domanda su client diversi produce la stessa risposta, ancorata al corpus

Gli id dei passaggi dipendono dalla sorgente attiva: con il feed del CMS acceso sono generati dai contenuti del portale (`tari:p08`), con il solo corpus statico sono quelli del file (`tari:p02`). `GET /health` sul Registry dice quale sorgente è in uso.

Test diretto del tool `cerca`, senza modelli:

```bash
curl -s http://localhost:5000/mcp \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"cerca","arguments":{"query":"prima rata TARI"}}}'
```

## Accesso con SPID/CIE (servizi online)

Il flusso di login usa la federazione OIDC italiana emulata in locale con l'infrastruttura ufficiale AgID (`italia/spid-cie-oidc-django` in Docker):

```
Browser -> Duckburg.ServiziOnline (5300) -> /accedi
        -> Duckburg.Identity (identity.paperopoli.derrigo.it:8001)  [RP federato]
        -> OP SPID (trust-anchor.paperopoli.derrigo.it:8000) o OP CIE (cie-provider.paperopoli.derrigo.it:8002)
        <- callback OIDC su Identity -> token SSO firmato -> /auth/callback su ServiziOnline
        -> sessione cookie -> /area-personale
```

Setup (una volta sola):

```powershell
# 1. Hostname locali (PowerShell da amministratore)
.\scripts\add-hosts.ps1

# 2. Chiavi demo del RP (gitignored)
.\scripts\setup-secrets.ps1

# 3. Federazione locale: Trust Anchor + OP CIE + Duckburg.Identity
docker compose up --build
```

Poi, dal repo:

```powershell
dotnet run --project Duckburg.ServiziOnline   # porta 5300
```

Su `http://localhost:5300` la card "Area personale del cittadino" chiede il login (credenziali demo `user / oidcuser` o `admin / oidcadmin`).

Le pagine Django della federazione **non** sono del Comune di Paperopoli: rappresentano gli enti centrali dello Stato fittizio di Palmipedia, con brand e palette distinti (override in `infra/*/templates/`), sullo stesso modello reale SPID/CIE:

| Servizio | Ruolo reale (Italia) | Equivalente Palmipedia | Stile |
|---|---|---|---|
| `trust-anchor.paperopoli.derrigo.it` (onboarding) | AgID, autorità di federazione | **AIDP**: Agenzia per l'Identità Digitale di Palmipedia | navy/argento/oro, sigillo di Stato |
| `trust-anchor.paperopoli.derrigo.it` (login SPID locale) | Gestore SPID privato (es. Poste, Aruba) | **BeccoID S.p.A.**: soggetto privato accreditato AIDP | viola/giallo, fumetto "da startup" |
| `cie-provider.paperopoli.derrigo.it` (login CIE) | Istituto Poligrafico e Zecca dello Stato, per conto del Ministero dell'Interno | **IPZP**: Istituto Poligrafico e Zecca di Palmipedia, per conto del Ministero dell'Interno di Palmipedia | verde/oro, medaglione a conio |

Il test end-to-end della federazione (login CIE, refresh, logout via curl) è in `e2e_test.sh`.

I dump del Trust Anchor registrano il RP con entity id `http://identity.paperopoli.derrigo.it:8001`; le chiavi private demo vivono in `secrets/` (solo il `.sample.json` è versionato).

## Dettagli tecnici

**Monitoraggio dei consumi**: il pannello "Limiti di utilizzo" sotto la chat riporta consumo e quota residua:

- **Claude**: valori reali dagli header `anthropic-ratelimit-*`, intercettati da un `DelegatingHandler` (`AnthropicRateLimitHandler`).
- **Gemini**: token da `usageMetadata` delle risposte; quota residua stimata localmente (Google non la espone via API, verificabile in AI Studio).

Il tracker (`ModelUsageTracker`) è in memoria e si azzera al riavvio.

**Risposte non ancorate**: se un client risponde con normativa nazionale generica anziché con i dati del corpus, non inserire i dati nel prompt: rinforzare le regole di comportamento e mostrare i passaggi recuperati accanto alla risposta (la UI lo fa già con il riquadro "Fonti recuperate").

## CMS del portale

Il portale (`Duckburg.Portal`) include un CMS completo: tutti i contenuti (servizi, novità, organi, uffici, luoghi, eventi, documenti, pagine, menu e impostazioni del sito) sono nel database e gestibili da un'area di amministrazione.

- **Area admin**: `http://localhost:5100/admin` (credenziali in `Cms:Admin`, default `admin` / `paperopoli`). Dashboard, elenco e CRUD per ogni tipo di contenuto, allineati all'architettura dell'informazione del modello Comuni.
- **Contenuti di default**: i contenuti seed in stile Paperopoli sono marcati `IsDefault`. Con `Cms:ProtectDefaultContent: true` non sono modificabili né eliminabili (in sola lettura nell'admin, guardia lato server). Impostando `false` diventano gestibili.
- **Seed**: al primo avvio, se `Cms:SeedOnStartup: true`, lo schema viene creato e popolato. Idempotente: agisce solo su database vuoto.
- **Libreria media**: upload di immagini e allegati da `/admin/media`; i file finiscono in `Duckburg.Portal/wwwroot/media` (escluso dal versioning).
- **Posta in arrivo**: le interazioni raccolte dalle pagine pubbliche finiscono anch'esse nel database e si consultano dall'admin: prenotazioni appuntamento (`/admin/appuntamenti`), segnalazioni di disservizio (`/admin/segnalazioni`), valutazioni di chiarezza (`/admin/valutazioni`, alimentate da `POST /api/valutazione`).

### Database plug-and-play

Il provider si cambia da `appsettings.json` senza toccare il codice:

```jsonc
"Cms": {
  "Database": {
    "Provider": "Sqlite",           // Sqlite | SqlServer | PostgreSql | MySql | Oracle
    "ConnectionString": "Data Source=App_Data/paperopoli-cms.db"
  }
}
```

Esempi di stringa di connessione per gli altri motori:

- **PostgreSql**: `Host=localhost;Database=paperopoli;Username=postgres;Password=...`
- **SqlServer**: `Server=localhost;Database=paperopoli;Trusted_Connection=True;TrustServerCertificate=True`
- **MySql**: `Server=localhost;Database=paperopoli;User=root;Password=...`
- **Oracle**: `User Id=paperopoli;Password=...;Data Source=localhost:1521/XEPDB1`

Lo schema è creato con `EnsureCreated` (provider-agnostico) e le liste sono serializzate in JSON su colonne testo, così il modello resta portabile fra i motori.

### Dal CMS al corpus MCP

I contenuti pubblicati nel CMS sono anche la fonte su cui risponde l'assistente: quello che la redazione scrive in `/admin` diventa un passaggio citabile del corpus, senza riscriverlo altrove.

```
CMS (Sqlite | SqlServer | PostgreSql | ...)
      |
      v
Portal   GET /api/corpus          proiezione read-only dei contenuti pubblicati
      |  (HTTP, JSON)
      v
Registry  sorgenti del corpus
      |-- cms:  feed del portale   (priorità alta, sorgente viva)
      +-- file: corpus.json        (ripiego, la demo gira anche da sola)
      |
      v
  /mcp   tool cerca()
```

- **Proiezione, non copia**: `Duckburg.Portal/Cms/CorpusFeed.cs` traduce servizi, uffici, amministratori, novità, eventi, luoghi, documenti, pagine, FAQ e dati dell'ente in aree (`works`) e passaggi. Un passaggio per campo valorizzato, etichettato ("Come fare", "Cosa serve", "Scadenze"), con l'HTML ridotto a testo semplice. Solo contenuti con `IsPublished`: le bozze non finiscono nelle risposte.
- **Versione e hash per passaggio**: la `version` è l'istante di ultima modifica del contenuto, l'`hash` è lo SHA-256 del testo. La citazione dell'assistente resta verificabile e si vede quando un contenuto è cambiato.
- **Nessuno schema condiviso**: il Registry non conosce il database del portale, parla solo questo JSON. Restano due servizi indipendenti, e il Registry resta l'unico proprietario dell'indice di ricerca.
- **Precedenza e ripiego**: con `Corpus:Merge: "Replace"` (default) vince il feed del CMS, e il file resta la rete di sicurezza per quando il portale è spento. Con `"Merge"` le due sorgenti si sommano e, a parità di id dell'area, vince il CMS.
- **Aggiornamento**: automatico ogni `Corpus:RefreshMinutes` (default 5), oppure subito con `POST http://localhost:5000/corpus/reload`. `GET http://localhost:5000/health` riporta versione, aree, passaggi e sorgente in uso.

```bash
# Cosa sta servendo il Registry, e da dove
curl -s http://localhost:5000/health
# {"status":"ok","corpus_version":"cms-...","works":44,"passages":333,"sources":["cms:http://localhost:5100/api/corpus"], ...}

# Il feed grezzo, leggibile senza il Registry
curl -s http://localhost:5100/api/corpus
```

Il feed espone contenuti già pubblici sulle pagine del portale, quindi non è autenticato. Se il portale è irraggiungibile il Registry non si ferma: tiene l'ultimo corpus caricato e riprova al ciclo successivo.

## Conformità al modello Comuni (misura 1.4.1)

Il portale segue l'architettura dell'informazione e i criteri di conformità del pacchetto **Cittadino Informato** del modello Comuni (PNRR 1.4.1): menu e pagine di secondo livello del vocabolario ufficiale, schede servizio strutturate con indice e `data-element` per l'App di valutazione, Bootstrap Italia, tassonomia argomenti del modello, FAQ, segnalazione disservizio, prenotazione appuntamenti senza autenticazione, widget di valutazione della chiarezza su ogni pagina, licenza CC-BY 4.0 nelle note legali.

### Modulo di valutazione (Duckburg.Valutazione, porta 5400)

Wrapper web del validatore ufficiale [pa-website-validator-ng](https://github.com/italia/pa-website-validator-ng) (Lighthouse + Puppeteer):

1. `bash scripts/setup-valutazione.sh`: clona e compila il validatore in `Duckburg.Valutazione/tool` (richiede Node 18+, npm, git).
2. `dotnet run --project Duckburg.Valutazione`: apre l'interfaccia su `http://localhost:5400`.
3. Dal footer del portale: "Valutazione adesione al modello" → avvia la scansione e consulta i report.

La pagina "Deviazioni dichiarate" documenta i criteri che la demo non supera per scelta (font display in stile fumetto, C.SI.1.1) o per natura dell'ambiente (HTTPS, dominio istituzionale, dichiarazione AgID: un ente immaginario non può registrarli).

## Note

Prototipo dimostrativo, non pronto per la produzione. I dettagli implementativi (bridge Gemini, gestione rate limit, formato del corpus) sono candidati a una futura cartella `docs/`.

**Licenze**: il codice originale è sotto Apache 2.0 (`LICENSE.txt`). I componenti di terze parti inclusi nel repository (configurazione Django della federazione in `infra/`, Bootstrap Italia, pulsanti SPID/CIE, font) restano sotto le rispettive licenze: l'elenco è in `NOTICE-spid-cie-oidc.txt`.
