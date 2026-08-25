using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChattyDuck.Corpus.Modello;

/// <summary>
/// Il modello del corpus: un vocabolario del dominio della pubblica amministrazione,
/// non la proiezione di un CMS particolare.
/// <para>
/// E' questa la ragione per cui esiste un servizio separato. Ogni ente ha il proprio
/// gestore di contenuti, con le proprie tabelle e i propri nomi; le informazioni pero'
/// sono le stesse ovunque, perche' il modello Comuni prescrive quali attributi deve
/// avere una scheda servizio. Il corpus parla quel linguaggio: a monte ogni CMS ha il
/// proprio adattatore, a valle nessuno sa piu' da dove i contenuti vengano.
/// </para>
/// </summary>
public sealed record Istantanea
{
    /// <summary>Versione del modello, non del contenuto. Serve a far convivere adattatori di eta' diverse.</summary>
    [JsonPropertyName("modello")] public string Modello { get; init; } = Vocabolario.VersioneModello;

    [JsonPropertyName("ente")] public required Ente Ente { get; init; }

    [JsonPropertyName("generato_il")] public DateTimeOffset GeneratoIl { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("sorgente")] public Sorgente? Sorgente { get; init; }

    /// <summary>Avvertenza dell'ente, mostrata insieme alle risposte.</summary>
    [JsonPropertyName("disclaimer")] public string? Disclaimer { get; init; }

    /// <summary>Regola di comportamento che l'ente detta a chi consuma il corpus.</summary>
    [JsonPropertyName("principio")] public string? Principio { get; init; }

    [JsonPropertyName("contenuti")] public IReadOnlyList<Contenuto> Contenuti { get; init; } = [];
}

public sealed record Ente
{
    /// <summary>Identificatore stabile, minuscolo, senza spazi. Es. "comune-paperopoli".</summary>
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
}

/// <summary>Sistema da cui l'istantanea proviene. Utile quando un ente cambia CMS.</summary>
public sealed record Sorgente
{
    [JsonPropertyName("sistema")] public required string Sistema { get; init; }
    [JsonPropertyName("versione")] public string? Versione { get; init; }
}

/// <summary>
/// Un contenuto pubblicato dell'ente: una scheda servizio, un evento, un ufficio.
/// <para>
/// La distinzione che regge tutto il modello e' fra <see cref="Attributi"/> e
/// <see cref="Sezioni"/>. Gli attributi sono fatti tipizzati, interrogabili senza
/// leggere: una data di scadenza e' una data, una tariffa e' una tabella di importi.
/// Le sezioni sono prosa, ed e' li' che vivono le citazioni: ognuna ha id, versione e
/// impronta, ed e' l'unita' che una risposta cita.
/// </para>
/// </summary>
public sealed record Contenuto
{
    /// <summary>Identificatore stabile nella forma <c>tipo:slug</c>. Es. "servizio:tari".</summary>
    [JsonPropertyName("id")] public required string Id { get; init; }

    /// <summary>Uno dei valori di <see cref="Vocabolario.Tipi"/>.</summary>
    [JsonPropertyName("tipo")] public required string Tipo { get; init; }

    [JsonPropertyName("titolo")] public required string Titolo { get; init; }

    /// <summary>Riassunto breve, usato negli elenchi senza dover leggere le sezioni.</summary>
    [JsonPropertyName("sommario")] public string? Sommario { get; init; }

    /// <summary>URL pubblica canonica: e' quella che una risposta puo' offrire al cittadino.</summary>
    [JsonPropertyName("url")] public string? Url { get; init; }

    [JsonPropertyName("lingua")] public string Lingua { get; init; } = "it";

    /// <summary>
    /// Finestra di validita'. Assente significa sempre valido. Senza questo campo un
    /// assistente cita con sicurezza scadenze scadute, che per una fonte che si dichiara
    /// certificata e' il difetto peggiore.
    /// </summary>
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

    public bool ValidoAl(DateTimeOffset istante) =>
        (Da is null || istante >= Da) && (A is null || istante <= A);
}

/// <summary>
/// Un fatto tipizzato. <see cref="Valore"/> resta JSON grezzo di proposito: la forma
/// dipende da <see cref="Tipo"/>, e vincolarla in C# renderebbe il contratto piu' rigido
/// di quanto serva a chi scrive un adattatore in un altro linguaggio.
/// </summary>
public sealed record Attributo
{
    /// <summary>Chiave del vocabolario. Es. "costo", "scadenza", "orario".</summary>
    [JsonPropertyName("chiave")] public required string Chiave { get; init; }

    /// <summary>Etichetta leggibile, quella che l'ente mostra sul proprio sito.</summary>
    [JsonPropertyName("etichetta")] public string? Etichetta { get; init; }

    /// <summary>Uno dei valori di <see cref="Vocabolario.TipiAttributo"/>.</summary>
    [JsonPropertyName("tipo")] public required string Tipo { get; init; }

    [JsonPropertyName("valore")] public JsonElement Valore { get; init; }
}

/// <summary>Un arco del grafo dei contenuti: e' cio' che il corpus piatto buttava via.</summary>
public sealed record Relazione
{
    /// <summary>Uno dei valori di <see cref="Vocabolario.TipiRelazione"/>.</summary>
    [JsonPropertyName("tipo")] public required string Tipo { get; init; }

    /// <summary>Id del contenuto di destinazione.</summary>
    [JsonPropertyName("verso")] public required string Verso { get; init; }

    [JsonPropertyName("etichetta")] public string? Etichetta { get; init; }
}

/// <summary>
/// Un pezzo di prosa citabile. E' l'unita' che una risposta indica come fonte, quindi
/// porta con se' tutto quello che serve a verificarla anche a distanza di tempo.
/// </summary>
public sealed record Sezione
{
    /// <summary>
    /// Nella forma <c>idContenuto#chiave</c>. Es. "servizio:tari#come-fare".
    /// Se l'adattatore lo omette lo compone il corpus: e' un dettaglio che non ha senso
    /// far ricalcolare a ogni adattatore, e sbagliarlo spezzerebbe le citazioni.
    /// </summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    /// <summary>Chiave del vocabolario. Es. "come-fare", "cosa-serve".</summary>
    [JsonPropertyName("chiave")] public required string Chiave { get; init; }

    [JsonPropertyName("etichetta")] public string? Etichetta { get; init; }

    [JsonPropertyName("ordine")] public int Ordine { get; init; }

    [JsonPropertyName("testo")] public required string Testo { get; init; }

    /// <summary>Istante dell'ultima modifica del contenuto da cui la sezione deriva.</summary>
    [JsonPropertyName("versione")] public string? Versione { get; init; }

    /// <summary>
    /// Impronta del testo, nella forma <c>sha256:...</c>. Se l'adattatore la omette la
    /// calcola il corpus; se la dichiara viene verificata, e un'istantanea con impronte
    /// incoerenti viene rifiutata. Il controllo sta qui e non nell'adattatore perche'
    /// gli adattatori sono tanti e scritti da altri.
    /// </summary>
    [JsonPropertyName("hash")] public string? Hash { get; init; }
}

/// <summary>
/// Da dove viene il contenuto e come e' stato ricavato.
/// <para>
/// <see cref="Metodo"/> distingue una mappatura esplicita, scritta da chi conosce il CMS
/// di partenza, da un'estrazione assistita da un modello su una sorgente che non si
/// controlla. Nel secondo caso <see cref="Confidenza"/> va dichiarata: un dato ricavato
/// automaticamente non puo' presentarsi con la stessa autorevolezza di uno mappato.
/// </para>
/// </summary>
public sealed record Provenienza
{
    [JsonPropertyName("sistema")] public required string Sistema { get; init; }
    [JsonPropertyName("id_sorgente")] public string? IdSorgente { get; init; }
    [JsonPropertyName("url_sorgente")] public string? UrlSorgente { get; init; }
    [JsonPropertyName("estratto_il")] public DateTimeOffset? EstrattoIl { get; init; }

    /// <summary>"mappatura" oppure "estrazione".</summary>
    [JsonPropertyName("metodo")] public string Metodo { get; init; } = Vocabolario.MetodoMappatura;

    /// <summary>Da 0 a 1. Attesa quando il metodo e' "estrazione".</summary>
    [JsonPropertyName("confidenza")] public double? Confidenza { get; init; }
}
