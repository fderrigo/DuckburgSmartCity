# ChattyDuck — Duckburg Smart City

Assistente del cittadino su fonte certificata via **MCP** (Model Context Protocol).
L'ente possiede la fonte, non l'assistente: un server MCP espone i contenuti certificati
del Comune di Paperopoli (immaginario) e i modelli AI rispondono usando **solo** quei
contenuti, citando sempre id e versione dei passaggi.

> Prototipo dimostrativo.

## Architettura

| Progetto | Ruolo |
|---|---|
| `Duckburg.Registry` | Server MCP dell'ente (porta 5000). Minimal API, trasporto Streamable HTTP su `/mcp`. Carica `corpus/out/corpus.json` in memoria (`CorpusService`, sola lettura). Espone il tool `cerca(query, limite)` e le risorse del corpus. Token di accesso opzionale. |
| `Duckburg.Portal` | Portale del Comune (porta 5100). Razor Pages in stile Designers Italia; monta l'assistente su tutte le pagine come widget e su `/assistente` a pagina intera. |
| `ChattyDuck.Quack` | Libreria RCL dell'assistente: UI chat (pagina + widget), endpoint `POST /chat`, `GET /chat/usage`, `GET /debug/tools`, orchestratore dei modelli. |
| `ChattyDuck.Models` | Servizi modello intercambiabili (`IModelService`): Gemini e Claude. Include il tracker d'uso (`ModelUsageTracker`) e la cattura degli header rate-limit Anthropic. |
| `ChattyDuck.Mcp` | Client MCP verso il Registry (`McpGateway`), usato dal ponte Gemini. |

### L'asimmetria del ponte (il punto della demo)

- **Percorso Gemini** (`GeminiModelService`): Gemini NON è MCP-nativo. Il ponte sta nel
  portale: `McpGateway` elenca i tool del Registry, il servizio li traduce in
  `functionDeclarations` per Gemini ed esegue le chiamate riportando i risultati
  come `functionResponse`.
- **Percorso Claude** (`ClaudeModelService`): Claude È MCP-nativo. Si passa l'endpoint
  pubblico del server nel parametro `mcp_servers` della Messages API (connettore MCP,
  beta `mcp-client-2025-11-20`): il modello si collega da solo. Niente ponte.

Il prompt di sistema (`SystemPrompt.cs`) impone solo il comportamento: il contenuto
sta SOLO nel corpus, mai nel prompt.

### Stato d'uso dei modelli

Il pannello "Limiti di utilizzo" sotto la chat mostra consumo e residuo:

- **Claude**: valori reali dagli header `anthropic-ratelimit-*` delle risposte API,
  catturati da un `DelegatingHandler` (`AnthropicRateLimitHandler`).
- **Gemini**: token da `usageMetadata` delle risposte; il residuo è una stima locale
  (Google non espone la quota via API, si verifica in AI Studio).

Il tracker è in memoria e si azzera al riavvio.

## Configurazione (chiavi MAI nel repository)

I file `appsettings*.json` reali sono esclusi da git: in remoto vanno solo i template.
Al primo avvio copia i template e inserisci i valori:

```powershell
Copy-Item Duckburg.Portal\appsettings.template.json Duckburg.Portal\appsettings.json
Copy-Item Duckburg.Registry\appsettings.template.json Duckburg.Registry\appsettings.json
```

| Chiave (Portal) | Env | Note |
|---|---|---|
| `Gemini:ApiKey` | `Gemini__ApiKey` | Google AI Studio, free tier |
| `Gemini:Model` | — | default `gemini-2.5-flash` |
| `Anthropic:ApiKey` | `Anthropic__ApiKey` | console Anthropic, a consumo |
| `Anthropic:Model` | — | es. `claude-haiku-4-5` |
| `Anthropic:McpEndpoint` | `Anthropic__McpEndpoint` | **URL pubblico** del server MCP (es. ngrok). I server Anthropic devono raggiungerlo: localhost non funziona. |
| `Registry:McpEndpoint` | — | endpoint MCP del ponte Gemini (default `http://localhost:5000/mcp`) |

| Chiave (Registry) | Note |
|---|---|
| `Corpus:Path` | percorso di `corpus.json` (default `../corpus/out/corpus.json`) |
| `Registry:AccessToken` | opzionale; se valorizzato richiede `Authorization: Bearer <token>` o `X-Access-Token` |

In alternativa ai file: variabili d'ambiente o `dotnet user-secrets`.

## Avvio

```powershell
# 1. Server MCP (porta 5000)
dotnet run --project Duckburg.Registry

# 2. Portale (porta 5100) -> http://localhost:5100
dotnet run --project Duckburg.Portal
```

In Visual Studio: profilo di avvio multiplo "Portal + MCP" (`DuckburgSmartCity.slnLaunch`).

Verifica rapida senza chiavi:

- `GET http://localhost:5100/debug/tools` → elenco tool MCP visti dal ponte
- `GET http://localhost:5100/chat/usage` → stato d'uso dei modelli

## Esposizione del server MCP

Il percorso Claude e i client MCP esterni richiedono un endpoint pubblico:

- Sviluppo: `ngrok http 5000` → endpoint `https://<sottodominio>.ngrok-free.dev/mcp`
  (da riportare in `Anthropic:McpEndpoint`; cambia a ogni riavvio del tunnel).
- Produzione: dominio dedicato dietro reverse proxy, ambienti separati.

## Collegare un chatbot esterno (zero codice)

Qualunque client MCP può usare il corpus. Dalla pagina `/assistente`, la voce
"⚙ Configura il tuo chatbot" del selettore modello mostra endpoint e configurazione:

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

## Scene di verifica

Su tutti i client:

1. "Quando scade la prima rata della TARI?" → 30 aprile, cita `tari:p02`.
2. "Quali sono le aliquote IMU?" → valori di Paperopoli (`imu:p02`), citati.
3. "Che giorno passa l'umido nel quartiere Vesuvio?" → "Questa informazione non è nelle fonti."
4. "Come prenoto la carta d'identità?" → procedura da `carta-identita-residenza:p01/p02`.
5. Stessa domanda ai client → stessa risposta ancorata.

Test manuale del tool `cerca` (senza modelli):

```bash
curl -s http://localhost:5000/mcp \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"cerca","arguments":{"query":"prima rata TARI"}}}'
```

Se un client risponde con regole nazionali generiche: NON mettere i dati nel prompt;
rinforza le regole di comportamento e mostra i passaggi recuperati accanto alla risposta
(la UI lo fa già con il riquadro "Fonti recuperate").
