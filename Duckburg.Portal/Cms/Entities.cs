namespace Duckburg.Portal.Cms;

/// <summary>
/// Base di tutte le entità di contenuto del CMS.
/// <see cref="IsDefault"/> marca i contenuti di seed (Paperopoli) che,
/// se <c>Cms:ProtectDefaultContent</c> è true, non sono modificabili né eliminabili.
/// </summary>
public abstract class CmsEntity
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsPublished { get; set; } = true;
    public int Ordine { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum TipoNovita { Notizia = 0, Comunicato = 1, Avviso = 2 }

public enum RuoloPersona { Sindaco = 0, Vicesindaco = 1, Assessore = 2, Consigliere = 3, Dirigente = 4, Responsabile = 5 }

public enum TipoUnita { Ufficio = 0, Area = 1, Servizio = 2, Ente = 3 }

public enum TipoDocumento { Documento = 0, Modulo = 1, Normativa = 2, Dataset = 3, AlboPretorio = 4 }

public enum PosizioneMenu { Principale = 0, Footer = 1, Header = 2 }

public enum StatoSegnalazione { Ricevuta = 0, InLavorazione = 1, Risolta = 2 }

/// <summary>Argomento della Tassonomia argomenti del modello Comuni (vocabolario controllato).</summary>
public sealed class Argomento : CmsEntity
{
    public string Nome { get; set; } = "";
    public string Descrizione { get; set; } = "";
    /// <summary>Chiave icona logica (es. "oro", "acqua", "rossa") usata dal tema.</summary>
    public string Colore { get; set; } = "oro";

    public List<Servizio> Servizi { get; set; } = new();
    public List<Novita> Novita { get; set; } = new();
}

/// <summary>
/// Categoria di servizio: pagina di secondo livello della sezione Servizi.
/// I nomi seguono il vocabolario del modello Comuni (C.SI.1.7).
/// </summary>
public sealed class CategoriaServizio : CmsEntity
{
    public string Nome { get; set; } = "";
    public string Descrizione { get; set; } = "";

    public List<Servizio> Servizi { get; set; } = new();
}

/// <summary>Scheda servizio (contenuto principale del modello Comuni, C.SI.1.3).</summary>
public sealed class Servizio : CmsEntity
{
    public string Titolo { get; set; } = "";
    public string Sottotitolo { get; set; } = "";
    public string DescrizioneBreve { get; set; } = "";
    /// <summary>Se false la scheda mostra lo stato "non attivo" con il motivo.</summary>
    public bool Attivo { get; set; } = true;
    public string MotivoStato { get; set; } = "";
    public string AChiERivolto { get; set; } = "";
    public string Descrizione { get; set; } = "";
    public string ComeFare { get; set; } = "";
    public string CosaServe { get; set; } = "";
    public string CosaSiOttiene { get; set; } = "";
    public string Tempi { get; set; } = "";
    public string Costi { get; set; } = "";
    public string CondizioniServizio { get; set; } = "";
    /// <summary>URL del documento con le condizioni di servizio (data-element service-file).</summary>
    public string CondizioniServizioUrl { get; set; } = "";
    public List<string> Scadenze { get; set; } = new();
    public List<string> Fonti { get; set; } = new();
    /// <summary>Chiave icona SVG del tema (es. "tari", "acqua", "casa").</summary>
    public string IconaKey { get; set; } = "documento";
    public string ColoreIcona { get; set; } = "oro";
    public bool InEvidenza { get; set; }

    public int? CategoriaId { get; set; }
    public CategoriaServizio? Categoria { get; set; }
    public int? ArgomentoId { get; set; }
    public Argomento? Argomento { get; set; }
    public int? UnitaOrganizzativaId { get; set; }
    public UnitaOrganizzativa? UnitaOrganizzativa { get; set; }
}

/// <summary>Notizia, comunicato o avviso (sezione Novità).</summary>
public sealed class Novita : CmsEntity
{
    public string Titolo { get; set; } = "";
    public TipoNovita Tipo { get; set; } = TipoNovita.Notizia;
    public DateTime Data { get; set; }
    public string Sommario { get; set; } = "";
    public string Corpo { get; set; } = "";
    public bool InEvidenza { get; set; }
    /// <summary>URL immagine di accompagnamento (dalla libreria media).</summary>
    public string ImmagineUrl { get; set; } = "";

    public int? ArgomentoId { get; set; }
    public Argomento? Argomento { get; set; }
    /// <summary>Ufficio che cura il contenuto ("A cura di", attributo obbligatorio).</summary>
    public int? ACuraDiId { get; set; }
    public UnitaOrganizzativa? ACuraDi { get; set; }
}

/// <summary>Evento (Vivere il Comune).</summary>
public sealed class Evento : CmsEntity
{
    public string Titolo { get; set; } = "";
    public string Sommario { get; set; } = "";
    public string Descrizione { get; set; } = "";
    public DateTime? DataInizio { get; set; }
    public DateTime? DataFine { get; set; }
    /// <summary>Orario testuale dell'evento (es. "dalle 8 alle 13").</summary>
    public string Orario { get; set; } = "";
    /// <summary>Ricorrenza testuale (es. "ogni sabato", "settembre").</summary>
    public string Ricorrenza { get; set; } = "";
    public string LuogoTesto { get; set; } = "";
    public string Costo { get; set; } = "Gratuito";
    public string Contatti { get; set; } = "";
    public string ImmagineUrl { get; set; } = "";
}

/// <summary>Luogo del territorio (Vivere il Comune).</summary>
public sealed class Luogo : CmsEntity
{
    public string Nome { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Descrizione { get; set; } = "";
    public string Indirizzo { get; set; } = "";
    /// <summary>Come arrivare, accessibilità, costi di accesso (attributo obbligatorio).</summary>
    public string ModalitaAccesso { get; set; } = "";
    public string Contatti { get; set; } = "";
    public string Orari { get; set; } = "";
    public string ImmagineUrl { get; set; } = "";
}

/// <summary>Persona / politico / responsabile (Amministrazione).</summary>
public sealed class Persona : CmsEntity
{
    public string Nome { get; set; } = "";
    public RuoloPersona Ruolo { get; set; } = RuoloPersona.Assessore;
    /// <summary>Carica testuale mostrata (es. "Sindaca", "Vicesindaco").</summary>
    public string Carica { get; set; } = "";
    public string Biografia { get; set; } = "";
    public List<string> Deleghe { get; set; } = new();
    public string Ricevimento { get; set; } = "";
    public string Email { get; set; } = "";
    public string Telefono { get; set; } = "";
    /// <summary>Ritratto SVG inline (fumetto Paperopoli).</summary>
    public string RitrattoSvg { get; set; } = "";
    /// <summary>In alternativa all'SVG: URL immagine dalla libreria media.</summary>
    public string ImmagineUrl { get; set; } = "";
}

/// <summary>Unità organizzativa: ufficio, area o ente (Amministrazione).</summary>
public sealed class UnitaOrganizzativa : CmsEntity
{
    public string Nome { get; set; } = "";
    public TipoUnita Tipo { get; set; } = TipoUnita.Ufficio;
    public string Descrizione { get; set; } = "";
    /// <summary>Compiti assegnati alla struttura (attributo obbligatorio, una per riga).</summary>
    public List<string> Competenze { get; set; } = new();
    public string Sede { get; set; } = "";
    public string Orari { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Email { get; set; } = "";
    public string Pec { get; set; } = "";
    public string Responsabile { get; set; } = "";
    /// <summary>Se true l'ufficio è prenotabile dalla funzionalità appuntamenti.</summary>
    public bool Prenotabile { get; set; } = true;
}

/// <summary>Documento pubblico (Documenti e dati, Albo pretorio).</summary>
public sealed class Documento : CmsEntity
{
    public string Titolo { get; set; } = "";
    public TipoDocumento Tipo { get; set; } = TipoDocumento.Documento;
    public DateTime Data { get; set; }
    public string Descrizione { get; set; } = "";
    public string UrlFile { get; set; } = "";

    /// <summary>Ufficio responsabile del documento (attributo obbligatorio).</summary>
    public int? UfficioResponsabileId { get; set; }
    public UnitaOrganizzativa? UfficioResponsabile { get; set; }
}

/// <summary>Pagina di contenuto generica (pagine legali, editoriali).</summary>
public sealed class Pagina : CmsEntity
{
    public string Titolo { get; set; } = "";
    public string Sottotitolo { get; set; } = "";
    /// <summary>Corpo HTML della pagina.</summary>
    public string Corpo { get; set; } = "";
    public bool MostraInFooter { get; set; }
}

/// <summary>Voce di navigazione (menu principale o footer).</summary>
public sealed class VoceMenu : CmsEntity
{
    public string Etichetta { get; set; } = "";
    public string Url { get; set; } = "";
    public PosizioneMenu Posizione { get; set; } = PosizioneMenu.Principale;
    public string Icona { get; set; } = "";
    public bool Evidenzia { get; set; }
    /// <summary>data-element dell'App di valutazione da emettere sul link.</summary>
    public string DataElement { get; set; } = "";
}

/// <summary>Impostazione chiave/valore (dati dell'ente, hero, contatti, footer).</summary>
public sealed class Impostazione : CmsEntity
{
    public string Chiave { get; set; } = "";
    public string Valore { get; set; } = "";
    public string Gruppo { get; set; } = "Generale";
    public string Etichetta { get; set; } = "";
}

/// <summary>Domanda frequente (C.SI.2.3).</summary>
public sealed class FaqItem : CmsEntity
{
    public string Domanda { get; set; } = "";
    public string Risposta { get; set; } = "";
    public string Categoria { get; set; } = "Generale";
}

/// <summary>Prenotazione di un appuntamento presso un ufficio (C.SI.2.1). Dato applicativo, non contenuto.</summary>
public sealed class Appuntamento : CmsEntity
{
    public int UfficioId { get; set; }
    public UnitaOrganizzativa? Ufficio { get; set; }
    public DateOnly Data { get; set; }
    public TimeOnly Ora { get; set; }
    public string Argomento { get; set; } = "";
    public string Motivo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public string Telefono { get; set; } = "";
    /// <summary>Codice univoco della prenotazione mostrato al cittadino.</summary>
    public string Codice { get; set; } = "";
    public bool Annullato { get; set; }
}

/// <summary>Segnalazione di disservizio (C.SI.2.4). Dato applicativo, non contenuto.</summary>
public sealed class Segnalazione : CmsEntity
{
    public string Categoria { get; set; } = "";
    public string Indirizzo { get; set; } = "";
    public string Oggetto { get; set; } = "";
    public string Descrizione { get; set; } = "";
    /// <summary>Percorsi dei file allegati (immagini e documenti).</summary>
    public List<string> Allegati { get; set; } = new();
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public StatoSegnalazione Stato { get; set; } = StatoSegnalazione.Ricevuta;
    public string Codice { get; set; } = "";
}

/// <summary>Risposta del widget di valutazione della chiarezza (C.SI.2.5 / C.SI.2.6).</summary>
public sealed class ValutazionePagina : CmsEntity
{
    public string Url { get; set; } = "";
    public string TitoloPagina { get; set; } = "";
    /// <summary>Voto 1-5 (scala stelline).</summary>
    public int Voto { get; set; }
    /// <summary>Risposte alla domanda di follow-up selezionate.</summary>
    public List<string> Risposte { get; set; } = new();
    public string Commento { get; set; } = "";
}

/// <summary>File della libreria media (immagini e allegati caricati dall'admin).</summary>
public sealed class MediaFile : CmsEntity
{
    public string FileName { get; set; } = "";
    public string Url { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    /// <summary>Testo alternativo per l'accessibilità.</summary>
    public string Alt { get; set; } = "";
}
