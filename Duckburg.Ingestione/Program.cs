using Duckburg.Ingestione;
using Duckburg.Ingestione.Mappatura;
using Duckburg.Portal.Cms;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHostedService<Pianificatore>();

var app = builder.Build();

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
    Results.Ok(new { status = "ok", ultima = servizio.Ultima?.Istante, riuscita = servizio.Ultima?.Riuscita }));

app.Run();
