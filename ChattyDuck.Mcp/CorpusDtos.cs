using System.Text.Json.Serialization;

namespace ChattyDuck.Mcp;

/// <summary>
/// DTO lato assistente del risultato di ricerca esposto da Duckburg.Registry.
/// Copia wire-format del contratto del server: il confine e' il protocollo MCP,
/// non un riferimento a progetto.
/// </summary>
public sealed record SearchHit(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("work_id")] string WorkId,
    [property: JsonPropertyName("work_title")] string WorkTitle,
    [property: JsonPropertyName("score")] double Score);
