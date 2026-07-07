# ChattyDuck — Duckburg Smart City

> **TL;DR** — Assistente al cittadino che risponde esclusivamente sui contenuti certificati dell'ente, esposti tramite **Model Context Protocol (MCP)**. Il contenuto vive nel server MCP, mai nel prompt del modello.

Prototipo dimostrativo sul Comune di Paperopoli (fittizio). L'ente pubblica i propri contenuti attraverso un server MCP; i modelli AI (Gemini e Claude) rispondono solo sulla base di quei contenuti, citando id e versione dei passaggi recuperati.

## Architettura

<!-- Punto ideale per un diagramma dell'architettura (Registry ↔ Portal ↔ modelli) -->

| Progetto | Ruolo |
|---|---|
| `Duckburg.Registry` | Server MCP dell'ente (porta 5000). Minimal API, trasporto Streamable HTTP su `/mcp`. Espone il tool `cerca(query, limite)` e le risorse del corpus (`corpus/out/corpus.json`, caricato in memoria in sola lettura). Access token opzionale. |
| `Duckburg.Portal` | Portale del Comune (porta 5100), Razor Pages in stile Designers Italia. Assistente come widget su tutte le pagine e a pagina intera su `/assistente`. |
| `ChattyDuck.Quack` | Razor Class Library dell'assistente: UI chat, endpoint `POST /chat`, `GET /chat/usage`, `GET /debug/tools`, orchestrazione dei modelli. |
| `ChattyDuck.Models` | Implementazioni intercambiabili di `IModelService` (Gemini, Claude), tracking dei consumi. |
| `ChattyDuck.Mcp` | Client MCP verso il Registry, usato dal bridge Gemini. |

**Principio architetturale**: il system prompt definisce solo il comportamento del modello; i contenuti risiedono unicamente nel corpus del server MCP.

I due modelli si collegano al corpus in modo diverso:

- **Gemini** — non supporta MCP nativamente: il portale fa da bridge, traducendo i tool MCP in `functionDeclarations` ed eseguendo le chiamate come `functionResponse`.
- **Claude** — supporta MCP nativamente tramite il connettore della Messages API (parametro `mcp_servers`, header beta `mcp-client-2025-11-20`): si collega direttamente all'endpoint pubblico del server, senza bridge.

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

In Visual Studio: profilo di avvio multiplo "Portal + MCP" (`DuckburgSmartCity.slnLaunch`).

Verifica senza API key:

- `GET http://localhost:5100/debug/tools` — tool MCP visibili al bridge
- `GET http://localhost:5100/chat/usage` — stato dei consumi per modello

## Configurazione

I file `appsettings*.json` reali sono esclusi dal versioning: nel repository ci sono solo i template. In alternativa: variabili d'ambiente o `dotnet user-secrets`.

**Portal**

| Chiave | Variabile d'ambiente | Note |
|---|---|---|
| `Gemini:ApiKey` | `Gemini__ApiKey` | Google AI Studio, free tier |
| `Gemini:Model` | — | default `gemini-2.5-flash` |
| `Anthropic:ApiKey` | `Anthropic__ApiKey` | Anthropic Console, a consumo |
| `Anthropic:Model` | — | es. `claude-haiku-4-5` |
| `Anthropic:McpEndpoint` | `Anthropic__McpEndpoint` | URL **pubblico** del server MCP: deve essere raggiungibile dai server Anthropic (localhost non funziona) |
| `Registry:McpEndpoint` | — | endpoint del bridge Gemini (default `http://localhost:5000/mcp`) |

**Registry**

| Chiave | Note |
|---|---|
| `Corpus:Path` | percorso di `corpus.json` (default `../corpus/out/corpus.json`) |
| `Registry:AccessToken` | opzionale; se valorizzato richiede `Authorization: Bearer <token>` o `X-Access-Token` |

### Endpoint pubblico

Il percorso Claude e i client MCP esterni richiedono un endpoint raggiungibile da Internet:

- **Sviluppo** — `ngrok http 5000` → `https://<sottodominio>.ngrok-free.dev/mcp` (da riportare in `Anthropic:McpEndpoint`; cambia a ogni riavvio del tunnel).
- **Produzione** — dominio dedicato dietro reverse proxy, ambienti separati.

## Utilizzo

L'assistente è disponibile su `http://localhost:5100` (widget) e su `/assistente` (pagina intera), con selettore del modello.

**Client MCP esterni** — qualunque client MCP può consumare il corpus. La voce "Configura il tuo chatbot" in `/assistente` mostra la configurazione:

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

**Verifica funzionale** — domande di controllo, valide su ogni client:

1. "Quando scade la prima rata della TARI?" → 30 aprile, cita `tari:p02`
2. "Quali sono le aliquote IMU?" → valori di Paperopoli (`imu:p02`), citati
3. "Che giorno passa l'umido nel quartiere Vesuvio?" → "Questa informazione non è nelle fonti."
4. "Come prenoto la carta d'identità?" → procedura da `carta-identita-residenza:p01/p02`
5. La stessa domanda su client diversi produce la stessa risposta, ancorata al corpus

Test diretto del tool `cerca`, senza modelli:

```bash
curl -s http://localhost:5000/mcp \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"cerca","arguments":{"query":"prima rata TARI"}}}'
```

## Dettagli tecnici

**Monitoraggio dei consumi** — il pannello "Limiti di utilizzo" sotto la chat riporta consumo e quota residua:

- **Claude**: valori reali dagli header `anthropic-ratelimit-*`, intercettati da un `DelegatingHandler` (`AnthropicRateLimitHandler`).
- **Gemini**: token da `usageMetadata` delle risposte; quota residua stimata localmente (Google non la espone via API, verificabile in AI Studio).

Il tracker (`ModelUsageTracker`) è in memoria e si azzera al riavvio.

**Risposte non ancorate** — se un client risponde con normativa nazionale generica anziché con i dati del corpus, non inserire i dati nel prompt: rinforzare le regole di comportamento e mostrare i passaggi recuperati accanto alla risposta (la UI lo fa già con il riquadro "Fonti recuperate").

## Note

Prototipo dimostrativo, non pronto per la produzione. I dettagli implementativi (bridge Gemini, gestione rate limit, formato del corpus) sono candidati a una futura cartella `docs/`.
