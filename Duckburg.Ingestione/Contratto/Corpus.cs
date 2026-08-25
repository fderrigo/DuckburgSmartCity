using System.Text.Json.Serialization;

namespace Duckburg.Ingestione.Contratto;

// Il contratto del corpus, ridichiarato qui invece di essere importato.
//
// Non e' duplicazione per svista: il corpus e' un servizio, non una libreria, proprio
// perche' gli adattatori dei CMS reali saranno scritti in PHP, Python o JavaScript e
// non possono referenziare un assembly .NET. Se anche il nostro adattatore importasse
// i tipi dal servizio, il contratto smetterebbe di essere autosufficiente senza che
// nessuno se ne accorga, e il primo adattatore di terzi scoprirebbe le lacune.
//
// Questi record sono percio' scritti leggendo lo schema pubblicato su
// /schema/corpus-1.0.json, esattamente come farebbe chiunque altro.

public sealed record Istantanea
{
    [JsonPropertyName("modello")] public string Modello { get; init; } = "1.0";
    [JsonPropertyName("ente")] public required Ente Ente { get; init; }
    [JsonPropertyName("generato_il")] public DateTimeOffset GeneratoIl { get; init; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("sorgente")] public Sorgente? Sorgente { get; init; }
    [JsonPropertyName("disclaimer")] public string? Disclaimer { get; init; }
    [JsonPropertyName("principio")] public string? Principio { get; init; }
    [JsonPropertyName("contenuti")] public IReadOnlyList<Contenuto> Contenuti { get; init; } = [];
}

public sealed record Ente
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
}

public sealed record Sorgente
{
    [JsonPropertyName("sistema")] public required string Sistema { get; init; }
    [JsonPropertyName("versione")] public string? Versione { get; init; }
}

public sealed record Contenuto
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("tipo")] public required string Tipo { get; init; }
    [JsonPropertyName("titolo")] public required string Titolo { get; init; }
    [JsonPropertyName("sommario")] public string? Sommario { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("lingua")] public string Lingua { get; init; } = "it";
    [JsonPropertyName("validita")] public Periodo? Validita { get; init; }
    [JsonPropertyName("aggiornato_il")] public DateTimeOffset AggiornatoIl { get; init; }
    [JsonPropertyName("attributi")] public IReadOnlyList<Attributo> Attributi { get; init; } = [];
    [JsonPropertyName("relazioni")] public IReadOnlyList<Relazione> Relazioni { get; init; } = [];
    [JsonPropertyName("sezioni")] public IReadOnlyList<Sezione> Sezioni { get; init; } = [];
    [JsonPropertyName("provenienza")] public Provenienza? Provenienza { get; init; }
}

public sealed record Periodo
{
    [JsonPropertyName("da")] public DateTimeOffset? Da { get; init; }
    [JsonPropertyName("a")] public DateTimeOffset? A { get; init; }
}

public sealed record Attributo
{
    [JsonPropertyName("chiave")] public required string Chiave { get; init; }
    [JsonPropertyName("etichetta")] public string? Etichetta { get; init; }
    [JsonPropertyName("tipo")] public required string Tipo { get; init; }
    [JsonPropertyName("valore")] public required object Valore { get; init; }
}

public sealed record Relazione
{
    [JsonPropertyName("tipo")] public required string Tipo { get; init; }
    [JsonPropertyName("verso")] public required string Verso { get; init; }
    [JsonPropertyName("etichetta")] public string? Etichetta { get; init; }
}

public sealed record Sezione
{
    [JsonPropertyName("chiave")] public required string Chiave { get; init; }
    [JsonPropertyName("etichetta")] public string? Etichetta { get; init; }
    [JsonPropertyName("ordine")] public int Ordine { get; init; }
    [JsonPropertyName("testo")] public required string Testo { get; init; }
    [JsonPropertyName("versione")] public string? Versione { get; init; }
    // L'impronta non si dichiara: la calcola il corpus. Ricalcolarla qui vorrebbe dire
    // reimplementare SHA-256 in ogni adattatore, e un errore spezzerebbe la
    // verificabilita' di ogni risposta senza che nessuno se ne accorga.
}

public sealed record Provenienza
{
    [JsonPropertyName("sistema")] public required string Sistema { get; init; }
    [JsonPropertyName("id_sorgente")] public string? IdSorgente { get; init; }
    [JsonPropertyName("url_sorgente")] public string? UrlSorgente { get; init; }
    [JsonPropertyName("estratto_il")] public DateTimeOffset? EstrattoIl { get; init; }
    [JsonPropertyName("metodo")] public string Metodo { get; init; } = "mappatura";
    [JsonPropertyName("confidenza")] public double? Confidenza { get; init; }
}
