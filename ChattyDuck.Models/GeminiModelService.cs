using System.Text.Json;
using System.Text.Json.Nodes;
using ChattyDuck.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ChattyDuck.Models;

/// <summary>
/// Client 1: Gemini free tier via IHttpClientFactory.
/// Gemini NON e' MCP-nativo: QUI sta il ponte, che traduce i tool MCP del Registry
/// in function declarations e riporta i risultati come functionResponse.
/// </summary>
public sealed class GeminiModelService(
    IHttpClientFactory httpClientFactory,
    McpGateway gateway,
    IConfiguration configuration,
    ModelUsageTracker usageTracker,
    ILogger<GeminiModelService> logger) : IModelService
{
    private const int MaxToolRounds = 5;

    public string Name => "gemini";

    public async Task<ChatResult> AskAsync(string message, CancellationToken ct)
    {
        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Chiave Gemini mancante: imposta Gemini:ApiKey (env Gemini__ApiKey).");
        var model = configuration["Gemini:Model"] ?? "gemini-2.5-flash";

        // Il ponte: tool MCP -> function declarations di Gemini.
        var mcpTools = await gateway.ListToolsAsync(ct);
        var functionDeclarations = new JsonArray(mcpTools.Select(t => (JsonNode)new JsonObject
        {
            ["name"] = t.Name,
            ["description"] = t.Description,
            ["parameters"] = ToGeminiSchema(JsonNode.Parse(t.JsonSchema.GetRawText())!),
        }).ToArray());

        var contents = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = message }),
            },
        };

        var http = httpClientFactory.CreateClient("gemini");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
        var passages = new List<SearchHit>();

        for (var round = 0; round <= MaxToolRounds; round++)
        {
            var body = new JsonObject
            {
                ["systemInstruction"] = new JsonObject
                {
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = SystemPrompt.Text }),
                },
                ["contents"] = contents.DeepClone(),
                ["tools"] = new JsonArray(new JsonObject
                {
                    ["functionDeclarations"] = functionDeclarations.DeepClone(),
                }),
            };

            using var response = await http.PostAsync(url,
                new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"), ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gemini ha risposto {(int)response.StatusCode}: {payload}");

            var rootNode = JsonNode.Parse(payload);

            // Ogni round e' una richiesta che consuma quota: registra token dal usageMetadata.
            var usage = rootNode?["usageMetadata"];
            usageTracker.Record(Name,
                usage?["promptTokenCount"]?.GetValue<long>() ?? 0,
                usage?["candidatesTokenCount"]?.GetValue<long>() ?? 0);

            var candidateContent = rootNode?["candidates"]?[0]?["content"];
            var parts = candidateContent?["parts"]?.AsArray()
                        ?? throw new InvalidOperationException($"Risposta Gemini senza contenuto: {payload}");

            var functionCalls = parts
                .Where(p => p?["functionCall"] is not null)
                .Select(p => p!["functionCall"]!)
                .ToList();

            if (functionCalls.Count == 0)
            {
                var reply = string.Concat(parts.Select(p => p?["text"]?.GetValue<string>() ?? string.Empty)).Trim();
                return new ChatResult(reply, Distinct(passages));
            }

            // Riporta il turno del modello, poi esegue le chiamate sul Registry via MCP.
            contents.Add(new JsonObject { ["role"] = "model", ["parts"] = parts.DeepClone() });

            var responseParts = new JsonArray();
            foreach (var call in functionCalls)
            {
                var name = call["name"]!.GetValue<string>();
                var args = call["args"]?.AsObject().ToDictionary(
                    kv => kv.Key,
                    kv => (object?)kv.Value?.DeepClone()) ?? [];

                logger.LogInformation("Ponte Gemini->MCP: {Tool}({Args})", name, JsonSerializer.Serialize(args));
                var resultText = await gateway.CallToolAsync(name, args, ct);
                CollectPassages(name, resultText, passages);

                JsonNode resultNode;
                try { resultNode = JsonNode.Parse(resultText) ?? JsonValue.Create(resultText)!; }
                catch (JsonException) { resultNode = JsonValue.Create(resultText)!; }

                responseParts.Add(new JsonObject
                {
                    ["functionResponse"] = new JsonObject
                    {
                        ["name"] = name,
                        ["response"] = new JsonObject { ["result"] = resultNode },
                    },
                });
            }
            contents.Add(new JsonObject { ["role"] = "user", ["parts"] = responseParts });
        }

        throw new InvalidOperationException("Gemini non ha prodotto una risposta entro il limite di chiamate tool.");
    }

    private static void CollectPassages(string toolName, string resultText, List<SearchHit> passages)
    {
        if (!string.Equals(toolName, "cerca", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var hits = JsonSerializer.Deserialize<List<SearchHit>>(resultText);
            if (hits is not null) passages.AddRange(hits);
        }
        catch (JsonException)
        {
            // il risultato non e' l'array atteso: nessun passaggio da citare
        }
    }

    private static IReadOnlyList<SearchHit> Distinct(List<SearchHit> passages)
        => passages.GroupBy(p => p.Id).Select(g => g.First()).ToList();

    /// <summary>
    /// Gemini accetta un sottoinsieme OpenAPI dello schema JSON: niente type union
    /// (["integer","null"]), niente default/additionalProperties/$schema.
    /// </summary>
    private static JsonNode ToGeminiSchema(JsonNode schema)
    {
        if (schema is JsonArray arr)
            return new JsonArray(arr.Select(n => n is null ? null : ToGeminiSchema(n)).ToArray());
        if (schema is not JsonObject obj)
            return schema.DeepClone();

        var cleaned = new JsonObject();
        foreach (var (key, value) in obj)
        {
            switch (key)
            {
                case "default" or "additionalProperties" or "$schema" or "title":
                    continue;
                case "type" when value is JsonArray types:
                    var first = types.Select(t => t?.GetValue<string>())
                                     .FirstOrDefault(t => t is not null && t != "null");
                    cleaned["type"] = first ?? "string";
                    if (types.Any(t => t?.GetValue<string>() == "null")) cleaned["nullable"] = true;
                    continue;
                default:
                    cleaned[key] = value is null ? null : ToGeminiSchema(value);
                    continue;
            }
        }
        return cleaned;
    }
}
