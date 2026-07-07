using System.ComponentModel;
using Duckburg.Registry.Corpus;
using ModelContextProtocol.Server;

namespace Duckburg.Registry.Mcp;

[McpServerToolType]
public sealed class CorpusTools
{
    [McpServerTool(Name = "cerca")]
    [Description("Cerca nei contenuti certificati del Comune di Paperopoli. Ritorna i passaggi piu pertinenti, " +
                 "ognuno con id, version e hash: cita sempre l'id del passaggio nelle risposte. " +
                 "L'unica fonte di verita sono questi passaggi.")]
    public static IReadOnlyList<SearchHit> Cerca(
        CorpusService corpus,
        [Description("Testo da cercare, es. 'scadenza prima rata TARI' o 'prenotare carta d'identita'")] string query,
        [Description("Numero massimo di passaggi da ritornare (default 5)")] int? limite = null)
        => corpus.Search(query, Math.Clamp(limite ?? 5, 1, 10));
}
