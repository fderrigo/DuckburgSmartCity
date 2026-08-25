# Piano di deploy: DuckburgSmartCity

Versione consolidata dopo l'analisi dei due server. Sostituisce `publish/IIS_DEPLOY.md`,
che copriva solo Portal e Registry.

## Riparto delle componenti

| Dove | Cosa | Vincoli noti |
|---|---|---|
| Server IIS (Windows, 8 GB) | Portal, Registry, ServiziOnline, Valutazione | ospita altri siti di terzi, niente Docker, certificati con win-acme |
| Host Docker Linux | trust-anchor, cie-provider, identity, caddy | da decidere: vedi punto 1 |

`Duckburg.Identity` sta sul Linux insieme alla federazione: e' gia' nel compose, deve
risolvere la trust chain, e Caddy gli fa il TLS insieme agli altri due. Ha gia'
`UseForwardedHeaders` con `KnownProxies.Clear()`, quindi dietro proxy funziona senza
modifiche.

`Duckburg.DockerLaunch` non si deploya: e' un helper di avvio per Visual Studio.

---

## 1. Decidere l'host della federazione

KundividiSQL con 3,8 GB non basta. Il bilancio reale, dopo l'incidente:

| Voce | RAM |
|---|---|
| SQL Server a 2048 MB, minimo supportato, piu' overhead | ~2,3 GB |
| n8n | ~0,3 GB |
| Docker, containerd, sistema | ~0,5 GB |
| Totale gia' impegnato | ~3,1 GB su 3,8 |

I quattro container ne vogliono circa 0,65: non ci stanno con margine sufficiente.
Due strade:

- **Portare KundividiSQL a 8 GB.** E' una VM KVM, di solito e' una modifica di piano.
  Ha senso comunque: la macchina e' sottodimensionata per quello che gia' fa.
- **VPS separato da 2 GB** dedicato ai container.

Il resto del piano e' identico nei due casi. Chiamo `IP_FEDERAZIONE` l'indirizzo
dell'host scelto e `IP_IIS` quello del server Windows.

Prima di proseguire, sull'host scelto:

```bash
free -h        # servono almeno 1,5 GB disponibili stabili
df -h /        # servono almeno 3 GB liberi
```

---

## 2. Preparare il codice, in locale

Tutto quanto segue si fa sulla macchina di sviluppo e si verifica prima di toccare
i server.

### 2.1 Entity id da http con porta a https

Un entity id di OpenID Federation e' l'URL dove l'entita' serve la propria
configuration, e deve coincidere esattamente.

```bash
grep -rl "paperopoli.derrigo.it:800" infra/ Duckburg.Identity/ | xargs sed -i \
  -e 's|http://identity.paperopoli.derrigo.it:8001|https://identity.paperopoli.derrigo.it|g' \
  -e 's|http://trust-anchor.paperopoli.derrigo.it:8000|https://trust-anchor.paperopoli.derrigo.it|g' \
  -e 's|http://cie-provider.paperopoli.derrigo.it:8002|https://cie-provider.paperopoli.derrigo.it|g'

grep -rn "paperopoli.derrigo.it:800" infra/ Duckburg.Identity/ || echo "nessun residuo"
```

I `db.sqlite3` della federazione non sono versionati: sul server nascono puliti dai
dump appena corretti.

### 2.2 Provider di login visibili in Production

`HomeController.Landing` costruisce i pulsanti solo da `Oidc:LocalProviders`, che oggi
vive in `appsettings.Development.json`. Sotto un ambiente Production quel file non si
carica e la pagina di login esce senza pulsanti.

Sposta i provider di Palmipedia in `Duckburg.Identity/appsettings.json`, sotto
`Oidc:LocalProviders`, con gli entity id https. `Oidc:ProductionProviders` resta dov'e'
e resta non renderizzato: la landing non deve mostrare i gestori SPID reali.

Nello stesso file aggiorna le URL SSO:

```jsonc
"Sso": {
  "AllowedReturnUrls":     [ "https://servizionline.paperopoli.derrigo.it/auth/callback" ],
  "AllowedPostLogoutUrls": [ "https://servizionline.paperopoli.derrigo.it/" ]
}
```

### 2.3 Feed del corpus per il Registry

Sotto IIS i siti rispondono su 443 per host header, non su `localhost:5100`.

```jsonc
// Duckburg.Registry/appsettings.json
"Corpus": {
  "Path": "../corpus/out/corpus.json",
  "FeedUrl": "https://paperopoli.derrigo.it/api/corpus",
  "Merge": "Replace",
  "RefreshMinutes": 5
}
```

Il server deve poter raggiungere il proprio dominio pubblico. Se il giro esterno non
funziona, aggiungi al sito Portal un binding interno su `127.0.0.1:5100` e punta li'.

### 2.4 Hardening del modulo Valutazione

Da fare prima di esporlo, in `Duckburg.Valutazione`:

1. **Whitelist dei bersagli**: `POST /avvia` accetta solo host che terminano con
   `paperopoli.derrigo.it`, con eccezione per `localhost` in sviluppo. Senza questa,
   il modulo e' un crawler pubblico puntabile ovunque.
2. **Heap di Node**: `--max-old-space-size=8192` autorizza 8 GB di heap. Su macchine
   da 4 e 8 GB va portato a 2048.
3. **Una scansione alla volta**, con timeout oltre il quale il processo viene terminato.
4. **Id di job non collidenti**: oggi e' `yyyyMMdd-HHmmss`, due avvii nello stesso
   secondo si sovrascrivono.

### 2.5 Verifica locale

```bash
dotnet build DuckburgSmartCity.sln -c Release
docker compose up --build      # la federazione deve funzionare con i valori nuovi
bash e2e_test.sh               # da parametrizzare sugli host nuovi
```

Non si tocca alcun server finche' questo passo non e' verde.

---

## 3. IIS: aggiornare Portal e Registry

Indipendente dalla federazione, ed e' la parte che porta online il corpus alimentato
dal CMS. Farla per prima significa avere il contenuto pubblicato anche se il resto
si allunga.

Attenzione: il Portal in produzione oggi e' quello **pre-CMS**. Non e' una publish
incrementale, porta online per la prima volta il database, il seeding, la cartella
media e l'area `/admin` raggiungibile da Internet.

### 3.1 Rete di sicurezza

```powershell
Copy-Item C:\Windows\System32\inetsrv\config\applicationHost.config `
          C:\backup\applicationHost.config.$(Get-Date -f yyyyMMdd-HHmmss)
```

E' la configurazione di tutti i siti del server, inclusi quelli di terzi: e' il rollback.

### 3.2 Publish

```powershell
dotnet publish Duckburg.Portal   -c Release -o publish\Duckburg.Portal
dotnet publish Duckburg.Registry -c Release -o publish\Duckburg.Registry
```

Struttura sul server, con `corpus/` fratello di `Duckburg.Registry/`:

```
D:\sites\DuckburgSmartCity\
  Duckburg.Portal\
  Duckburg.Registry\
  corpus\out\corpus.json
```

### 3.3 Segreti come variabili d'ambiente

Nessuna chiave in `appsettings.json` sul server.

| Sito | Variabile |
|---|---|
| Portal | `Gemini__ApiKey`, `Anthropic__ApiKey`, `Cms__Admin__Password` |
| Registry | `Registry__AccessToken` se decidi di chiudere `/mcp` |

Chiavi API di produzione distinte da quelle di sviluppo: consumi separati e revoca
indipendente.

### 3.4 Permessi di scrittura

```powershell
icacls "D:\sites\DuckburgSmartCity\Duckburg.Portal\App_Data" /grant "IIS AppPool\DuckburgPortal:(OI)(CI)M"
icacls "D:\sites\DuckburgSmartCity\Duckburg.Portal\wwwroot\media" /grant "IIS AppPool\DuckburgPortal:(OI)(CI)M"
```

Senza il primo, il CMS non crea il database SQLite e il sito non parte.

### 3.5 Restrizione IP sull'area admin

`/admin` e' un'area di amministrazione raggiungibile da Internet, protetta solo da
una password in configurazione. Con la feature "IP and Domain Restrictions" di IIS,
limitala ai tuoi indirizzi. Se la feature non e' installata, resta almeno una
password lunga e generata, mai il default `paperopoli`.

### 3.6 Deploy e verifica

```powershell
Stop-WebAppPool DuckburgPortal; Stop-WebAppPool DuckburgRegistry
# copia dei file
Start-WebAppPool DuckburgRegistry; Start-WebAppPool DuckburgPortal
```

```bash
curl -s https://paperopoli-mcp.derrigo.it/health
# atteso: "sources":["cms:https://paperopoli.derrigo.it/api/corpus"], works 44+
curl -s https://paperopoli.derrigo.it/api/corpus | head -c 300
```

Se `sources` mostra il file invece del CMS, il Registry non raggiunge il Portal:
vedi 2.3.

---

## 4. DNS

Abbassa il TTL a 300 qualche ora prima.

| Nome | Tipo | Valore | Stato |
|---|---|---|---|
| `paperopoli.derrigo.it` | A | `IP_IIS` | esiste |
| `paperopoli-mcp.derrigo.it` | A | `IP_IIS` | esiste |
| `servizionline.paperopoli.derrigo.it` | A | `IP_IIS` | nuovo |
| `valutazione.paperopoli.derrigo.it` | A | `IP_IIS` | nuovo |
| `identity.paperopoli.derrigo.it` | A | `IP_FEDERAZIONE` | nuovo |
| `trust-anchor.paperopoli.derrigo.it` | A | `IP_FEDERAZIONE` | nuovo |
| `cie-provider.paperopoli.derrigo.it` | A | `IP_FEDERAZIONE` | nuovo |

```bash
for h in identity trust-anchor cie-provider; do dig +short $h.paperopoli.derrigo.it; done
```

Verifica prima di procedere: Caddy chiede i certificati e Let's Encrypt limita i
tentativi falliti.

Firewall: 80 e 443 aperti verso `IP_FEDERAZIONE`, sia sul provider sia su `ufw`.

---

## 5. Chiavi nuove del Relying Party

`secrets/rp_private_keys.json` e' byte per byte identico al sample versionato su
GitHub. In locale non conta. Con il RP pubblico, quella e' la chiave che firma il
token SSO con cui ServiziOnline apre l'area personale: chiunque potrebbe emetterne
uno valido.

```powershell
.\scripts\genera-chiavi-rp.ps1 -Destinazione C:\temp\rp_private_keys.prod.json
```

```bash
scp C:\temp\rp_private_keys.prod.json root@IP_FEDERAZIONE:/opt/paperopoli-secrets/rp_private_keys.json
chmod 600 /opt/paperopoli-secrets/rp_private_keys.json
```

Il file non va committato e non va messo dentro la cartella del repository.

---

## 6. Federazione sull'host Docker

### 6.1 Codice sul server

```bash
sudo mkdir -p /opt/paperopoli && cd /opt/paperopoli
git clone <url-del-repo> .
```

### 6.2 Caddy davanti, porte chiuse dietro

Nel `docker-compose.yml` **togli i blocchi `ports:` dai tre servizi**: altrimenti
Django resta raggiungibile in chiaro su `IP_FEDERAZIONE:8000`. Poi aggiungi:

```yaml
  caddy:
    image: caddy:2-alpine
    ports: [ "80:80", "443:443" ]
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
    networks: [ oidcfed ]
    depends_on:
      - trust-anchor.paperopoli.derrigo.it
      - cie-provider.paperopoli.derrigo.it
      - identity.paperopoli.derrigo.it

volumes:
  caddy_data:
```

`Caddyfile`: i nomi dei servizi coincidono con gli hostname pubblici, quindi il DNS
interno di Docker li risolve da solo.

```
trust-anchor.paperopoli.derrigo.it {
	reverse_proxy trust-anchor.paperopoli.derrigo.it:8000
}
cie-provider.paperopoli.derrigo.it {
	reverse_proxy cie-provider.paperopoli.derrigo.it:8002
}
identity.paperopoli.derrigo.it {
	reverse_proxy identity.paperopoli.derrigo.it:8001
}
```

Nel servizio identity: `ASPNETCORE_ENVIRONMENT=Production` e
`Rp__PrivateKeysFile=/secrets/rp_private_keys.json`, con il volume montato da
`/opt/paperopoli-secrets`.

### 6.3 Django dietro un proxy https

In entrambi i `infra/*/settingslocal.py`, altrimenti Django genera URL http e i
redirect OIDC si rompono:

```python
SECURE_PROXY_SSL_HEADER = ("HTTP_X_FORWARDED_PROTO", "https")
USE_X_FORWARDED_HOST = True
CSRF_TRUSTED_ORIGINS = [
    "https://trust-anchor.paperopoli.derrigo.it",
    "https://cie-provider.paperopoli.derrigo.it",
]
ALLOWED_HOSTS = ["trust-anchor.paperopoli.derrigo.it", "cie-provider.paperopoli.derrigo.it"]
```

E' il punto che piu' probabilmente richiedera' un aggiustamento al primo avvio.

### 6.4 Avvio

```bash
cd /opt/paperopoli && docker compose up -d --build
docker compose ps
docker compose logs -f caddy      # i certificati arrivano in pochi secondi

for h in identity trust-anchor cie-provider; do
  echo "--- $h"
  curl -s https://$h.paperopoli.derrigo.it/.well-known/openid-federation | head -c 120
  echo
done
```

Le tre entity configuration devono rispondere con un JWT.

### 6.5 Ri-onboarding del RP

Chiavi ed entity id sono cambiati, quindi il Trust Anchor deve ri-registrare il RP
con il JWKS nuovo. Si fa dalla UI di onboarding su
`https://trust-anchor.paperopoli.derrigo.it`, oppure aggiornando il dump e rifacendo
`loaddata`.

---

## 7. IIS: ServiziOnline

```jsonc
// Duckburg.ServiziOnline/appsettings.json
"Sso": {
  "IdentityBaseUrl": "https://identity.paperopoli.derrigo.it",
  "Issuer":          "https://identity.paperopoli.derrigo.it",
  "CallbackUrl":     "https://servizionline.paperopoli.derrigo.it/auth/callback",
  "PostLogoutUrl":   "https://servizionline.paperopoli.derrigo.it/"
}
```

Publish, nuovo sito IIS con host header `servizionline.paperopoli.derrigo.it`, app pool
dedicato in No Managed Code, certificato con win-acme puntato su quel sito.

Con win-acme, indica esplicitamente il sito bersaglio: su una macchina con molti siti
un errore di targeting sostituisce il certificato a un sito di terzi.

---

## 8. IIS: Valutazione

Solo dopo l'hardening del punto 2.4. Richiede Node 18+ nel PATH dell'app pool e il
validatore compilato in `Duckburg.Valutazione/tool/`. Puppeteer scarica un proprio
Chrome: verifica spazio su disco e che l'app pool possa eseguirlo.

Permessi di scrittura su `reports/`, e una pulizia periodica: le scansioni `online`
producono report pesanti e la cartella cresce senza limite.

---

## 9. Igiene della demo pubblica

- `robots.txt` con `Disallow: /` su `identity`, `trust-anchor` e `cie-provider`.
  Una pagina di login indicizzata e' l'unico modo in cui qualcuno ci arriva senza
  contesto.
- Banner "ente immaginario" anche sulle pagine Django, come gia' presente sul portale.
- `AllowedHosts` e' `*` su tutti i siti .NET: valorizzalo con l'host di ciascuno.
- HSTS e redirect 80 verso 443.

La landing di Identity non mostra i pulsanti dei gestori SPID reali e le pagine della
federazione sono brandizzate come enti di Palmipedia: la distinzione da un login vero
e' gia' evidente.

---

## 10. Verifica end-to-end

```bash
curl -s https://paperopoli-mcp.derrigo.it/health
curl -s https://paperopoli.derrigo.it/api/corpus | head -c 300
curl -s https://paperopoli-mcp.derrigo.it/mcp \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"cerca","arguments":{"query":"prima rata TARI"}}}'
```

Da browser:

- Portale: home, una scheda servizio, il widget chat risponde citando le fonti.
- Admin CMS: login con la password nuova, modifica di un contenuto non protetto, e la
  modifica compare in `/api/corpus` entro `RefreshMinutes` o subito dopo
  `POST /corpus/reload`.
- Servizi online: card area personale, login CIE demo con `user / oidcuser`, atterraggio
  su `/area-personale`, logout.
- Valutazione: scansione su un dominio in whitelist, rifiuto su uno fuori whitelist.

---

## 11. Rollback

Ogni sito IIS e' una cartella: si torna indietro rinominando e riavviando l'app pool.
La federazione si riporta indietro con `docker compose down` e il commit precedente.

Due eccezioni:

- **Database CMS**: `EnsureCreated` non versiona lo schema. Se una release cambia le
  entita', il rollback del codice non riporta indietro il database. Backup del file
  SQLite prima di ogni deploy che tocca `Cms/Entities.cs`.
- **Dump della federazione**: il rollback degli entity id richiede di ricreare i
  container, non basta il rollback dei file.

---

## 12. Dopo il primo deploy

`.github/workflows/` esiste ma e' vuota. Una pipeline minima con `dotnet build` e
`dotnet publish` su push in `main`, con artefatti scaricabili, toglie il passaggio
manuale piu' soggetto a errori senza introdurre deploy automatici.

Backup: il file SQLite del CMS e `wwwroot/media/` sono l'unico stato non rigenerabile.
Tutto il resto si ricostruisce dal repository.
