using Duckburg.Ingestione;
using Duckburg.Ingestione.Mappatura;
using Duckburg.Portal.Cms;
using Microsoft.EntityFrameworkCore;

var unaVolta = args.Contains("--una-volta", StringComparer.OrdinalIgnoreCase);

var builder = WebApplication.CreateBuilder(args);

// Un temporizzatore non e' un sito.
//
// IIS presume che il lavoro nasca da una richiesta: carica l'applicazione alla prima,
// la scarica dopo qualche minuto di silenzio, la ricicla ogni notte. Per un servizio
// che si sveglia da solo e che nessuno chiama mai, quelle regole significano non
// partire affatto. La resilienza scritta nel Pianificatore e' corretta e non verrebbe
// mai eseguita.
//
// Come servizio Windows invece parte al boot, si riavvia se cade, e non ha nessuno che
// decida di scaricarlo. UseWindowsService non fa nulla quando il processo non e' un
// servizio, quindi lo stesso binario resta eseguibile da console e ospitabile in IIS.
builder.Host.UseWindowsService(opzioni => opzioni.ServiceName = "Duckburg Ingestione");

// Su Linux lo stesso ruolo lo fa systemd (unita' di riferimento in linux/duckburg-ingestione.service).
// UseSystemd segnala a systemd quando il servizio e' pronto (Type=notify: chi dipende da lui parte dopo, e un
// avvio fallito si vede subito), scrive i log nel formato di journald con il livello riconosciuto, e chiude in
// modo ordinato su SIGTERM. Come UseWindowsService, non fa nulla quando il processo non e' sotto systemd:
// lo stesso binario gira come servizio Windows, come unita' systemd o da console.
builder.Host.UseSystemd();

// Fuori da IIS nessuno assegna l'indirizzo. Loopback: l'unico endpoint esposto e'
// l'innesco manuale, e non deve uscire dalla macchina.
if (string.IsNullOrWhiteSpace(builder.Configuration["Urls"]) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls("http://127.0.0.1:5250");

// Adattatore fra il CMS di Paperopoli e il corpus.
//
// E' il progetto che si riscrive per ogni cliente: e' l'unico che conosce le tabelle di
// partenza. Legge il CMS, traduce nel vocabolario del modello Comuni e spinge
// un'istantanea intera sul corpus. Non conosce ne' il Registry ne' l'assistente.
//
// Sta in piedi come servizio web solo per due ragioni: il pianificatore interno e
// l'innesco manuale, che serve a chi pubblica una scheda e non vuole aspettare il giro.

// Il CMS si legge in sola lettura, con la stessa configurazione del portale: un
// adattatore per un altro CMS metterebbe qui il proprio modo di leggerlo.
builder.Services.AddDbContext<CmsDbContext>(db =>
{
    var provider = (builder.Configuration["Ingestione:Cms:Provider"] ?? "Sqlite").Trim().ToLowerInvariant();
    var cs = builder.Configuration["Ingestione:Cms:ConnectionString"]
             ?? "Data Source=../Duckburg.Portal/App_Data/paperopoli-cms.db";

    // Un percorso relativo si ancora alla radice del contenuto, non alla directory del
    // processo: sotto IIS quella e' C:\Windows\System32\inetsrv, e il database non
    // verrebbe trovato con un errore che sembra di permessi e non lo e'.
    if (provider == "sqlite") cs = AncoraSqlite(cs, builder.Environment.ContentRootPath);

    switch (provider)
    {
        case "sqlite": db.UseSqlite(cs); break;
        case "sqlserver" or "mssql": db.UseSqlServer(cs); break;
        case "postgres" or "postgresql" or "npgsql": db.UseNpgsql(cs); break;
        case "mysql" or "mariadb": db.UseMySql(cs, ServerVersion.AutoDetect(cs)); break;
        default: throw new InvalidOperationException($"Provider CMS non riconosciuto: '{provider}'.");
    }
    db.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

builder.Services.AddScoped<MappaturaDuckburgCms>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ServizioIngestione>();
if (!unaVolta) builder.Services.AddHostedService<Pianificatore>();

var app = builder.Build();

// Esecuzione singola: per un'attivita' pianificata dall'esterno, o per vedere subito
// cosa succede senza aprire un log. Esce con 1 se l'ingestione non e' riuscita, cosi'
// chi la pianifica se ne accorge.
if (unaVolta)
{
    var servizioUnaVolta = app.Services.GetRequiredService<ServizioIngestione>();
    var esitoUnaVolta = await servizioUnaVolta.Esegui(CancellationToken.None);
    Console.WriteLine(esitoUnaVolta.Riuscita
        ? $"Riuscita: versione {esitoUnaVolta.Versione}, {esitoUnaVolta.Contenuti} contenuti, "
          + $"{esitoUnaVolta.Sezioni} sezioni, {esitoUnaVolta.Avvisi.Count} avvisi."
        : $"Fallita: {esitoUnaVolta.Errore}");
    return esitoUnaVolta.Riuscita ? 0 : 1;
}

// Innesco manuale: senza, chi pubblica una scheda aspetta il giro successivo senza
// sapere quando arriva.
app.MapPost("/esegui", async (ServizioIngestione servizio, CancellationToken ct) =>
{
    var esito = await servizio.Esegui(ct);
    return esito.Riuscita
        ? Results.Ok(new
        {
            riuscita = true, versione = esito.Versione,
            contenuti = esito.Contenuti, sezioni = esito.Sezioni,
            avvisi = esito.Avvisi, durata_ms = (int)esito.Durata.TotalMilliseconds,
        })
        : Results.Json(new { riuscita = false, errore = esito.Errore }, statusCode: 502);
});

app.MapGet("/", (ServizioIngestione servizio, IConfiguration cfg) =>
{
    var u = servizio.Ultima;
    return Results.Ok(new
    {
        servizio = "Duckburg.Ingestione",
        descrizione = "Adattatore fra il CMS di Paperopoli e il corpus.",
        ente = cfg["Ingestione:IdEnte"] ?? "comune-paperopoli",
        corpus = cfg["Ingestione:UrlCorpus"],
        intervallo_minuti = cfg.GetValue<int?>("Ingestione:IntervalloMinuti") ?? 15,
        ultima_esecuzione = u is null ? null : new
        {
            istante = u.Istante, riuscita = u.Riuscita, versione = u.Versione,
            contenuti = u.Contenuti, sezioni = u.Sezioni,
            avvisi = u.Avvisi, errore = u.Errore, durata_ms = (int)u.Durata.TotalMilliseconds,
        },
    });
});

app.MapGet("/health", (ServizioIngestione servizio) =>
{
    var u = servizio.Ultima;
    var stato = u is null ? "avvio" : u.Riuscita ? "ok" : "errore";
    var messaggio = u is null
        ? "Prima ingestione non ancora eseguita: allineamento in corso."
        : u.Riuscita
            ? $"Ultima ingestione riuscita: {u.Contenuti} contenuti, {u.Sezioni} sezioni."
            : $"Ultima ingestione fallita: {u.Errore}";

    var corpo = new { stato, messaggio, ultima = u?.Istante, riuscita = u?.Riuscita };
    // 503 finche' non c'e' stata una prima ingestione riuscita: per chi sorveglia e'
    // la differenza fra "sta partendo" e "funziona".
    return u is { Riuscita: true }
        ? Results.Ok(corpo)
        : Results.Json(corpo, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();
return 0;

static string AncoraSqlite(string cs, string contentRoot)
{
    const string chiave = "Data Source=";
    var idx = cs.IndexOf(chiave, StringComparison.OrdinalIgnoreCase);
    if (idx < 0) return cs;

    var inizio = idx + chiave.Length;
    var fine = cs.IndexOf(';', inizio);
    var percorso = (fine >= 0 ? cs[inizio..fine] : cs[inizio..]).Trim();

    // I nomi speciali di SQLite non sono percorsi.
    if (percorso.Length == 0 ||
        percorso.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
        percorso.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
        Path.IsPathRooted(percorso))
        return cs;

    percorso = Path.GetFullPath(Path.Combine(contentRoot, percorso));
    return string.Concat(cs.AsSpan(0, inizio), percorso, fine >= 0 ? cs.AsSpan(fine) : "");
}
