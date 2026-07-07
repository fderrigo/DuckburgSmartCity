using System.ComponentModel;
using System.Text.Json;
using Duckburg.Registry.Corpus;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Duckburg.Registry.Mcp;

[McpServerResourceType]
public sealed class CorpusResources
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    [McpServerResource(UriTemplate = "corpus://index", Name = "indice", MimeType = "application/json")]
    [Description("Indice del corpus del Comune di Paperopoli: elenco delle aree di servizio (works) con id e titolo.")]
    public static string Index(CorpusService corpus)
        => JsonSerializer.Serialize(new
        {
            corpus_version = corpus.Document.CorpusVersion,
            disclaimer = corpus.Document.Disclaimer,
            works = corpus.Works.Select(w => new { w.Id, w.Title, w.PassageCount }),
        }, Pretty);

    [McpServerResource(UriTemplate = "corpus://work/{workId}", Name = "area", MimeType = "application/json")]
    [Description("Un'area di servizio (work) del corpus, con tutti i suoi passaggi: id, version, hash e testo.")]
    public static string GetWork(CorpusService corpus, string workId)
    {
        var work = corpus.GetWork(workId)
                   ?? throw new McpException($"Area non trovata: {workId}");
        return JsonSerializer.Serialize(work, Pretty);
    }
}
