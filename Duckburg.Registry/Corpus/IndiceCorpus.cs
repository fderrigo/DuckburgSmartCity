using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Duckburg.Registry.Corpus;

/// <summary>
/// L'indice in memoria del corpus, e il recupero in due stadi.
/// <para>
/// Il recupero e' diviso perche' l'unita' di significato e' il contenuto, non il
/// frammento. Primo stadio: si cerca la scheda, valutando titolo, tipo, fatti e prosa
/// come un documento unico. Secondo stadio: dentro le schede migliori si scelgono le
/// sezioni pertinenti, e si restituiscono raggruppate sotto la scheda.
/// </para>
/// <para>
/// Cosi' "quanto costa la mensa" trova la scheda della mensa perche' il documento
/// contiene sia "mensa" sia "costi", e poi ne mostra la sezione dei costi. Ordinando
/// frammenti isolati, come si faceva prima, vincevano i testi piu' lunghi o quelli piu'
/// corti a seconda di come si tarava il punteggio: mai quello giusto.
/// </para>
/// </summary>
public sealed class IndiceCorpus
{
    private readonly Documento[] _documenti;
    private readonly Dictionary<string, Documento> _perId;
    private readonly Dictionary<string, int> _frequenzaDocumentale;
    private readonly Dictionary<string, int> _frequenzaSezionale;
    private readonly int _numeroSezioni;
    private readonly double _lunghezzaMedia;

    public Istantanea Istantanea { get; }
    public int NumeroContenuti => _documenti.Length;
    public int NumeroSezioni => _documenti.Sum(d => d.Contenuto.Sezioni.Count);

    public IndiceCorpus(Istantanea istantanea)
    {
        Istantanea = istantanea;

        _documenti = istantanea.Contenuti.Select(c => new Documento(c)).ToArray();
        _perId = _documenti.ToDictionary(d => d.Contenuto.Id, StringComparer.OrdinalIgnoreCase);

        // Frequenza documentale: un termine che compare ovunque non discrimina. Senza
        // questo, parole come "comune" o "servizio" pesano quanto "mensa".
        _frequenzaDocumentale = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var d in _documenti)
            foreach (var t in d.TerminiDistinti)
                _frequenzaDocumentale[t] = _frequenzaDocumentale.GetValueOrDefault(t) + 1;

        _lunghezzaMedia = _documenti.Length == 0 ? 1 : _documenti.Average(d => d.Termini.Length);

        // La stessa statistica serve a livello di sezione: nel secondo stadio l'unita' e'
        // la sezione, e "servizio" compare in quasi tutte mentre "mensa" in poche. Senza
        // questo peso, una sezione intitolata "Condizioni di servizio" batte quella dei
        // costi su una domanda che chiede quanto costa.
        _frequenzaSezionale = new Dictionary<string, int>(StringComparer.Ordinal);
        var sezioni = _documenti.SelectMany(d => d.Sezioni).ToArray();
        _numeroSezioni = sezioni.Length;
        foreach (var s in sezioni)
            foreach (var t in s.TerminiDistinti)
                _frequenzaSezionale[t] = _frequenzaSezionale.GetValueOrDefault(t) + 1;
    }

    // --------------------------------------------------------------------- ricerca

    public IReadOnlyList<RisultatoRicerca> Cerca(string query, string? tipo, int limite, int sezioniPerContenuto)
    {
        var termini = Termini(query);
        if (termini.Length == 0) return [];

        var adesso = DateTimeOffset.UtcNow;

        var candidati = _documenti
            .Where(d => tipo is null || string.Equals(d.Contenuto.Tipo, tipo, StringComparison.OrdinalIgnoreCase))
            .Select(d => (Doc: d, Punteggio: PunteggioDocumento(d, termini)))
            .Where(x => x.Punteggio > 0)
            .OrderByDescending(x => x.Punteggio)
            .Take(limite)
            .ToList();

        return candidati.Select(x => new RisultatoRicerca(
            x.Doc.Contenuto.Id,
            x.Doc.Contenuto.Tipo,
            x.Doc.Contenuto.Titolo,
            x.Doc.Contenuto.Url,
            Math.Round(x.Punteggio, 2),
            x.Doc.Contenuto.Validita?.ValidoAl(adesso) ?? true,
            // I fatti si restituiscono sempre e per intero: sono pochi, tipizzati, e
            // sono spesso la risposta esatta a "quanto costa" o "quando scade".
            x.Doc.Contenuto.Attributi
                .Select(a => new AttributoEsposto(a.Chiave, a.Etichetta, a.Tipo, a.Valore)).ToList(),
            SezioniPertinenti(x.Doc, termini, sezioniPerContenuto)
        )).ToList();
    }

    /// <summary>
    /// Secondo stadio: quali sezioni della scheda mostrare.
    /// Se nessuna spicca si restituiscono le prime nell'ordine redazionale, che nel
    /// modello Comuni e' gia' un ordine di importanza: meglio l'inizio della scheda che
    /// una sezione qualsiasi scelta da un pareggio.
    /// </summary>
    private IReadOnlyList<SezioneEsposta> SezioniPertinenti(Documento d, string[] termini, int quante)
    {
        var punteggiate = d.Sezioni
            .Select(s => (s.Sezione, Punteggio: PunteggioSezione(s, termini)))
            .ToList();

        var scelte = punteggiate.Where(x => x.Punteggio > 0)
            .OrderByDescending(x => x.Punteggio)
            .ThenBy(x => x.Sezione.Ordine)
            .Take(quante)
            .ToList();

        if (scelte.Count == 0)
            scelte = punteggiate.OrderBy(x => x.Sezione.Ordine).Take(quante).ToList();

        return scelte
            .OrderBy(x => x.Sezione.Ordine)
            .Select(x => new SezioneEsposta(
                x.Sezione.Id, x.Sezione.Etichetta ?? x.Sezione.Chiave,
                x.Sezione.Testo, x.Sezione.Versione, x.Sezione.Hash))
            .ToList();
    }

    /// <summary>
    /// Punteggio del documento: frequenza satura, pesata per rarita' del termine e
    /// normalizzata sulla lunghezza. Il titolo e il tipo contano come campi a se'.
    /// </summary>
    private double PunteggioDocumento(Documento d, string[] termini)
    {
        const double Saturazione = 1.2;
        const double PesoLunghezza = 0.4;

        double punteggio = 0;
        var coperti = 0;

        foreach (var termine in termini)
        {
            var frequenza = Occorrenze(d.Termini, termine);
            var nelTitolo = Presente(d.TerminiTitolo, termine);
            var nelTipo = Presente(d.TerminiTipo, termine);

            if (frequenza == 0 && !nelTitolo && !nelTipo) continue;
            coperti++;

            // Rarita': un termine presente in pochi documenti vale piu' di uno diffuso.
            var df = _frequenzaDocumentale.GetValueOrDefault(termine, 0);
            var idf = Math.Log(1 + (_documenti.Length - df + 0.5) / (df + 0.5));

            var norma = 1 - PesoLunghezza + PesoLunghezza * (d.Termini.Length / Math.Max(_lunghezzaMedia, 1));
            punteggio += idf * (frequenza * (Saturazione + 1)) / (frequenza + Saturazione * norma);

            if (nelTitolo) punteggio += 2.5 * idf;
            if (nelTipo) punteggio += 1.5;
        }

        if (coperti == 0) return 0;
        // Chi copre piu' termini della domanda vince su chi ne ripete uno solo.
        return punteggio * (0.5 + 0.5 * ((double)coperti / termini.Length));
    }

    private double PunteggioSezione(SezioneIndicizzata s, string[] termini)
    {
        double punteggio = 0;
        foreach (var termine in termini)
        {
            // Rarita' del termine fra le sezioni: senza, le parole che ricorrono ovunque
            // pesano quanto quelle che individuano davvero la sezione giusta.
            var df = _frequenzaSezionale.GetValueOrDefault(termine, 0);
            var idf = Math.Log(1 + (_numeroSezioni - df + 0.5) / (df + 0.5));

            var n = Occorrenze(s.Termini, termine);
            if (n > 0) punteggio += idf * (1 + Math.Log(n));

            // L'etichetta dice di che cosa parla la sezione: vale piu' del corpo.
            if (Presente(s.TerminiEtichetta, termine)) punteggio += 2 * idf;
        }
        return punteggio;
    }

    // ---------------------------------------------------------------- consultazione

    public Contenuto? Scheda(string id) => _perId.GetValueOrDefault(id)?.Contenuto;

    /// <summary>Contenuti collegati a una scheda, in entrambi i versi del grafo.</summary>
    public IReadOnlyList<VoceElenco> Collegati(string id)
    {
        var adesso = DateTimeOffset.UtcNow;
        var uscenti = _perId.GetValueOrDefault(id)?.Contenuto.Relazioni.Select(r => r.Verso) ?? [];
        var entranti = _documenti
            .Where(d => d.Contenuto.Relazioni.Any(r => string.Equals(r.Verso, id, StringComparison.OrdinalIgnoreCase)))
            .Select(d => d.Contenuto.Id);

        return uscenti.Concat(entranti).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => _perId.GetValueOrDefault(x)?.Contenuto)
            .Where(c => c is not null)
            .Select(c => Voce(c!, adesso))
            .ToList();
    }

    public IReadOnlyList<VoceElenco> Elenca(string? tipo, string? collegatoA, bool soloValidi, int limite)
    {
        var adesso = DateTimeOffset.UtcNow;
        IEnumerable<Contenuto> q = _documenti.Select(d => d.Contenuto);

        if (!string.IsNullOrWhiteSpace(tipo))
            q = q.Where(c => string.Equals(c.Tipo, tipo, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(collegatoA))
            q = q.Where(c => c.Relazioni.Any(r => string.Equals(r.Verso, collegatoA, StringComparison.OrdinalIgnoreCase)));

        if (soloValidi)
            q = q.Where(c => c.Validita?.ValidoAl(adesso) ?? true);

        return q.OrderBy(c => c.Tipo).ThenBy(c => c.Titolo).Take(limite)
                .Select(c => Voce(c, adesso)).ToList();
    }

    public IReadOnlyList<string> Tipi() =>
        _documenti.Select(d => d.Contenuto.Tipo).Distinct().OrderBy(t => t).ToList();

    private static VoceElenco Voce(Contenuto c, DateTimeOffset adesso) =>
        new(c.Id, c.Tipo, c.Titolo, c.Sommario, c.Url, c.Validita?.ValidoAl(adesso) ?? true);

    // ------------------------------------------------------------------ tokenizza

    /// <summary>
    /// Parole troppo comuni per discriminare. Senza scartarle, una domanda come
    /// "quanto costa il servizio mensa" viene vinta dai testi piu' lunghi, che
    /// contengono molte volte "il" e "servizio" senza parlare di mense.
    /// </summary>
    private static readonly HashSet<string> Vuote = new(StringComparer.Ordinal)
    {
        "il","lo","la","gli","le","un","uno","una","di","del","dei","della","delle","dello",
        "da","dal","dai","in","nel","nei","nella","con","su","sul","sulla","sui","per","tra",
        "fra","ed","che","chi","cosa","come","quanto","quanta","quanti","quante","quando",
        "dove","qual","quale","quali","non","piu","meno","al","allo","alla","ai","agli","alle",
        "si","se","sono","essere","ho","hai","ha","mi","ti","ci","vi","ne","io","tu","lui","lei",
        "noi","voi","loro","questo","questa","quello","quella","vorrei","posso","devo","mio","mia",
        "anche","ma","o","e","a","ed","del","sul","nel",
    };

    /// <summary>
    /// Termini della domanda. Le parole vuote si scartano, ma se la domanda e' fatta solo
    /// di quelle si ripiega su tutto: meglio un risultato mediocre che nessun risultato.
    /// </summary>
    public static string[] Termini(string testo)
    {
        var tutti = Tokenizza(testo).Where(t => t.Length > 1).Distinct(StringComparer.Ordinal).ToArray();
        var utili = tutti.Where(t => t.Length >= 3 && !Vuote.Contains(t)).ToArray();
        return utili.Length > 0 ? utili : tutti;
    }

    /// <summary>Conta le occorrenze di un termine, ammettendo la forma senza ultima lettera.</summary>
    private static int Occorrenze(string[] termini, string termine)
    {
        var radice = termine.Length >= 5 ? termine[..^1] : termine;
        var n = 0;
        foreach (var t in termini)
        {
            if (t.Equals(termine, StringComparison.Ordinal)) n++;
            // Riduzione morfologica povera ma efficace: senza, "costa" non trova "costi"
            // e "eventi" non trova "evento", che e' come si fanno le domande.
            else if (termine.Length >= 5 && t.StartsWith(radice, StringComparison.Ordinal)) n++;
        }
        return n;
    }

    private static bool Presente(string[] termini, string termine)
    {
        var radice = termine.Length >= 5 ? termine[..^1] : termine;
        foreach (var t in termini)
            if (t.Equals(termine, StringComparison.Ordinal)
                || (termine.Length >= 5 && t.StartsWith(radice, StringComparison.Ordinal))) return true;
        return false;
    }

    private static string[] Tokenizza(string s) =>
        Normalizza(s).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Normalizza(string s)
    {
        var formD = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue; // via gli accenti
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Sinonimi del tipo, per far funzionare "quali eventi ci sono" anche via ricerca.
    /// Il modo esatto resta lo strumento <c>elenca</c>, ma una domanda in linguaggio
    /// naturale non deve cadere nel vuoto solo perche' l'utente non sapeva dell'elenco.
    /// </summary>
    private static readonly Dictionary<string, string[]> SinonimiTipo = new(StringComparer.Ordinal)
    {
        ["servizio"] = ["servizio", "servizi", "scheda", "pratica"],
        ["evento"] = ["evento", "eventi", "manifestazione", "manifestazioni", "appuntamento"],
        ["luogo"] = ["luogo", "luoghi", "posto", "sede"],
        ["unita-organizzativa"] = ["ufficio", "uffici", "unita", "organizzativa", "sportello", "sportelli"],
        ["persona"] = ["persona", "amministratore", "sindaco", "assessore", "giunta", "consigliere"],
        ["documento"] = ["documento", "documenti", "modulo", "moduli", "regolamento", "delibera"],
        ["novita"] = ["novita", "notizia", "notizie", "avviso", "avvisi", "comunicato", "comunicati"],
        ["pagina"] = ["pagina", "pagine"],
        ["faq"] = ["faq", "domanda", "domande", "frequenti"],
        ["argomento"] = ["argomento", "argomenti", "tema", "temi"],
        ["categoria"] = ["categoria", "categorie"],
        ["ente"] = ["ente", "comune", "municipio"],
    };

    private sealed class Documento
    {
        public Contenuto Contenuto { get; }
        public string[] Termini { get; }
        public string[] TerminiTitolo { get; }
        public string[] TerminiTipo { get; }
        public HashSet<string> TerminiDistinti { get; }
        public SezioneIndicizzata[] Sezioni { get; }

        public Documento(Contenuto c)
        {
            Contenuto = c;
            TerminiTitolo = Tokenizza(c.Titolo);
            TerminiTipo = SinonimiTipo.GetValueOrDefault(c.Tipo, [c.Tipo]);

            Sezioni = c.Sezioni.Select(s => new SezioneIndicizzata(s)).ToArray();

            // Il documento indicizzato comprende titolo, sommario, prosa e testo dei
            // fatti: e' su tutto questo che si decide se la scheda parla dell'argomento.
            var pezzi = new List<string> { c.Titolo, c.Sommario ?? "" };
            pezzi.AddRange(c.Sezioni.Select(s => $"{s.Etichetta} {s.Testo}"));
            pezzi.AddRange(c.Attributi.Select(a => $"{a.Etichetta} {a.Chiave} {TestoValore(a.Valore)}"));
            pezzi.AddRange(c.Relazioni.Select(r => r.Etichetta ?? ""));

            Termini = Tokenizza(string.Join(' ', pezzi));
            TerminiDistinti = new HashSet<string>(Termini, StringComparer.Ordinal);
        }

        /// <summary>Rende cercabile anche il contenuto dei fatti, non solo la loro chiave.</summary>
        private static string TestoValore(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToString(),
            JsonValueKind.Array or JsonValueKind.Object => v.GetRawText(),
            _ => "",
        };
    }

    private sealed class SezioneIndicizzata
    {
        public Sezione Sezione { get; }
        public string[] Termini { get; }
        public string[] TerminiEtichetta { get; }
        public HashSet<string> TerminiDistinti { get; }

        public SezioneIndicizzata(Sezione s)
        {
            Sezione = s;
            Termini = Tokenizza(s.Testo);
            TerminiEtichetta = Tokenizza($"{s.Etichetta} {s.Chiave}");
            TerminiDistinti = new HashSet<string>(Termini.Concat(TerminiEtichetta), StringComparer.Ordinal);
        }
    }
}
