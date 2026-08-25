namespace ChattyDuck.Corpus.Modello;

/// <summary>
/// Il vocabolario condiviso fra chi ingesta e chi legge.
/// <para>
/// Non e' inventato qui: i tipi di contenuto e le chiavi delle sezioni di una scheda
/// servizio sono quelli del modello Comuni, che li prescrive a tutti gli enti. E' questo
/// che rende scrivibile un adattatore da parte di chi non ha mai visto il nostro codice:
/// gli basta conoscere il proprio CMS e questo elenco.
/// </para>
/// <para>
/// I termini fuori elenco non sono un errore: vengono accettati e conservati, ma non
/// godono del trattamento riservato a quelli noti (filtri per tipo, confronto di date,
/// elenchi). Chiudere il vocabolario renderebbe il corpus inservibile al primo ente che
/// ha un contenuto in piu'.
/// </para>
/// </summary>
public static class Vocabolario
{
    public const string VersioneModello = "1.0";

    /// <summary>Tipi di contenuto dell'architettura dell'informazione del modello Comuni.</summary>
    public static readonly IReadOnlySet<string> Tipi = new HashSet<string>(StringComparer.Ordinal)
    {
        "servizio",
        "evento",
        "luogo",
        "unita-organizzativa",
        "persona",
        "documento",
        "novita",
        "pagina",
        "faq",
        "argomento",
        "categoria",
        "ente",
    };

    /// <summary>Forme che puo' assumere il valore di un attributo.</summary>
    public static readonly IReadOnlySet<string> TipiAttributo = new HashSet<string>(StringComparer.Ordinal)
    {
        "testo",      // stringa
        "numero",     // double
        "booleano",   // true/false
        "data",       // ISO 8601
        "periodo",    // { da, a }
        "importo",    // { valore, valuta }
        "elenco",     // array di stringhe
        "tabella",    // array di oggetti omogenei, es. fasce ISEE
        "contatto",   // { telefono?, email?, pec?, url? }
        "indirizzo",  // { via?, civico?, cap?, comune?, note? }
    };

    /// <summary>Archi del grafo dei contenuti.</summary>
    public static readonly IReadOnlySet<string> TipiRelazione = new HashSet<string>(StringComparer.Ordinal)
    {
        "erogato-da",       // servizio  -> unita-organizzativa
        "responsabile-di",  // persona   -> unita-organizzativa
        "argomento",        // qualsiasi -> argomento
        "categoria",        // servizio  -> categoria
        "si-svolge-in",     // evento    -> luogo
        "riguarda",         // novita    -> servizio
        "allegato",         // qualsiasi -> documento
        "correlato",        // generico
    };

    /// <summary>
    /// Chiavi delle sezioni di una scheda servizio: sono gli attributi obbligatori
    /// del modello Comuni, e ricorrono identici in ogni ente.
    /// </summary>
    public static readonly IReadOnlySet<string> SezioniServizio = new HashSet<string>(StringComparer.Ordinal)
    {
        "descrizione",
        "a-chi-e-rivolto",
        "come-fare",
        "cosa-serve",
        "cosa-si-ottiene",
        "tempi-e-scadenze",
        "costi",
        "condizioni-di-servizio",
        "riferimenti-normativi",
        "casi-particolari",
    };

    /// <summary>Chiavi di attributo ricorrenti. L'elenco e' aperto.</summary>
    public static readonly IReadOnlySet<string> ChiaviAttributo = new HashSet<string>(StringComparer.Ordinal)
    {
        "costo",
        "scadenza",
        "data-inizio",
        "data-fine",
        "orario",
        "ricorrenza",
        "indirizzo",
        "contatto",
        "prenotabile",
        "stato",
        "protocollo",
    };

    public const string MetodoMappatura = "mappatura";
    public const string MetodoEstrazione = "estrazione";

    public static readonly IReadOnlySet<string> Metodi = new HashSet<string>(StringComparer.Ordinal)
    {
        MetodoMappatura, MetodoEstrazione,
    };
}
