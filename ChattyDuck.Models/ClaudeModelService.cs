using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using ChattyDuck.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ChattyDuck.Models;

/// <summary>
/// Client 2: API Anthropic. Claude e' MCP-nativo: si passa al modello l'endpoint del
/// server MCP (connettore MCP della Messages API) e si collega da solo. NIENTE ponte.
/// L'endpoint deve essere raggiungibile da Anthropic: in sviluppo il dominio ngrok,
/// in produzione mcp.derrigo.it.
/// </summary>
public sealed class ClaudeModelService(
    IConfiguration configuration,
    ModelUsageTracker usageTracker,
    ILogger<ClaudeModelService> logger) : IModelService
{
    // HttpClient condiviso con handler che cattura gli header anthropic-ratelimit-* (dati reali).
    private readonly HttpClient _http = new(new AnthropicRateLimitHandler(usageTracker));

    public string Name => "claude";

    public async Task<ChatResult> AskAsync(string message, CancellationToken ct)
    {
        var apiKey = configuration["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Chiave Anthropic mancante: imposta Anthropic:ApiKey (env Anthropic__ApiKey).");

        var mcpEndpoint = configuration["Anthropic:McpEndpoint"];
        if (string.IsNullOrWhiteSpace(mcpEndpoint))
            throw new InvalidOperationException(
                "Endpoint MCP pubblico mancante: imposta Anthropic:McpEndpoint (es. https://<dominio>.ngrok-free.app/mcp). " +
                "Deve essere raggiungibile dai server Anthropic, non localhost.");

        var client = new AnthropicClient { ApiKey = apiKey, HttpClient = _http };

        var response = await client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = configuration["Anthropic:Model"] ?? "claude-opus-4-8",
            MaxTokens = 4096,
            Betas = ["mcp-client-2025-11-20"],
            System = SystemPrompt.Text,
            McpServers =
            [
                new BetaRequestMcpServerUrlDefinition
                {
                    Name = "chattyduck-registry",
                    Url = mcpEndpoint,
                },
            ],
            Tools =
            [
                new BetaToolUnion(new BetaMcpToolset { McpServerName = "chattyduck-registry" }),
            ],
            Messages = [new() { Role = Role.User, Content = message }],
        }, cancellationToken: ct);

        usageTracker.Record(Name, response.Usage.InputTokens, response.Usage.OutputTokens);

        var reply = new StringBuilder();
        var passages = new List<SearchHit>();

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out BetaTextBlock? text))
            {
                reply.AppendLine(text.Text);
            }
            else if (block.TryPickMcpToolUse(out BetaMcpToolUseBlock? use))
            {
                logger.LogInformation("Claude ha chiamato il tool MCP {Tool}", use.Name);
            }
            else if (block.TryPickMcpToolResult(out BetaMcpToolResultBlock? result))
            {
                CollectPassages(result, passages);
            }
        }

        return new ChatResult(reply.ToString().Trim(),
            passages.GroupBy(p => p.Id).Select(g => g.First()).ToList());
    }

    private static void CollectPassages(BetaMcpToolResultBlock result, List<SearchHit> passages)
    {
        // Il contenuto del tool result e' testo JSON: la lista di SearchHit prodotta dal Registry.
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return;
        foreach (var item in content.EnumerateArray())
        {
            if (!item.TryGetProperty("text", out var textProp)) continue;
            try
            {
                var hits = JsonSerializer.Deserialize<List<SearchHit>>(textProp.GetString() ?? "");
                if (hits is not null) passages.AddRange(hits);
            }
            catch (JsonException)
            {
                // non era l'array di passaggi: ignora
            }
        }
    }
}
