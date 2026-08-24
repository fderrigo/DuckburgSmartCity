using System.Text;
using Duckburg.Valutazione;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ScanService>();

var app = builder.Build();

var scans = app.Services.GetRequiredService<ScanService>();

// I report generati dal validatore vengono serviti come file statici.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(scans.ReportsDir),
    RequestPath = "/reports",
    ServeUnknownFileTypes = true,
});

static string Pagina(string titolo, string corpo, int? refreshSeconds = null) => $$"""
<!DOCTYPE html>
<html lang="it">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
{{(refreshSeconds is int r ? $"<meta http-equiv=\"refresh\" content=\"{r}\" />" : "")}}
<title>{{titolo}} · Valutazione modello Comuni</title>
<style>
:root{--oro:#F2B705;--acqua:#1D9BB8;--rosso:#E23A2E;--inchiostro:#17120E;--carta:#FFF6E0;}
*{box-sizing:border-box;}
body{margin:0;font-family:'Titillium Web',system-ui,sans-serif;background:#f4efe1;color:var(--inchiostro);}
header{background:var(--inchiostro);color:#fff;padding:1rem 1.4rem;display:flex;align-items:center;gap:.7rem;flex-wrap:wrap;}
header h1{margin:0;font-size:1.25rem;} header small{color:#d9cfa4;}
header nav{margin-left:auto;display:flex;gap:1rem;}
header a{color:#FFD84D;text-decoration:none;font-weight:700;}
main{max-width:960px;margin:1.6rem auto;padding:0 1.2rem;}
.card{background:#fff;border:2px solid var(--inchiostro);border-radius:.7rem;padding:1.2rem;
    box-shadow:3px 3px 0 rgba(23,18,14,.85);margin-bottom:1.2rem;}
.btn{display:inline-block;background:var(--oro);color:var(--inchiostro);border:2px solid var(--inchiostro);
    border-radius:.5rem;padding:.5rem 1rem;font-weight:800;cursor:pointer;text-decoration:none;font-size:1rem;
    box-shadow:2px 2px 0 var(--inchiostro);}
.btn.ghost{background:#fff;}
label{display:block;font-weight:700;margin:.6rem 0 .25rem;}
input,select{width:100%;max-width:480px;padding:.5rem .6rem;border:2px solid var(--inchiostro);border-radius:.45rem;font-size:.95rem;font-family:inherit;}
table{width:100%;border-collapse:collapse;} th,td{text-align:left;padding:.5rem .6rem;border-bottom:1px solid #eadfbf;font-size:.92rem;}
pre.log{background:#17120E;color:#d7f2d7;padding:1rem;border-radius:.6rem;max-height:480px;overflow:auto;font-size:.8rem;white-space:pre-wrap;}
.stato{display:inline-block;padding:.15rem .7rem;border-radius:1rem;border:2px solid var(--inchiostro);font-weight:700;font-size:.8rem;}
.stato.ok{background:#cdeccd;} .stato.run{background:#ffe9a8;} .stato.ko{background:#f3d4d0;}
.warn{background:#efe6c6;border:2px solid var(--inchiostro);border-radius:.5rem;padding:.7rem 1rem;font-size:.92rem;}
h2{font-size:1.2rem;} dt{font-weight:700;margin-top:.6rem;} dd{margin:0 0 .3rem;}
</style>
</head>
<body>
<header>
    <span style="font-size:1.6rem;">🦆</span>
    <div><h1>Valutazione adesione al modello Comuni</h1>
    <small>Misura 1.4.1 · validatore ufficiale pa-website-validator-ng · Comune di Paperopoli (demo)</small></div>
    <nav><a href="/">Scansioni</a><a href="/deviazioni">Deviazioni dichiarate</a><a href="http://localhost:5100/">↗ Portale</a></nav>
</header>
<main>{{corpo}}</main>
</body>
</html>
""";

app.MapGet("/", (ScanService s) =>
{
    var sb = new StringBuilder();

    if (!s.ToolInstallato)
    {
        sb.Append("""
        <div class="warn"><strong>Validatore non installato.</strong>
        Esegui <code>scripts/setup-valutazione.sh</code> per clonare e compilare
        <code>italia/pa-website-validator-ng</code> in <code>Duckburg.Valutazione/tool</code>.</div>
        """);
    }

    sb.Append("""
    <div class="card">
        <h2>Nuova scansione</h2>
        <p>Esegue l'App di valutazione ufficiale (Lighthouse + Puppeteer) sul portale e produce
        il report dei criteri di conformità della misura 1.4.1, pacchetto Cittadino Informato.</p>
        <form method="post" action="/avvia">
            <label for="website">Sito da valutare</label>
            <input id="website" name="website" value="http://localhost:5100" required />
            <label for="accuracy">Accuratezza (numero di pagine analizzate)</label>
            <select id="accuracy" name="accuracy">
                <option value="min">min · veloce, poche pagine</option>
                <option value="suggested" selected>suggested · consigliata</option>
                <option value="high">high · approfondita</option>
                <option value="all">all · tutte le pagine</option>
            </select>
            <p><button class="btn" type="submit">Avvia la valutazione</button></p>
        </form>
        <p style="font-size:.85rem;color:#6b6257;">Nota: la scansione può richiedere diversi minuti e usa molta memoria.</p>
    </div>
    """);

    var attivi = s.JobsAttivi.Where(j => j.Status == ScanStatus.InCorso).ToList();
    if (attivi.Count > 0)
    {
        sb.Append("<div class=\"card\"><h2>Scansioni in corso</h2><table>");
        foreach (var j in attivi)
            sb.Append($"<tr><td>{j.StartedAt:dd/MM HH:mm}</td><td>{j.Website}</td>" +
                      $"<td><span class=\"stato run\">in corso</span></td>" +
                      $"<td><a class=\"btn ghost\" href=\"/scansione/{j.Id}\">Segui</a></td></tr>");
        sb.Append("</table></div>");
    }

    sb.Append("<div class=\"card\"><h2>Report generati</h2>");
    var report = s.ReportSalvati();
    if (report.Count == 0) sb.Append("<p>Nessun report ancora. Avvia la prima scansione!</p>");
    else
    {
        sb.Append("<table><tr><th>Data</th><th>Scansione</th><th></th></tr>");
        foreach (var (nome, file, data) in report)
            sb.Append($"<tr><td>{data:dd/MM/yyyy HH:mm}</td><td>{nome}</td>" +
                      $"<td><a class=\"btn\" href=\"/reports/{nome}/{file}\" target=\"_blank\">Apri report</a> " +
                      $"<a class=\"btn ghost\" href=\"/scansione/{nome}\">Log</a></td></tr>");
        sb.Append("</table>");
    }
    sb.Append("</div>");

    return Results.Content(Pagina("Scansioni", sb.ToString()), "text/html; charset=utf-8");
});

app.MapPost("/avvia", (HttpRequest req, ScanService s) =>
{
    var website = req.Form["website"].ToString();
    var accuracy = req.Form["accuracy"].ToString();
    if (string.IsNullOrWhiteSpace(website)) website = "http://localhost:5100";
    if (accuracy is not ("min" or "suggested" or "high" or "all")) accuracy = "suggested";
    var job = s.Start(website, accuracy);
    return Results.Redirect($"/scansione/{job.Id}");
});

app.MapGet("/scansione/{id}", (string id, ScanService s) =>
{
    var job = s.Get(id);
    string corpo;
    int? refresh = null;

    if (job == null)
    {
        // Scansione di una sessione precedente: mostra solo il report se esiste.
        var dir = Path.Combine(s.ReportsDir, id);
        var html = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.html").FirstOrDefault() : null;
        corpo = html != null
            ? $"<div class=\"card\"><h2>Scansione {id}</h2><p><a class=\"btn\" href=\"/reports/{id}/{Path.GetFileName(html)}\" target=\"_blank\">Apri report</a></p></div>"
            : "<div class=\"card\"><h2>Scansione non trovata</h2><p><a class=\"btn ghost\" href=\"/\">Torna all'elenco</a></p></div>";
    }
    else
    {
        var stato = job.Status switch
        {
            ScanStatus.Completata => "<span class=\"stato ok\">completata</span>",
            ScanStatus.Errore => "<span class=\"stato ko\">errore</span>",
            _ => "<span class=\"stato run\">in corso…</span>",
        };
        if (job.Status == ScanStatus.InCorso) refresh = 5;

        string log;
        lock (job.Log) log = job.Log.ToString();
        var reportLink = job.ReportFile != null
            ? $"<p><a class=\"btn\" href=\"/reports/{job.Id}/{job.ReportFile}\" target=\"_blank\">Apri il report</a></p>"
            : "";

        corpo = $"""
        <div class="card">
            <h2>Scansione {job.Id} {stato}</h2>
            <p><strong>Sito:</strong> {job.Website} · <strong>Accuratezza:</strong> {job.Accuracy} ·
               <strong>Avviata:</strong> {job.StartedAt:HH:mm:ss}</p>
            {reportLink}
            <pre class="log">{System.Net.WebUtility.HtmlEncode(log)}</pre>
            <p><a class="btn ghost" href="/">← Tutte le scansioni</a></p>
        </div>
        """;
    }

    return Results.Content(Pagina($"Scansione {id}", corpo, refresh), "text/html; charset=utf-8");
});

app.MapGet("/deviazioni", () =>
{
    const string corpo = """
    <div class="card">
        <h2>Deviazioni dichiarate dal modello Comuni</h2>
        <p>Il portale di Paperopoli aderisce al modello Comuni della misura 1.4.1 con le deviazioni
        consapevoli elencate qui sotto. In una asseverazione reale queste andrebbero sanate o motivate
        formalmente; in questa demo sono una scelta di progetto documentata.</p>

        <dl>
            <dt>C.SI.1.1 — Coerenza dell'utilizzo dei font</dt>
            <dd>
                <strong>Esito atteso: fallimento.</strong> Il criterio richiede che tutti i titoli usino
                esclusivamente Titillium Web, Lora o Roboto Mono. I paragrafi del portale usano Titillium Web
                (conforme), ma i titoli usano il carattere display «Bangers» in stile fumetto.
                <br /><strong>Perché:</strong> l'identità visiva di Paperopoli è volutamente a fumetto:
                il carattere dei titoli è parte essenziale del racconto dell'ente immaginario. La scelta è
                mantenuta e documentata; contrasto e leggibilità restano verificati (WCAG 2.1 AA).
                <br /><strong>Come rientrare:</strong> basterebbe rimuovere la variante «display» dal tema
                (una riga di CSS) per usare Titillium Web anche nei titoli.
            </dd>

            <dt>C.SI.3.2 — Dichiarazione di accessibilità</dt>
            <dd>
                <strong>Esito atteso: fallimento in ambiente demo.</strong> Il criterio richiede che il link
                del footer punti a una dichiarazione registrata su <code>form.agid.gov.it</code>: la
                registrazione presso AgID è riservata alle amministrazioni reali. Il portale espone una
                pagina che ricalca il modello della dichiarazione, dichiarando la conformità parziale
                alle WCAG 2.1, ma non può essere censita da AgID.
                <br /><strong>In produzione:</strong> un ente reale compila la dichiarazione su AgID e
                sostituisce l'URL del link nel footer (una voce di menu nel CMS).
            </dd>

            <dt>C.SI.5.1 / C.SI.5.2 — HTTPS e dominio istituzionale</dt>
            <dd>
                <strong>Esito atteso: fallimento in ambiente demo.</strong> Il portale gira in locale
                (http://localhost) e non usa il dominio <code>comune.[nome].[provincia].it</code>:
                Paperopoli è un ente immaginario e non può registrare un dominio istituzionale reale
                né ottenere un certificato pubblico.
                <br /><strong>In produzione:</strong> deploy su dominio istituzionale con TLS conforme
                alle raccomandazioni AgID renderebbe verdi entrambi i criteri senza modifiche al codice.
            </dd>

            <dt>C.SI.1.4 — Tema CMS</dt>
            <dd>
                <strong>Esito atteso: tolleranza.</strong> Il portale non usa i temi CMS del modello
                (WordPress/Drupal): usa un CMS proprio sviluppato ad hoc. Il criterio lo consente
                esplicitamente come condizione di tolleranza.
            </dd>
        </dl>
    </div>
    """;
    return Results.Content(Pagina("Deviazioni dichiarate", corpo), "text/html; charset=utf-8");
});

app.Run();
