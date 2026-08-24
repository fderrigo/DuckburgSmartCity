using System.Threading.RateLimiting;
using Duckburg.Registry.Corpus;
using Duckburg.Registry.Mcp;

var builder = WebApplication.CreateBuilder(args);

// Sorgenti del corpus, in ordine di autorevolezza: il feed del CMS del portale, se
// configurato, e il corpus statico su file come ripiego. Con Corpus:Merge = "Merge"
// i due si sommano invece di escludersi.
builder.Services.AddHttpClient();
if (!string.IsNullOrWhiteSpace(builder.Configuration["Corpus:FeedUrl"]))
    builder.Services.AddSingleton<ICorpusSource, HttpFeedCorpusSource>();
builder.Services.AddSingleton<ICorpusSource, FileCorpusSource>();
builder.Services.AddSingleton<CorpusService>();
builder.Services.AddHostedService<CorpusRefreshService>();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<CorpusTools>()
    .WithResources<CorpusResources>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.UseRateLimiter();

// Token di accesso opzionale per i test: attivo solo se Registry:AccessToken e' valorizzato.
var accessToken = app.Configuration["Registry:AccessToken"];
if (!string.IsNullOrWhiteSpace(accessToken))
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }
        var provided = context.Request.Headers.Authorization.ToString();
        var ok = provided == $"Bearer {accessToken}"
                 || context.Request.Headers["X-Access-Token"].ToString() == accessToken;
        if (!ok)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Token di accesso mancante o non valido.");
            return;
        }
        await next(context);
    });
}

app.MapGet("/health", (CorpusService corpus) => Results.Ok(new
{
    status = "ok",
    corpus_version = corpus.Document.CorpusVersion,
    works = corpus.Works.Count,
    passages = corpus.PassageCount,
    sources = corpus.SorgentiAttive,
    loaded_at = corpus.CaricatoIl,
}));

// Ricarico a caldo delle sorgenti: dopo una pubblicazione nel CMS rende subito
// disponibile il contenuto nuovo, senza riavviare il server MCP.
// Protetto dal middleware di Registry:AccessToken quando il token e' configurato.
app.MapPost("/corpus/reload", async (CorpusService corpus, CancellationToken ct) =>
{
    var ok = await corpus.ReloadAsync(obbligatorio: false, ct);
    return Results.Ok(new
    {
        reloaded = ok,
        corpus_version = corpus.Document.CorpusVersion,
        works = corpus.Works.Count,
        passages = corpus.PassageCount,
        sources = corpus.SorgentiAttive,
    });
});

app.MapMcp("/mcp");

// Primo caricamento prima di servire richieste: se nessuna sorgente risponde, meglio
// non partire affatto che rispondere su un corpus vuoto.
await app.Services.GetRequiredService<CorpusService>().ReloadAsync(obbligatorio: true);

app.Run();
