# Runbook di deploy

Sequenza esecutiva. Il ragionamento dietro le scelte sta in `DEPLOY.md`.

- Federazione: `217.61.62.212` (KundividiSQL)
- IIS: `85.235.150.245`

Legenda: **[L]** macchina locale Windows, **[F]** server federazione, **[I]** server IIS,
**[DNS]** pannello DNS.

---

## FASE A - Preparazione, sulla macchina locale

### A1 [L] Chiavi nuove del Relying Party

```powershell
.\scripts\genera-chiavi-rp.ps1 -Destinazione C:\temp\rp_private_keys.prod.json
```

Il file NON va committato. Serve alla fase C3.

### A2 [L] Entity id da http con porta a https

Da Git Bash, nella radice del repository:

```bash
grep -rl "paperopoli.derrigo.it:800" infra/ Duckburg.Identity/ | xargs sed -i \
  -e 's|http://identity.paperopoli.derrigo.it:8001|https://identity.paperopoli.derrigo.it|g' \
  -e 's|http://trust-anchor.paperopoli.derrigo.it:8000|https://trust-anchor.paperopoli.derrigo.it|g' \
  -e 's|http://cie-provider.paperopoli.derrigo.it:8002|https://cie-provider.paperopoli.derrigo.it|g'

grep -rn "paperopoli.derrigo.it:800" infra/ Duckburg.Identity/ || echo "nessun residuo"
```

### A3 [L] Identity: provider di login e URL SSO

In `Duckburg.Identity/appsettings.json`, dentro `Oidc`, aggiungi il blocco che oggi
sta solo in `appsettings.Development.json`. Senza questo, in Production la pagina di
login esce senza pulsanti.

```jsonc
"Oidc": {
  "LocalProviders": [
    { "Profile": "spid", "Name": "BeccoID", "EntityId": "https://trust-anchor.paperopoli.derrigo.it/oidc/op" },
    { "Profile": "cie",  "Name": "IPZP",    "EntityId": "https://cie-provider.paperopoli.derrigo.it/oidc/op" }
  ],
  "ProductionProviders": [ /* invariato */ ]
}
```

Nello stesso file:

```jsonc
"Sso": {
  "TokenAudience": "duckburg-servizionline",
  "TokenLifetimeSeconds": 120,
  "AllowedReturnUrls":     [ "https://servizionline.paperopoli.derrigo.it/auth/callback" ],
  "AllowedPostLogoutUrls": [ "https://servizionline.paperopoli.derrigo.it/" ]
}
```

### A4 [L] ServiziOnline

In `Duckburg.ServiziOnline/appsettings.template.json` e nel tuo `appsettings.json`:

```jsonc
"Sso": {
  "IdentityBaseUrl": "https://identity.paperopoli.derrigo.it",
  "Issuer":          "https://identity.paperopoli.derrigo.it",
  "Audience":        "duckburg-servizionline",
  "CallbackUrl":     "https://servizionline.paperopoli.derrigo.it/auth/callback",
  "PostLogoutUrl":   "https://servizionline.paperopoli.derrigo.it/"
},
"Registry": { "McpEndpoint": "https://paperopoli-mcp.derrigo.it/mcp" },
"Anthropic": { "McpEndpoint": "https://paperopoli-mcp.derrigo.it/mcp" }
```

### A5 [L] Registry: feed del corpus

In `Duckburg.Registry/appsettings.json`:

```jsonc
"Corpus": {
  "Path": "../corpus/out/corpus.json",
  "FeedUrl": "https://paperopoli.derrigo.it/api/corpus",
  "Merge": "Replace",
  "RefreshMinutes": 5
}
```

### A6 [L] Django dietro proxy

```bash
cat deploy/settingslocal-proxy.py >> infra/federation_authority/federation_authority/settingslocal.py
cat deploy/settingslocal-proxy.py >> infra/provider/provider/settingslocal.py
```

Le variabili in coda sovrascrivono quelle definite sopra: e' voluto.

### A7 [L] Verifica

```bash
dotnet build DuckburgSmartCity.sln -c Release

# I settingslocal.py devono restare Python valido dopo l'append.
python -c "import ast;[ast.parse(open(f,encoding='utf-8').read()) for f in ['infra/federation_authority/federation_authority/settingslocal.py','infra/provider/provider/settingslocal.py']];print('Python valido')"

# Gli appsettings contengono commenti // che ASP.NET Core accetta: un parser JSON
# stretto li rifiuta, e togliere i // romperebbe gli URL. Quindi si controllano
# i valori attesi, non la sintassi.
grep -c "https://identity.paperopoli.derrigo.it"   Duckburg.ServiziOnline/appsettings.json
grep -c "https://paperopoli.derrigo.it/api/corpus" Duckburg.Registry/appsettings.json
grep -c "LocalProviders"                           Duckburg.Identity/appsettings.json
grep -rn "paperopoli.derrigo.it:800" infra/ Duckburg.Identity/ Duckburg.ServiziOnline/ || echo "nessun residuo con porta"
```

Ogni `grep -c` deve restituire almeno 1. L'ultimo comando non deve trovare nulla.

**La federazione in locale ora non parte piu'**, ed e' corretto: gli entity id
puntano a hostname https pubblici che in locale non esistono. Non e' un errore da
inseguire. La verifica vera e' in fase C6.

### A8 [L] Commit e push

```bash
git add -A
git commit -m "Configurazione di produzione: entity id https, feed del corpus, proxy Django"
git push
```

Le chiavi in `C:\temp` e i file `secrets/` restano fuori: sono gia' ignorati.

---

## FASE B - DNS

### B1 [DNS] Record

TTL 300. Sette record di tipo A:

| Nome | Valore |
|---|---|
| `paperopoli.derrigo.it` | `85.235.150.245` (esiste) |
| `paperopoli-mcp.derrigo.it` | `85.235.150.245` (esiste) |
| `servizionline.paperopoli.derrigo.it` | `85.235.150.245` |
| `valutazione.paperopoli.derrigo.it` | `85.235.150.245` |
| `identity.paperopoli.derrigo.it` | `217.61.62.212` |
| `trust-anchor.paperopoli.derrigo.it` | `217.61.62.212` |
| `cie-provider.paperopoli.derrigo.it` | `217.61.62.212` |

### B2 [L] Verifica prima di proseguire

```bash
for h in identity trust-anchor cie-provider; do echo -n "$h: "; dig +short $h.paperopoli.derrigo.it; done
for h in servizionline valutazione; do echo -n "$h: "; dig +short $h.paperopoli.derrigo.it; done
```

I primi tre devono dare `217.61.62.212`, gli altri due `85.235.150.245`.
Non avviare Caddy prima: Let's Encrypt limita i tentativi falliti.

---

## FASE C - Server federazione, 217.61.62.212

### C1 [F] Verifica preliminare

```bash
free -h                            # servono ~800 MB disponibili
df -h /                            # servono ~3 GB liberi
ss -tln | grep -E ':(80|443)\s' || echo "80 e 443 libere"
/opt/mssql/bin/mssql-conf list | grep -i memorylimit
```

Se la memoria disponibile e' sotto 800 MB, fermati e libera qualcosa prima.

### C2 [F] Codice

```bash
sudo mkdir -p /opt/paperopoli && cd /opt/paperopoli
sudo git clone <url-del-repo> .
```

### C3 [F] Chiavi private

Dalla macchina locale:

```powershell
scp C:\temp\rp_private_keys.prod.json root@217.61.62.212:/tmp/rp.json
```

Sul server:

```bash
sudo mkdir -p /opt/paperopoli-secrets
sudo mv /tmp/rp.json /opt/paperopoli-secrets/rp_private_keys.json
sudo chmod 600 /opt/paperopoli-secrets/rp_private_keys.json
sudo chown root:root /opt/paperopoli-secrets/rp_private_keys.json
```

### C4 [F] Firewall

```bash
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw status
```

Verifica anche il firewall del provider: entrambi devono permettere 80 e 443.

### C5 [F] Avvio

```bash
cd /opt/paperopoli
docker compose -f deploy/docker-compose.prod.yml up -d --build
docker compose -f deploy/docker-compose.prod.yml ps
docker compose -f deploy/docker-compose.prod.yml logs -f caddy
```

Nei log di Caddy devono comparire tre certificati ottenuti. Esci con Ctrl-C.

### C6 [F] Verifica della federazione

```bash
for h in identity trust-anchor cie-provider; do
  echo "--- $h"
  curl -s https://$h.paperopoli.derrigo.it/.well-known/openid-federation | head -c 120
  echo
done
```

Tutte e tre devono restituire un JWT (una stringa che inizia con `eyJ`).

```bash
curl -sI https://identity.paperopoli.derrigo.it/oidc/rp/landing | head -3
```

### C7 [F] Ri-onboarding del RP sul Trust Anchor

Chiavi ed entity id sono cambiati, quindi la registrazione precedente non vale piu'.
Da browser su `https://trust-anchor.paperopoli.derrigo.it`, area di onboarding,
registra il RP `https://identity.paperopoli.derrigo.it`.

Verifica che la trust chain si risolva:

```bash
curl -s "https://trust-anchor.paperopoli.derrigo.it/fetch?sub=https://identity.paperopoli.derrigo.it" | head -c 120
```

### C8 [F] Consumo effettivo

```bash
docker stats --no-stream
free -h
```

Da riguardare dopo qualche giorno. Se un container sta stabilmente al proprio tetto,
alzalo o riduci il perimetro.

---

## FASE D - IIS: aggiornare Portal e Registry

E' il passo che porta online il corpus alimentato dal CMS. Il Portal in produzione
oggi e' quello pre-CMS: questa publish introduce database, seeding, cartella media e
area `/admin` raggiungibile da Internet.

### D1 [I] Rete di sicurezza

```powershell
New-Item -ItemType Directory -Force C:\backup | Out-Null
Copy-Item C:\Windows\System32\inetsrv\config\applicationHost.config `
          "C:\backup\applicationHost.config.$(Get-Date -f yyyyMMdd-HHmmss)"
Get-ChildItem IIS:\AppPools | Select-Object Name, State
Get-ChildItem IIS:\Sites | Select-Object Name, State, PhysicalPath
```

Annota i nomi effettivi degli app pool di Portal e Registry: servono nei passi
successivi. Nel seguito li chiamo `DuckburgPortal` e `DuckburgRegistry`.

### D2 [L] Publish

```powershell
dotnet publish Duckburg.Portal   -c Release -o publish\Duckburg.Portal
dotnet publish Duckburg.Registry -c Release -o publish\Duckburg.Registry
```

### D3 [I] Backup del database CMS, se esiste gia'

```powershell
Copy-Item "D:\sites\DuckburgSmartCity\Duckburg.Portal\App_Data\*.db" C:\backup\ -EA SilentlyContinue
```

### D4 [I] Stop, copia, permessi

```powershell
Stop-WebAppPool DuckburgPortal
Stop-WebAppPool DuckburgRegistry
```

Copia le due cartelle di publish sul server mantenendo `corpus\` fratello di
`Duckburg.Registry\`.

```powershell
icacls "D:\sites\DuckburgSmartCity\Duckburg.Portal\App_Data"      /grant "IIS AppPool\DuckburgPortal:(OI)(CI)M"
icacls "D:\sites\DuckburgSmartCity\Duckburg.Portal\wwwroot\media" /grant "IIS AppPool\DuckburgPortal:(OI)(CI)M"
```

Se le cartelle non esistono, creale prima. Senza il primo permesso il CMS non crea
il database e il sito non parte.

### D5 [I] Segreti come variabili d'ambiente

Nel `web.config` di `Duckburg.Portal`, dentro `<aspNetCore>`:

```xml
<environmentVariables>
  <environmentVariable name="Gemini__ApiKey" value="..." />
  <environmentVariable name="Anthropic__ApiKey" value="..." />
  <environmentVariable name="Cms__Admin__Password" value="password-lunga-generata" />
</environmentVariables>
```

Mai il default `paperopoli`.

### D6 [I] Restrizione IP sull'area admin

Se la feature "IP and Domain Restrictions" e' installata, limita `/admin` ai tuoi
indirizzi. Altrimenti resta la sola password del passo precedente.

### D7 [I] Avvio e verifica

```powershell
Start-WebAppPool DuckburgRegistry
Start-WebAppPool DuckburgPortal
```

```bash
curl -s https://paperopoli-mcp.derrigo.it/health
```

Atteso: `"sources":["cms:https://paperopoli.derrigo.it/api/corpus"]` con 44 o piu'
aree. Se mostra il file invece del CMS, il server non raggiunge il proprio dominio
pubblico: aggiungi al sito Portal un binding su `127.0.0.1:5100` e cambia `FeedUrl`.

```bash
curl -s https://paperopoli.derrigo.it/api/corpus | head -c 200
curl -s https://paperopoli-mcp.derrigo.it/mcp \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"cerca","arguments":{"query":"prima rata TARI"}}}'
```

---

## FASE E - IIS: ServiziOnline

### E1 [L] Publish

```powershell
dotnet publish Duckburg.ServiziOnline -c Release -o publish\Duckburg.ServiziOnline
```

### E2 [I] Sito e app pool

App pool `DuckburgServiziOnline`, .NET CLR Version **No Managed Code**, Start Mode
`AlwaysRunning`. Sito con host header `servizionline.paperopoli.derrigo.it`.
Variabili d'ambiente per le chiavi API come in D5.

### E3 [I] Certificato

```powershell
wacs.exe --target iis --siteid <ID_DEL_SITO> --installation iis
```

Indica esplicitamente il sito: su una macchina con molti siti un errore di targeting
sostituisce il certificato a un sito di terzi.

### E4 Verifica

```bash
curl -sI https://servizionline.paperopoli.derrigo.it | head -3
```

---

## FASE F - Verifica end-to-end

Da browser:

1. `https://paperopoli.derrigo.it` carica, il widget chat risponde citando le fonti.
2. `https://paperopoli.derrigo.it/admin`, login con la password nuova, modifica un
   contenuto non protetto, e la modifica compare in `/api/corpus` entro 5 minuti o
   subito dopo `curl -X POST https://paperopoli-mcp.derrigo.it/corpus/reload`.
3. `https://servizionline.paperopoli.derrigo.it`, card area personale, login CIE demo
   con `user / oidcuser`, atterraggio su `/area-personale`, logout.

Il punto 3 e' il giro che tocca tutti e quattro i container piu' IIS.

---

## FASE G - Dopo

- `robots.txt` con `Disallow: /` sui tre host della federazione: gia' coperto
  dall'header `X-Robots-Tag` nel Caddyfile, ma il file esplicito non guasta.
- `AllowedHosts` da `*` all'host effettivo su tutti i siti .NET.
- SQL Server su `0.0.0.0:1433`: da limitare agli IP che devono raggiungerlo. Ora che
  tre hostname pubblici puntano a quella macchina, comparira' nei log di Certificate
  Transparency, che gli scanner leggono.
- Modulo Valutazione: NON deployarlo finche' non e' fatto l'hardening (whitelist dei
  bersagli, heap di Node a 2048, scansione singola con timeout, id di job univoci).
- Backup ricorrente del file SQLite del CMS e di `wwwroot/media/`: sono l'unico stato
  non rigenerabile.
