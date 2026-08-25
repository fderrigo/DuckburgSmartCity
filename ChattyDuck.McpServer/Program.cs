using System.Threading.RateLimiting;
using ChattyDuck.McpServer.Corpus;
using ChattyDuck.McpServer.Mcp;

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
builder.Services.AddHostedService<Allineatore>();

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

// Lo stato e' esplicito: "allineamento" non e' un guasto, e' la condizione normale
// dei primi secondi. Il codice HTTP lo distingue per chi sorveglia, il corpo lo
// spiega a chi legge.
app.MapGet("/health", (ServizioCorpus corpus) =>
{
    var corpo = new
    {
        stato = corpus.Stato.ToString().ToLowerInvariant(),
        pronto = corpus.Pronto,
        messaggio = corpus.Descrizione,
        ente = corpus.Indice?.Istantanea.Ente.Id,
        versione = corpus.Versione,
        contenuti = corpus.Indice?.NumeroContenuti ?? 0,
        sezioni = corpus.Indice?.NumeroSezioni ?? 0,
        caricato_il = corpus.CaricatoIl,
        ultimo_errore = corpus.UltimoErrore,
        tentativi_falliti = corpus.TentativiFalliti,
    };
    return corpus.Pronto
        ? Results.Ok(corpo)
        : Results.Json(corpo, statusCode: StatusCodes.Status503ServiceUnavailable);
});

// Riallineamento immediato: dopo una pubblicazione nel CMS evita di aspettare il giro.
app.MapPost("/corpus/reload", async (ServizioCorpus corpus, CancellationToken ct) =>
{
    var cambiato = await corpus.Riallinea(ct);
    return Results.Ok(new
    {
        aggiornato = cambiato,
        stato = corpus.Stato.ToString().ToLowerInvariant(),
        messaggio = corpus.Descrizione,
        versione = corpus.Versione,
        contenuti = corpus.Indice?.NumeroContenuti ?? 0,
        sezioni = corpus.Indice?.NumeroSezioni ?? 0,
        ultimo_errore = corpus.UltimoErrore,
    });
});

app.MapMcp("/mcp");

// Nessun caricamento bloccante all'avvio: l'ordine con cui partono i servizi non deve
// essere un requisito. L'allineatore ci pensa in sottofondo, e finche' non ha finito
// gli strumenti dicono che l'allineamento e' in corso.
app.Run();
