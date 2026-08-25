using System.ComponentModel;
using ChattyDuck.McpServer.Corpus;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ChattyDuck.McpServer.Mcp;

/// <summary>
/// Gli strumenti che l'ente mette a disposizione dei modelli.
/// <para>
/// Sono tre e stretti invece di uno largo. Un modello sceglie meglio fra strumenti con
/// un compito chiaro che non formulando query testuali sperando che peschino: a una
/// domanda come "quali eventi ci sono" deve poter rispondere <c>elenca</c>, non una
/// ricerca per parole che dipende da come e' scritto il titolo di un evento.
/// </para>
/// <para>
/// Le descrizioni contano quanto il codice: sono l'unica documentazione che il modello
/// legge, e da li' decide quale strumento usare e come citare.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class StrumentiCorpus
{
    [McpServerTool(Name = "cerca")]
    [Description(
        "Cerca fra i contenuti certificati del Comune. Restituisce le schede pertinenti, " +
        "ognuna con i propri fatti (attributi: costi, scadenze, orari) e le sezioni di testo " +
        "piu' vicine alla domanda. Cita SEMPRE l'id della sezione da cui prendi l'informazione. " +
        "Se un contenuto ha valido=false e' scaduto: dillo invece di presentarlo come attuale. " +
        "Per elencare tutti i contenuti di un tipo usa 'elenca', non questa.")]
    public static IReadOnlyList<RisultatoRicerca> Cerca(
        ServizioCorpus corpus,
        [Description("Testo della domanda, es. 'quanto costa la mensa scolastica'")] string query,
        [Description("Facoltativo: restringe a un tipo, es. 'servizio', 'evento', 'unita-organizzativa'")] string? tipo = null,
        [Description("Numero massimo di schede da restituire (default 4)")] int? limite = null,
        [Description("Sezioni per scheda (default 3)")] int? sezioni = null)
        => corpus.Indice.Cerca(query, tipo,
            Math.Clamp(limite ?? 4, 1, 10),
            Math.Clamp(sezioni ?? 3, 1, 12));

    [McpServerTool(Name = "scheda")]
    [Description(
        "Restituisce una scheda intera per id, con tutti i fatti, tutte le sezioni e i " +
        "collegamenti ad altri contenuti. Usala quando 'cerca' ha individuato la scheda " +
        "giusta ma servono parti che non erano nel risultato.")]
    public static object Scheda(
        ServizioCorpus corpus,
        [Description("Id del contenuto, es. 'servizio:tari'")] string id)
    {
        var c = corpus.Indice.Scheda(id) ?? throw new McpException($"Contenuto non trovato: {id}");
        return new
        {
            c.Id, c.Tipo, c.Titolo, c.Sommario, c.Url,
            valido = c.Validita?.ValidoAl(DateTimeOffset.UtcNow) ?? true,
            validita = c.Validita,
            attributi = c.Attributi,
            sezioni = c.Sezioni,
            collegati = corpus.Indice.Collegati(id),
        };
    }

    [McpServerTool(Name = "elenca")]
    [Description(
        "Elenca i contenuti di un tipo, senza cercare per parole. E' il modo giusto di " +
        "rispondere a domande come 'quali eventi ci sono', 'quali uffici esistono', " +
        "'che servizi offre il Comune'. Con soloValidi=true esclude cio' che e' scaduto. " +
        "Con collegatoA elenca i contenuti collegati a una scheda, es. i servizi di un ufficio.")]
    public static IReadOnlyList<VoceElenco> Elenca(
        ServizioCorpus corpus,
        [Description("Tipo: servizio, evento, luogo, unita-organizzativa, persona, documento, novita, pagina, faq, argomento, categoria, ente")] string? tipo = null,
        [Description("Facoltativo: id di una scheda, per elencare i contenuti collegati")] string? collegatoA = null,
        [Description("Se true esclude i contenuti fuori dal periodo di validita' (default true)")] bool? soloValidi = null,
        [Description("Numero massimo di voci (default 50)")] int? limite = null)
        => corpus.Indice.Elenca(tipo, collegatoA, soloValidi ?? true, Math.Clamp(limite ?? 50, 1, 300));
}

/// <summary>Le risorse: servono a un client per capire cosa c'e' dentro, senza interrogare.</summary>
[McpServerResourceType]
public sealed class RisorseCorpus
{
    private static readonly System.Text.Json.JsonSerializerOptions Leggibile = new() { WriteIndented = true };

    [McpServerResource(UriTemplate = "corpus://indice", Name = "indice", MimeType = "application/json")]
    [Description("Che cosa contiene il corpus dell'ente: tipi di contenuto e quanti ce ne sono.")]
    public static string Indice(ServizioCorpus corpus)
    {
        var i = corpus.Indice;
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            ente = i.Istantanea.Ente,
            versione = corpus.Versione,
            disclaimer = i.Istantanea.Disclaimer,
            principio = i.Istantanea.Principio,
            contenuti = i.NumeroContenuti,
            sezioni = i.NumeroSezioni,
            tipi = i.Tipi().Select(t => new { tipo = t, quanti = i.Elenca(t, null, false, 1000).Count }),
        }, Leggibile);
    }

    [McpServerResource(UriTemplate = "corpus://contenuto/{id}", Name = "contenuto", MimeType = "application/json")]
    [Description("Una scheda intera del corpus, con fatti, sezioni e collegamenti.")]
    public static string Contenuto(ServizioCorpus corpus, string id)
    {
        var c = corpus.Indice.Scheda(id) ?? throw new McpException($"Contenuto non trovato: {id}");
        return System.Text.Json.JsonSerializer.Serialize(c, Leggibile);
    }
}
