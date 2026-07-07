using ChattyDuck.Mcp;
using ChattyDuck.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChattyDuck.Quack;

public sealed record ChatRequest(string Message, string Model);

public sealed record ChatResponse(string Reply, string Model, IReadOnlyList<SearchHit> Passages);

/// <summary>
/// Montaggio dell'assistente dentro un'applicazione ospite (Duckburg.Portal):
/// servizi in DI e canale chat come Minimal API.
/// </summary>
public static class QuackExtensions
{
    public static IServiceCollection AddQuack(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<ModelUsageTracker>();
        services.AddSingleton<McpGateway>();
        services.AddSingleton<IModelService, GeminiModelService>();
        services.AddSingleton<IModelService, ClaudeModelService>();
        services.AddSingleton<ChatOrchestrator>();
        return services;
    }

    public static IEndpointRouteBuilder MapQuackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/chat", async (ChatRequest request, ChatOrchestrator orchestrator, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "Messaggio vuoto." });
            try
            {
                var result = await orchestrator.AskAsync(request.Model, request.Message, ct);
                return Results.Ok(new ChatResponse(result.Reply, request.Model, result.Passages));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("ChattyDuck.Quack").LogError(ex, "Errore nel canale chat (modello {Model})", request.Model);
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // Stato d'uso dei modelli: finestra mobile 60s + totale giornaliero, per il pannello limiti.
        app.MapGet("/chat/usage", (ChatOrchestrator orchestrator, ModelUsageTracker tracker) =>
            Results.Ok(orchestrator.AvailableModels.Select(tracker.Snapshot)));

        // Diagnostica: verifica che il ponte MCP verso Duckburg.Registry funzioni, senza chiavi API.
        app.MapGet("/debug/tools", async (McpGateway gateway, CancellationToken ct) =>
        {
            var tools = await gateway.ListToolsAsync(ct);
            return Results.Ok(tools.Select(t => new { t.Name, t.Description }));
        });

        return app;
    }
}
