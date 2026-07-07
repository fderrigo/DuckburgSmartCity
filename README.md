# ChattyDuck — Duckburg Smart City

Prototipo di assistente al cittadino basato su fonte certificata, esposta tramite **Model Context Protocol (MCP)**.

Il principio architetturale è la separazione tra contenuto e modello: l'ente pubblica i propri contenuti certificati attraverso un server MCP e i modelli AI rispondono esclusivamente sulla base di quei contenuti, citando id e versione dei passaggi recuperati. Il caso di studio è il Comune di Paperopoli (fittizio).

## Architettura

La solution è composta da cinque progetti:

| Progetto | Ruolo |
|---|---|
| `Duckburg.Registry` | Server MCP dell'ente (porta 5000). Minimal API con trasporto Streamable HTTP su `/mcp`. Carica `corpus/out/corpus.json` in memoria tramite `CorpusService` (sola lettura) ed espone il tool `cerca(query, limite)` e le risorse del corpus. Supporta un access token opzionale. |
| `Duckburg.Portal` | Portale del Comune (porta 5100). Razor Pages conformi alle linee guida Designers Italia. L'assistente è disponibile come widget su tutte le pagine e a pagina intera su `/assistente`. |
| `ChattyDuck.Quack` | Razor Class Library dell'assistente: UI chat (pagina e widget), endpoint `POST /chat`, `GET /chat/usage`, `GET /debug/tools`, orchestrazione dei modelli. |
| `ChattyDuck.Models` | Implementazioni intercambiabili di `IModelService` per Gemini e Claude. Include `ModelUsageTracker` e la cattura degli header di rate limit Anthropic. |
| `ChattyDuck.Mcp` | Client MCP verso il Registry (`McpGateway`), utilizzato dal bridge Gemini. |

### Integrazione MCP: due percorsi

I due modelli si collegano al corpus con modalità diverse, a seconda del supporto nativo al protocollo.

- **Gemini** (`GeminiModelService`) — Gemini non supporta MCP nativamente. Il bridge risiede nel portale: `McpGateway` enumera i tool del Registry, il servizio li traduce in `functionDeclarations`, esegue le chiamate e restituisce i risultati come `functionResponse`.
- **Claude** (`ClaudeModelService`) — Claude supporta MCP nativamente tramite il connettore MCP della Messages API (header beta `mcp-client-2025-11-20`). L'endpoint pubblico del server viene passato nel parametro `mcp_servers` e il modello si collega direttamente, senza bridge lato applicazione.

Il system prompt (`SystemPrompt.cs`) definisce esclusivamente il comportamento del modello; i contenuti risiedono nel solo corpus e non vengono mai inseriti nel prompt.

### Monitoraggio dei consumi

Il pannello "Limiti di utilizzo" sotto la chat riporta consumo e quota residua per modello:

- **Claude** — valori reali letti dagli header `anthropic-ratelimit-*` delle risposte API, intercettati da un `DelegatingHandler` (`AnthropicRateLimitHandler`).
- **Gemini** — token conteggiati da `usageMetadata` delle risposte; la quota residua è una stima locale, poiché Google non espone la quota via API (verificabile in AI Studio).

Il tracker è in memoria e viene azzerato al riavvio dell'applicazione.

## Configurazione

I file `appsettings*.json` reali sono esclusi dal versioning; nel repository sono presenti solo i template. Al primo avvio copiare i template e valorizzare le chiavi:

```powershell
Copy-Item Duckburg.Portal\appsettings.template.json Duckburg.Portal\appsettings.json
Copy-Item Duckburg.Registry\appsettings.template.json Duckburg.Registry\appsettings.json
```

| Chiave (Portal) | Variabile d'ambiente | Note |
|---|---|---|
| `Gemini:ApiKey` | `Gemini__ApiKey` | Google AI Studio, free tier |
| `Gemini:Model` | — | default `gemini-2.5-flash` |
| `Anthropic:ApiKey` | `Anthropic__ApiKey` | Anthropic Console, a consumo |
| `Anthropic:Model` | — | es. `claude-haiku-4-5` |
| `Anthropic:McpEndpoint` | `Anthropic__McpEndpoint` | URL pubblico del server MCP (es. tunnel ngrok). Deve essere raggiungibile dai server Anthropic: un endpoint localhost non è utilizzabile. |
| `Registry:McpEndpoint` | — | endpoint MCP usato dal bridge Gemini (default `http://localhost:5000/mcp`) |

| Chiave (Registry) | Note |
|---|---|
| `Corpus:Path` | percorso di `corpus.json` (default `../corpus/out/corpus.json`) |
| `Registry:AccessToken` | opzionale; se valorizzato, le richieste devono includere `Authorization: Bearer <token>` oppure `X-Access-Token` |

In alternativa ai file di configurazione è possibile usare variabili d'ambiente o `dotnet user-secrets`.

## Avvio

```powershell
# Server MCP (porta 5000)
dotnet run --project Duckburg.Registry

# Portale (porta 5100) -> http://localhost:5100
dotnet run --project Duckburg.Portal
```

In Visual Studio è disponibile il profilo di avvio multiplo "Portal + MCP" (`DuckburgSmartCity.slnLaunch`).

Verifiche di base, senza API key configurate:

- `GET http://localhost:5100/debug/tools` — elenco dei tool MCP visibili al bridge
- `GET http://localhost:5100/chat/usage` — stato dei consumi per modello

## Esposizione del server MCP

Il percorso Claude e i client MCP esterni richiedono un endpoint pubblico:

- **Sviluppo** — `ngrok http 5000`, endpoint risultante `https://<sottodominio>.ngrok-free.dev/mcp`, da riportare in `Anthropic:McpEndpoint`. L'URL cambia a ogni riavvio del tunnel.
- **Produzione** — dominio dedicato dietro reverse proxy, con ambienti separati.

## Client MCP esterni

Qualunque client MCP può consumare il corpus senza codice aggiuntivo. Nella pagina `/assistente`, la voce "Configura il tuo chatbot" del selettore modello mostra endpoint e configurazione di riferimento:

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

## Verifica funzionale

Casi di test da eseguire su ogni client:

1. "Quando scade la prima rata della TARI?" → 30 aprile, con citazione di `tari:p02`.
2. "Quali sono le aliquote IMU?" → valori di Paperopoli (`imu:p02`), citati.
3. "Che giorno passa l'umido nel quartiere Vesuvio?" → "Questa informazione non è nelle fonti."
4. "Come prenoto la carta d'identità?" → procedura da `carta-identita-residenza:p01/p02`.
5. La stessa domanda posta a client diversi produce la stessa risposta, ancorata al corpus.

Test diretto del tool `cerca`, senza passare dai modelli:

```bash
curl -s http://localhost:5000/mcp \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"cerca","arguments":{"query":"prima rata TARI"}}}'
```

Se un client risponde con normativa nazionale generica anziché con i dati del corpus, la soluzione non è inserire i dati nel prompt: vanno rinforzate le regole di comportamento e mostrati i passaggi recuperati accanto alla risposta (la UI lo fa già con il riquadro "Fonti recuperate").
