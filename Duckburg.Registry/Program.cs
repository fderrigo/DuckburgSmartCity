using System.Threading.RateLimiting;
using Duckburg.Registry.Corpus;
using Duckburg.Registry.Mcp;

var builder = WebApplication.CreateBuilder(args);

// Server MCP dell'ente: espone il corpus ai modelli.
//
// Non possiede i dati. Li legge dal servizio del corpus, che e' l'unico a scriverli, e
// ne tiene in memoria un indice. Cosi' si possono avere piu' server MCP sullo stesso
// corpus, e riavviarne uno non tocca nulla.
//
// I client sono tre e due non sono nostri: ChattyDuck fa da ponte per Gemini, i server
// di Anthropic si collegano da soli per Claude, e qualunque client MCP di terzi puo'
// consumare lo stesso endpoint. Per questo deve essere pubblico.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ServizioCorpus>();
builder.Services.AddHostedService<RiallineamentoPeriodico>();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<StrumentiCorpus>()
    .WithResources<RisorseCorpus>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

app.UseRateLimiter();

// Token di accesso opzionale: attivo solo se Registry:AccessToken e' valorizzato.
var accessToken = app.Configuration["Registry:AccessToken"];
if (!string.IsNullOrWhiteSpace(accessToken))
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/health")) { await next(context); return; }
        var fornito = context.Request.Headers.Authorization.ToString();
        var ok = fornito == $"Bearer {accessToken}"
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

app.MapGet("/health", (ServizioCorpus corpus) => Results.Ok(new
{
    status = corpus.Pronto ? "ok" : "in attesa del corpus",
    ente = corpus.Pronto ? corpus.Indice.Istantanea.Ente.Id : null,
    versione = corpus.Versione,
    contenuti = corpus.Pronto ? corpus.Indice.NumeroContenuti : 0,
    sezioni = corpus.Pronto ? corpus.Indice.NumeroSezioni : 0,
    caricato_il = corpus.CaricatoIl,
}));

// Riallineamento immediato: dopo una pubblicazione nel CMS evita di aspettare il giro.
app.MapPost("/corpus/reload", async (ServizioCorpus corpus, CancellationToken ct) =>
{
    var cambiato = await corpus.Riallinea(obbligatorio: false, ct);
    return Results.Ok(new
    {
        aggiornato = cambiato,
        versione = corpus.Versione,
        contenuti = corpus.Pronto ? corpus.Indice.NumeroContenuti : 0,
        sezioni = corpus.Pronto ? corpus.Indice.NumeroSezioni : 0,
    });
});

app.MapMcp("/mcp");

// Primo caricamento prima di servire: rispondere su un corpus vuoto sarebbe peggio
// che non partire.
await app.Services.GetRequiredService<ServizioCorpus>().Riallinea(obbligatorio: true);

app.Run();
