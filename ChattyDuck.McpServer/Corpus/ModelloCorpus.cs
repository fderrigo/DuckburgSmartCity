using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChattyDuck.McpServer.Corpus;

// Il contratto del corpus visto da chi legge, ridichiarato leggendo lo schema pubblicato.
// Il server MCP e' un consumatore del corpus come lo sarebbe un client di terzi: non
// condivide tipi con il servizio, altrimenti il contratto smetterebbe di essere
// autosufficiente senza che nessuno se ne accorga.

public sealed record Istantanea
{
    [JsonPropertyName("modello")] public string Modello { get; init; } = "1.0";
    [JsonPropertyName("ente")] public Ente Ente { get; init; } = new();
    [JsonPropertyName("generato_il")] public DateTimeOffset GeneratoIl { get; init; }
    [JsonPropertyName("disclaimer")] public string? Disclaimer { get; init; }
    [JsonPropertyName("principio")] public string? Principio { get; init; }
    [JsonPropertyName("contenuti")] public IReadOnlyList<Contenuto> Contenuti { get; init; } = [];
}

public sealed record Ente
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("nome")] public string Nome { get; init; } = "";
    [JsonPropertyName("url")] public string? Url { get; init; }
}

public sealed record Contenuto
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("tipo")] public string Tipo { get; init; } = "";
    [JsonPropertyName("titolo")] public string Titolo { get; init; } = "";
    [JsonPropertyName("sommario")] public string? Sommario { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("validita")] public Periodo? Validita { get; init; }
    [JsonPropertyName("aggiornato_il")] public DateTimeOffset AggiornatoIl { get; init; }
    [JsonPropertyName("attributi")] public IReadOnlyList<Attributo> Attributi { get; init; } = [];
    [JsonPropertyName("relazioni")] public IReadOnlyList<Relazione> Relazioni { get; init; } = [];
    [JsonPropertyName("sezioni")] public IReadOnlyList<Sezione> Sezioni { get; init; } = [];
}

public sealed record Periodo
{
    [JsonPropertyName("da")] public DateTimeOffset? Da { get; init; }
    [JsonPropertyName("a")] public DateTimeOffset? A { get; init; }

    public bool ValidoAl(DateTimeOffset t) => (Da is null || t >= Da) && (A is null || t <= A);
}

public sealed record Attributo
{
    [JsonPropertyName("chiave")] public string Chiave { get; init; } = "";
    [JsonPropertyName("etichetta")] public string? Etichetta { get; init; }
    [JsonPropertyName("tipo")] public string Tipo { get; init; } = "";
    [JsonPropertyName("valore")] public JsonElement Valore { get; init; }
}

public sealed record Relazione
{
    [JsonPropertyName("tipo")] public string Tipo { get; init; } = "";
    [JsonPropertyName("verso")] public string Verso { get; init; } = "";
    [JsonPropertyName("etichetta")] public string? Etichetta { get; init; }
}

public sealed record Sezione
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("chiave")] public string Chiave { get; init; } = "";
    [JsonPropertyName("etichetta")] public string? Etichetta { get; init; }
    [JsonPropertyName("ordine")] public int Ordine { get; init; }
    [JsonPropertyName("testo")] public string Testo { get; init; } = "";
    [JsonPropertyName("versione")] public string? Versione { get; init; }
    [JsonPropertyName("hash")] public string? Hash { get; init; }
}

// ------------------------------------------------------------- risultati del tool

/// <summary>
/// Un contenuto trovato, con le sezioni pertinenti e i fatti che porta.
/// <para>
/// La forma raggruppata non e' una comodita': e' la correzione di un errore. Prima il
/// tool restituiva frammenti sciolti, e un frammento come "Costi: dipende dall'ISEE"
/// non dice a quale servizio appartenga. Ordinare frammenti contro una domanda obbliga
/// ognuno a portarsi addosso il proprio contesto, e a quel punto tutti i frammenti di
/// una scheda diventano ugualmente pertinenti e a decidere resta la lunghezza.
/// </para>
/// </summary>
public sealed record RisultatoRicerca(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("titolo")] string Titolo,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("punteggio")] double Punteggio,
    [property: JsonPropertyName("valido")] bool Valido,
    [property: JsonPropertyName("attributi")] IReadOnlyList<AttributoEsposto> Attributi,
    [property: JsonPropertyName("sezioni")] IReadOnlyList<SezioneEsposta> Sezioni);

public sealed record AttributoEsposto(
    [property: JsonPropertyName("chiave")] string Chiave,
    [property: JsonPropertyName("etichetta")] string? Etichetta,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("valore")] JsonElement Valore);

public sealed record SezioneEsposta(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("etichetta")] string? Etichetta,
    [property: JsonPropertyName("testo")] string Testo,
    [property: JsonPropertyName("versione")] string? Versione,
    [property: JsonPropertyName("hash")] string? Hash);

public sealed record VoceElenco(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("titolo")] string Titolo,
    [property: JsonPropertyName("sommario")] string? Sommario,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("valido")] bool Valido);
