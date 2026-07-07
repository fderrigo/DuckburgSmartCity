using System.Threading.RateLimiting;
using Duckburg.Registry.Corpus;
using Duckburg.Registry.Mcp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CorpusService>();

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

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapMcp("/mcp");


app.Run();
