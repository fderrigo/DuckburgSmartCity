using Microsoft.EntityFrameworkCore;

namespace Duckburg.Portal.Cms.Admin;

/// <summary>Catalogo dei tipi di contenuto gestibili dall'area di amministrazione.</summary>
public static class AdminRegistry
{
    private static Func<CmsDbContext, Task<List<(string, string)>>> ArgomentiOptions => async db =>
        (await db.Argomenti.OrderBy(a => a.Nome).ToListAsync())
        .Select(a => (a.Id.ToString(), a.Nome)).ToList();

    private static Func<CmsDbContext, Task<List<(string, string)>>> CategorieOptions => async db =>
        (await db.CategorieServizio.OrderBy(c => c.Ordine).ToListAsync())
        .Select(c => (c.Id.ToString(), c.Nome)).ToList();

    private static Func<CmsDbContext, Task<List<(string, string)>>> UfficiOptions => async db =>
        (await db.Unita.OrderBy(u => u.Nome).ToListAsync())
        .Select(u => (u.Id.ToString(), u.Nome)).ToList();

    private const string SPub = "Pubblicazione";
    private const string SDett = "Dettagli";
    private const string SClass = "Classificazione";
    private const string SContatti = "Contatti";

    private static FieldDef Slug => new() { Prop = "Slug", Label = "Slug (URL)", Kind = FieldKind.Text, Sezione = SPub, Mezza = true,
        Aiuto = "Identificativo in URL. Si genera da solo dal titolo, puoi correggerlo." };
    private static FieldDef Pubblicato => new() { Prop = "IsPublished", Label = "Pubblicato", Kind = FieldKind.Bool, Sezione = SPub, Mezza = true,
        Aiuto = "Spento = bozza, non visibile sul sito." };
    private static FieldDef Ordine => new() { Prop = "Ordine", Label = "Ordine", Kind = FieldKind.Int, Sezione = SPub, Mezza = true,
        Aiuto = "Numero più basso = mostrato prima." };

    public static readonly List<EntityDef> All = new()
    {
        new EntityDef
        {
            Key = "servizi", Singolare = "Servizio", Plurale = "Servizi", Emoji = "🧾",
            Type = typeof(Servizio), ColonneLista = new[] { "Titolo", "Slug" },
            Descrizione = "Schede servizio del modello Comuni.",
            Campi = new()
            {
                new() { Prop = "Titolo", Label = "Titolo" },
                new() { Prop = "Sottotitolo", Label = "Sottotitolo", Aiuto = "Una riga sotto il titolo della scheda." },
                new() { Prop = "DescrizioneBreve", Label = "Descrizione breve", Kind = FieldKind.Multiline, Aiuto = "Usata nelle card di elenco." },
                new() { Prop = "Attivo", Label = "Servizio attivo", Kind = FieldKind.Bool, Mezza = true, Aiuto = "Se spento la scheda mostra lo stato non attivo." },
                new() { Prop = "MotivoStato", Label = "Motivo dello stato", Mezza = true, Aiuto = "Compila solo se il servizio non è attivo." },
                new() { Prop = "AChiERivolto", Label = "A chi è rivolto", Kind = FieldKind.Multiline, Sezione = SDett },
                new() { Prop = "Descrizione", Label = "Descrizione", Kind = FieldKind.Multiline, Sezione = SDett },
                new() { Prop = "ComeFare", Label = "Come fare", Kind = FieldKind.Multiline, Sezione = SDett },
                new() { Prop = "CosaServe", Label = "Cosa serve", Kind = FieldKind.Multiline, Sezione = SDett },
                new() { Prop = "CosaSiOttiene", Label = "Cosa si ottiene", Kind = FieldKind.Multiline, Sezione = SDett },
                new() { Prop = "Tempi", Label = "Tempi e scadenze (testo)", Kind = FieldKind.Multiline, Sezione = SDett },
                new() { Prop = "Scadenze", Label = "Scadenze (una per riga)", Kind = FieldKind.StringList, Sezione = SDett,
                    Aiuto = "Formato: \"30 aprile — Prima rata\"." },
                new() { Prop = "Costi", Label = "Costi", Kind = FieldKind.Multiline, Sezione = SDett,
                    Aiuto = "Se il servizio prevede un pagamento, l'informazione è obbligatoria (C.SI.1.3)." },
                new() { Prop = "CondizioniServizio", Label = "Condizioni di servizio", Kind = FieldKind.Multiline, Sezione = SDett },
                new() { Prop = "CondizioniServizioUrl", Label = "Documento condizioni (URL)", Sezione = SDett, Mezza = true,
                    Aiuto = "Link al regolamento o documento delle condizioni." },
                new() { Prop = "Fonti", Label = "Fonti certificate (una per riga)", Kind = FieldKind.StringList, Sezione = SDett },
                new() { Prop = "CategoriaId", Label = "Categoria di servizio", Kind = FieldKind.Select, Options = CategorieOptions, Sezione = SClass, Mezza = true,
                    Aiuto = "Pagina di secondo livello in cui compare la scheda." },
                new() { Prop = "ArgomentoId", Label = "Argomento", Kind = FieldKind.Select, Options = ArgomentiOptions, Sezione = SClass, Mezza = true },
                new() { Prop = "UnitaOrganizzativaId", Label = "Ufficio responsabile", Kind = FieldKind.Select, Options = UfficiOptions, Sezione = SClass, Mezza = true },
                new() { Prop = "IconaKey", Label = "Icona", Kind = FieldKind.Select, Sezione = SClass, Mezza = true,
                    Options = _ => Task.FromResult(new[] { "tari","acqua","rossa","casa","bus","pagamento","orologio","documento" }.Select(x => (x, x)).ToList()) },
                new() { Prop = "ColoreIcona", Label = "Colore icona", Kind = FieldKind.Select, Sezione = SClass, Mezza = true,
                    Options = _ => Task.FromResult(new[] { "oro","acqua","rossa" }.Select(x => (x, x)).ToList()) },
                new() { Prop = "InEvidenza", Label = "In evidenza in home", Kind = FieldKind.Bool, Sezione = SClass, Mezza = true },
                Slug, Pubblicato, Ordine,
            }
        },
        new EntityDef
        {
            Key = "categorie", Singolare = "Categoria di servizio", Plurale = "Categorie servizio", Emoji = "🗂️",
            Type = typeof(CategoriaServizio), ColonneLista = new[] { "Nome", "Slug" },
            Descrizione = "Pagine di secondo livello della sezione Servizi (vocabolario del modello).",
            Campi = new()
            {
                new() { Prop = "Nome", Label = "Nome", Aiuto = "Deve rispettare il vocabolario del modello Comuni (C.SI.1.7)." },
                new() { Prop = "Descrizione", Label = "Descrizione", Kind = FieldKind.Multiline },
                Slug, Pubblicato, Ordine,
            }
        },
        new EntityDef
        {
            Key = "novita", Singolare = "Novità", Plurale = "Novità", Emoji = "📰",
            Type = typeof(Novita), ColonneLista = new[] { "Titolo", "Data" },
            Descrizione = "Notizie, comunicati e avvisi.",
            Campi = new()
            {
                new() { Prop = "Titolo", Label = "Titolo" },
                new() { Prop = "Tipo", Label = "Tipo", Kind = FieldKind.Select, EnumType = typeof(TipoNovita), Mezza = true },
                new() { Prop = "Data", Label = "Data", Kind = FieldKind.Date, Mezza = true },
                new() { Prop = "Sommario", Label = "Sommario", Kind = FieldKind.Multiline },
                new() { Prop = "Corpo", Label = "Testo completo", Kind = FieldKind.Multiline },
                new() { Prop = "ImmagineUrl", Label = "Immagine", Kind = FieldKind.Image, Sezione = SClass },
                new() { Prop = "ACuraDiId", Label = "A cura di (ufficio)", Kind = FieldKind.Select, Options = UfficiOptions, Sezione = SClass, Mezza = true },
                new() { Prop = "ArgomentoId", Label = "Argomento", Kind = FieldKind.Select, Options = ArgomentiOptions, Sezione = SClass, Mezza = true },
                new() { Prop = "InEvidenza", Label = "In evidenza in home", Kind = FieldKind.Bool, Sezione = SClass, Mezza = true },
                Slug, Pubblicato, Ordine,
            }
        },
        new EntityDef
        {
            Key = "persone", Singolare = "Persona", Plurale = "Persone e organi", Emoji = "👥",
            Type = typeof(Persona), ColonneLista = new[] { "Nome", "Carica" },
            Descrizione = "Sindaco, giunta e responsabili.",
            Campi = new()
            {
                new() { Prop = "Nome", Label = "Nome e cognome" },
                new() { Prop = "Ruolo", Label = "Ruolo", Kind = FieldKind.Select, EnumType = typeof(RuoloPersona), Mezza = true },
                new() { Prop = "Carica", Label = "Carica mostrata", Mezza = true, Aiuto = "Es. \"Sindaca\", \"Vicesindaco\"." },
                new() { Prop = "Biografia", Label = "Biografia", Kind = FieldKind.Multiline },
                new() { Prop = "Deleghe", Label = "Deleghe (una per riga)", Kind = FieldKind.StringList },
                new() { Prop = "Ricevimento", Label = "Ricevimento", Kind = FieldKind.Multiline, Sezione = SContatti },
                new() { Prop = "Email", Label = "Email", Sezione = SContatti, Mezza = true },
                new() { Prop = "Telefono", Label = "Telefono", Sezione = SContatti, Mezza = true },
                new() { Prop = "ImmagineUrl", Label = "Fotografia", Kind = FieldKind.Image, Sezione = SClass,
                    Aiuto = "Se presente sostituisce il ritratto SVG." },
                new() { Prop = "RitrattoSvg", Label = "Ritratto SVG (avanzato)", Kind = FieldKind.Html, Sezione = SClass },
                Slug, Pubblicato, Ordine,
            }
        },
        new EntityDef
        {
            Key = "uffici", Singolare = "Ufficio", Plurale = "Uffici e aree", Emoji = "🏛️",
            Type = typeof(UnitaOrganizzativa), ColonneLista = new[] { "Nome", "Responsabile" },
            Descrizione = "Unità organizzative dell'ente.",
            Campi = new()
            {
                new() { Prop = "Nome", Label = "Nome" },
                new() { Prop = "Tipo", Label = "Tipo", Kind = FieldKind.Select, EnumType = typeof(TipoUnita), Mezza = true },
                new() { Prop = "Responsabile", Label = "Responsabile", Mezza = true },
                new() { Prop = "Descrizione", Label = "Descrizione", Kind = FieldKind.Multiline },
                new() { Prop = "Competenze", Label = "Competenze (una per riga)", Kind = FieldKind.StringList,
                    Aiuto = "Elenco dei compiti assegnati alla struttura." },
                new() { Prop = "Sede", Label = "Sede", Sezione = SContatti },
                new() { Prop = "Orari", Label = "Orari", Sezione = SContatti },
                new() { Prop = "Telefono", Label = "Telefono", Sezione = SContatti, Mezza = true },
                new() { Prop = "Email", Label = "Email", Sezione = SContatti, Mezza = true },
                new() { Prop = "Pec", Label = "PEC", Sezione = SContatti, Mezza = true },
                new() { Prop = "Prenotabile", Label = "Prenotabile (appuntamenti)", Kind = FieldKind.Bool, Sezione = SContatti, Mezza = true },
                Slug, Pubblicato, Ordine,
            }
        },
        new EntityDef
        {
            Key = "luoghi", Singolare = "Luogo", Plurale = "Luoghi", Emoji = "📍",
            Type = typeof(Luogo), ColonneLista = new[] { "Nome", "Categoria" },
            Descrizione = "Luoghi del territorio (Vivere il Comune).",
            Campi = new()
            {
                new() { Prop = "Nome", Label = "Nome" },
                new() { Prop = "Categoria", Label = "Categoria", Mezza = true, Aiuto = "Es. Piazza, Parco, Cultura." },
                new() { Prop = "Descrizione", Label = "Descrizione", Kind = FieldKind.Multiline },
                new() { Prop = "ImmagineUrl", Label = "Immagine", Kind = FieldKind.Image },
                new() { Prop = "Indirizzo", Label = "Indirizzo", Sezione = SDett },
                new() { Prop = "ModalitaAccesso", Label = "Modalità di accesso", Kind = FieldKind.Multiline, Sezione = SDett,
                    Aiuto = "Come arrivare, accessibilità, eventuali costi." },
                new() { Prop = "Orari", Label = "Orari", Sezione = SDett, Mezza = true },
                new() { Prop = "Contatti", Label = "Contatti", Sezione = SDett, Mezza = true },
                Slug, Pubblicato, Ordine,
            }
        },
        new EntityDef
        {
            Key = "eventi", Singolare = "Evento", Plurale = "Eventi", Emoji = "🎪",
            Type = typeof(Evento), ColonneLista = new[] { "Titolo", "Ricorrenza" },
            Descrizione = "Eventi del territorio.",
            Campi = new()
            {
                new() { Prop = "Titolo", Label = "Titolo" },
                new() { Prop = "Sommario", Label = "Sommario", Kind = FieldKind.Multiline },
                new() { Prop = "Descrizione", Label = "Descrizione", Kind = FieldKind.Multiline },
                new() { Prop = "ImmagineUrl", Label = "Immagine", Kind = FieldKind.Image },
                new() { Prop = "DataInizio", Label = "Data inizio", Kind = FieldKind.Date, Sezione = SDett, Mezza = true },
                new() { Prop = "DataFine", Label = "Data fine", Kind = FieldKind.Date, Sezione = SDett, Mezza = true },
                new() { Prop = "Orario", Label = "Orario", Sezione = SDett, Mezza = true },
                new() { Prop = "Ricorrenza", Label = "Ricorrenza (testo card)", Sezione = SDett, Mezza = true,
                    Aiuto = "Es. \"Mercato · ogni sabato\"." },
                new() { Prop = "LuogoTesto", Label = "Luogo", Sezione = SDett, Mezza = true },
                new() { Prop = "Costo", Label = "Costo", Sezione = SDett, Mezza = true },
                new() { Prop = "Contatti", Label = "Contatti", Sezione = SDett },
                Slug, Pubblicato, Ordine,
            }
        },
        new EntityDef
        {
            Key = "documenti", Singolare = "Documento", Plurale = "Documenti e dati", Emoji = "📄",
            Type = typeof(Documento), ColonneLista = new[] { "Titolo", "Data" },
            Descrizione = "Documenti pubblici, normativa e dataset.",
            Campi = new()
            {
                new() { Prop = "Titolo", Label = "Titolo" },
                new() { Prop = "Tipo", Label = "Tipo", Kind = FieldKind.Select, EnumType = typeof(TipoDocumento), Mezza = true },
                new() { Prop = "Data", Label = "Data", Kind = FieldKind.Date, Mezza = true },
                new() { Prop = "Descrizione", Label = "Descrizione", Kind = FieldKind.Multiline },
                new() { Prop = "UrlFile", Label = "File del documento", Kind = FieldKind.Image,
                    Aiuto = "Carica o scegli il file principale dalla libreria." },
                new() { Prop = "UfficioResponsabileId", Label = "Ufficio responsabile", Kind = FieldKind.Select, Options = UfficiOptions, Sezione = SClass, Mezza = true },
                Slug, Pubblicato, Ordine,
            }
        },
        new EntityDef
        {
            Key = "argomenti", Singolare = "Argomento", Plurale = "Argomenti", Emoji = "🏷️",
            Type = typeof(Argomento), ColonneLista = new[] { "Nome", "Slug" },
            Descrizione = "Tassonomia argomenti del modello (vocabolario controllato).",
            Campi = new()
            {
                new() { Prop = "Nome", Label = "Nome", Aiuto = "Deve appartenere alla Tassonomia argomenti del modello o a EuroVoc (C.SI.1.5)." },
                new() { Prop = "Descrizione", Label = "Descrizione", Kind = FieldKind.Multiline },
                new() { Prop = "Colore", Label = "Colore", Kind = FieldKind.Select, Mezza = true,
                    Options = _ => Task.FromResult(new[] { "oro","acqua","rossa" }.Select(x => (x, x)).ToList()) },
                Slug, Pubblicato, Ordine,
            }
        },
        new EntityDef
        {
            Key = "faq", Singolare = "Domanda frequente", Plurale = "Domande frequenti", Emoji = "❓",
            Type = typeof(FaqItem), ColonneLista = new[] { "Domanda", "Categoria" },
            Descrizione = "Le FAQ mostrate nella pagina Domande frequenti.",
            Campi = new()
            {
                new() { Prop = "Domanda", Label = "Domanda" },
                new() { Prop = "Risposta", Label = "Risposta", Kind = FieldKind.Multiline },
                new() { Prop = "Categoria", Label = "Categoria", Mezza = true },
                Slug, Pubblicato, Ordine,
            }
        },
        new EntityDef
        {
            Key = "pagine", Singolare = "Pagina", Plurale = "Pagine", Emoji = "📃",
            Type = typeof(Pagina), ColonneLista = new[] { "Titolo", "Slug" },
            Descrizione = "Pagine di contenuto (legali, editoriali).",
            Campi = new()
            {
                new() { Prop = "Titolo", Label = "Titolo" },
                new() { Prop = "Sottotitolo", Label = "Sopratitolo", Mezza = true },
                new() { Prop = "Corpo", Label = "Corpo (HTML)", Kind = FieldKind.Html },
                new() { Prop = "MostraInFooter", Label = "Mostra nel footer", Kind = FieldKind.Bool, Sezione = SPub, Mezza = true },
                Slug, Pubblicato, Ordine,
            }
        },
        new EntityDef
        {
            Key = "menu", Singolare = "Voce di menu", Plurale = "Menu e navigazione", Emoji = "🧭",
            Type = typeof(VoceMenu), ColonneLista = new[] { "Etichetta", "Url" },
            Descrizione = "Voci del menu principale e del footer.",
            Campi = new()
            {
                new() { Prop = "Etichetta", Label = "Etichetta" },
                new() { Prop = "Url", Label = "URL", Mezza = true },
                new() { Prop = "Posizione", Label = "Posizione", Kind = FieldKind.Select, EnumType = typeof(PosizioneMenu), Mezza = true },
                new() { Prop = "Icona", Label = "Icona (emoji)", Mezza = true },
                new() { Prop = "Evidenzia", Label = "Evidenzia", Kind = FieldKind.Bool, Mezza = true },
                new() { Prop = "DataElement", Label = "data-element (App valutazione)", Mezza = true,
                    Aiuto = "Attributo tecnico richiesto dall'App di valutazione. Non modificare se non sai cos'è." },
                Slug, Pubblicato, Ordine,
            }
        },
        new EntityDef
        {
            Key = "impostazioni", Singolare = "Impostazione", Plurale = "Impostazioni del sito", Emoji = "⚙️",
            Type = typeof(Impostazione), ColonneLista = new[] { "Etichetta", "Valore", "Gruppo" },
            Descrizione = "Dati dell'ente, contatti, hero e footer.",
            Campi = new()
            {
                new() { Prop = "Etichetta", Label = "Etichetta" },
                new() { Prop = "Chiave", Label = "Chiave", Mezza = true },
                new() { Prop = "Gruppo", Label = "Gruppo", Mezza = true },
                new() { Prop = "Valore", Label = "Valore", Kind = FieldKind.Multiline },
                Ordine,
            }
        },
    };

    public static EntityDef? Find(string key) =>
        All.FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
