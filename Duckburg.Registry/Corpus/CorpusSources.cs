using System.Text.Json;

namespace Duckburg.Registry.Corpus;

/// <summary>
/// Una sorgente di contenuti del corpus. Le sorgenti sono ordinate per priorita'
/// decrescente: la prima che risponde e' quella autorevole, le successive sono
/// ripiego (o integrazione, in modalita' Merge).
/// </summary>
public interface ICorpusSource
{
    /// <summary>Nome leggibile, usato nei log e in /health.</summary>
    string Nome { get; }

    /// <summary>Priorita': piu' alta = piu' autorevole.</summary>
    int Priorita { get; }

    /// <summary>Carica il documento, o null se la sorgente non e' disponibile.</summary>
    Task<CorpusDocument?> LoadAsync(CancellationToken ct);
}

/// <summary>
/// Corpus statico su file (<c>Corpus:Path</c>). E' il seme della demo e la rete di
/// sicurezza: se il feed del CMS non risponde, l'assistente continua a rispondere.
/// </summary>
public sealed class FileCorpusSource : ICorpusSource
{
    private readonly string _path;
    private readonly ILogger<FileCorpusSource> _logger;

    public FileCorpusSource(IConfiguration configuration, IWebHostEnvironment env, ILogger<FileCorpusSource> logger)
    {
        var path = configuration["Corpus:Path"]
                   ?? throw new InvalidOperationException("Configurazione mancante: Corpus:Path");
        _path = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(env.ContentRootPath, path));
        _logger = logger;
    }

    public string Nome => $"file:{_path}";

    public int Priorita => 10;

    public Task<CorpusDocument?> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            _logger.LogWarning("Corpus su file non trovato: {Path}", _path);
            return Task.FromResult<CorpusDocument?>(null);
        }

        var doc = JsonSerializer.Deserialize<CorpusDocument>(File.ReadAllText(_path))
                  ?? throw new InvalidOperationException($"Corpus non valido: {_path}");
        return Task.FromResult<CorpusDocument?>(doc);
    }
}

/// <summary>
/// Corpus servito dal CMS del portale (<c>Corpus:FeedUrl</c>, tipicamente
/// <c>http://localhost:5100/api/corpus</c>). Sorgente viva: quello che la redazione
/// pubblica nel CMS diventa cio' su cui l'assistente puo' rispondere.
/// Se il portale non risponde, la sorgente si limita a un warning: il Registry
/// resta in piedi con quello che ha gia' in memoria.
/// </summary>
public sealed class HttpFeedCorpusSource : ICorpusSource
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<HttpFeedCorpusSource> _logger;
    private readonly string _url;

    public HttpFeedCorpusSource(IHttpClientFactory http, IConfiguration configuration, ILogger<HttpFeedCorpusSource> logger)
    {
        _http = http;
        _logger = logger;
        _url = configuration["Corpus:FeedUrl"]
               ?? throw new InvalidOperationException("Configurazione mancante: Corpus:FeedUrl");
    }

    public string Nome => $"cms:{_url}";

    public int Priorita => 100;

    public async Task<CorpusDocument?> LoadAsync(CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(nameof(HttpFeedCorpusSource));
            client.Timeout = TimeSpan.FromSeconds(15);
            var doc = await client.GetFromJsonAsync<CorpusDocument>(_url, ct);
            if (doc is null || doc.Works.Count == 0)
            {
                _logger.LogWarning("Feed del CMS vuoto: {Url}", _url);
                return null;
            }
            return doc;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feed del CMS non raggiungibile: {Url}", _url);
            return null;
        }
    }
}
