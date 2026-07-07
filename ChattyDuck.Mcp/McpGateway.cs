using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;

namespace ChattyDuck.Mcp;

/// <summary>
/// Client MCP verso Duckburg.Registry. Usato dal percorso Gemini per il ponte tool-MCP.
/// Il percorso Claude non passa da qui: Claude e' MCP-nativo e si collega da solo.
/// Confine di rete: l'assistente parla con la fonte dell'ente solo via Streamable HTTP.
/// </summary>
public sealed class McpGateway(IConfiguration configuration) : IAsyncDisposable
{
    private readonly string _endpoint = configuration["Registry:McpEndpoint"]
        ?? throw new InvalidOperationException("Configurazione mancante: Registry:McpEndpoint");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpClient? _client;

    private async Task<McpClient> GetClientAsync(CancellationToken ct)
    {
        if (_client is not null) return _client;
        await _gate.WaitAsync(ct);
        try
        {
            _client ??= await McpClient.CreateAsync(
                new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(_endpoint),
                    Name = "Duckburg.Registry",
                    TransportMode = HttpTransportMode.StreamableHttp,
                }),
                cancellationToken: ct);
            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IList<McpClientTool>> ListToolsAsync(CancellationToken ct)
        => await (await GetClientAsync(ct)).ListToolsAsync(cancellationToken: ct);

    /// <summary>Chiama un tool MCP e ritorna il testo concatenato dei blocchi di contenuto.</summary>
    public async Task<string> CallToolAsync(string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
    {
        var result = await (await GetClientAsync(ct)).CallToolAsync(name, arguments, cancellationToken: ct);
        return string.Join("\n", result.Content
            .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .Select(b => b.Text));
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        _gate.Dispose();
    }
}
