using System.Globalization;
using System.Text;

namespace Duckburg.Registry.Corpus;

/// <summary>
/// Come combinare le sorgenti del corpus.
/// <see cref="Replace"/>: vince la sorgente disponibile piu' autorevole, le altre
/// restano ripiego (nessun rischio di contenuti divergenti fra CMS e file).
/// <see cref="Merge"/>: unione di tutte le sorgenti; a parita' di id di un'area
/// vince comunque la piu' autorevole.
/// </summary>
public enum CorpusMergeMode { Replace, Merge }

/// <summary>
/// Unico proprietario dei dati: tiene in memoria i passaggi e l'indice di ricerca.
/// I contenuti arrivano dalle <see cref="ICorpusSource"/> configurate (feed del CMS
/// del portale e/o corpus statico su file) e si possono ricaricare a caldo senza
/// riavviare il server MCP.
/// </summary>
public sealed class CorpusService
{
    private readonly IReadOnlyList<ICorpusSource> _sources;
    private readonly CorpusMergeMode _mode;
    private readonly ILogger<CorpusService> _logger;

    private volatile Snapshot _snapshot = Snapshot.Vuoto;

    public CorpusService(IEnumerable<ICorpusSource> sources, IConfiguration configuration, ILogger<CorpusService> logger)
    {
        _sources = sources.OrderByDescending(s => s.Priorita).ToList();
        _mode = Enum.TryParse<CorpusMergeMode>(configuration["Corpus:Merge"], ignoreCase: true, out var m)
            ? m
            : CorpusMergeMode.Replace;
        _logger = logger;
    }

    public CorpusDocument Document => _snapshot.Document;

    public IReadOnlyList<Work> Works => _snapshot.Document.Works;

    /// <summary>Sorgenti che hanno risposto all'ultimo caricamento.</summary>
    public IReadOnlyList<string> SorgentiAttive => _snapshot.Sorgenti;

    /// <summary>Istante dell'ultimo caricamento andato a buon fine.</summary>
    public DateTime? CaricatoIl => _snapshot.CaricatoIl;

    public int PassageCount => _snapshot.Index.Count;

    public Work? GetWork(string id) =>
        _snapshot.Document.Works.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Interroga tutte le sorgenti e sostituisce l'indice in memoria. Lo scambio e'
    /// atomico: le richieste in corso continuano a leggere lo snapshot precedente.
    /// </summary>
    /// <param name="obbligatorio">
    /// Se true (avvio) un corpus vuoto e' un errore fatale; se false (refresh periodico)
    /// si tiene lo snapshot precedente e si prosegue.
    /// </param>
    public async Task<bool> ReloadAsync(bool obbligatorio, CancellationToken ct = default)
    {
        var caricati = new List<(ICorpusSource Source, CorpusDocument Document)>();
        foreach (var source in _sources)
        {
            try
            {
                var doc = await source.LoadAsync(ct);
                if (doc is not null && doc.Works.Count > 0) caricati.Add((source, doc));
            }
            catch (Exception ex)
            {
                // Una sorgente rotta non ferma le altre: se poi non ne resta nessuna,
                // il controllo qui sotto fallisce comunque, e all'avvio in modo fatale.
                _logger.LogError(ex, "Sorgente del corpus non disponibile: {Sorgente}", source.Nome);
            }
        }

        if (caricati.Count == 0)
        {
            if (obbligatorio)
                throw new InvalidOperationException(
                    "Nessuna sorgente del corpus disponibile. Sorgenti configurate: " +
                    (_sources.Count == 0 ? "nessuna" : string.Join(", ", _sources.Select(s => s.Nome))));

            _logger.LogWarning("Nessuna sorgente del corpus ha risposto: resta in uso il corpus caricato in precedenza.");
            return false;
        }

        var usate = _mode == CorpusMergeMode.Replace ? caricati.Take(1).ToList() : caricati;
        var document = Combina(usate.Select(c => c.Document).ToList());
        var precedente = _snapshot;
        _snapshot = Snapshot.Crea(document, usate.Select(c => c.Source.Nome).ToList());

        _logger.LogInformation(
            "Corpus {Version} caricato da [{Sorgenti}]: {Works} aree, {Passages} passaggi (prima: {Prima} passaggi)",
            document.CorpusVersion, string.Join(" + ", _snapshot.Sorgenti),
            document.Works.Count, _snapshot.Index.Count, precedente.Index.Count);

        if (_mode == CorpusMergeMode.Replace && caricati.Count > 1)
            _logger.LogInformation("Sorgenti di ripiego non usate: {Sorgenti}",
                string.Join(", ", caricati.Skip(1).Select(c => c.Source.Nome)));

        return true;
    }

    /// <summary>Fonde i documenti: a parita' di id di un'area vince il documento piu' autorevole (il primo).</summary>
    private static CorpusDocument Combina(IReadOnlyList<CorpusDocument> documenti)
    {
        if (documenti.Count == 1) return documenti[0];

        var works = new List<Work>();
        var visti = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in documenti)
            foreach (var work in doc.Works)
                if (visti.Add(work.Id))
                    works.Add(work);

        var primo = documenti[0];
        return new CorpusDocument(
            CorpusVersion: string.Join("+", documenti.Select(d => d.CorpusVersion)),
            GeneratedAt: DateTime.UtcNow.ToString("O"),
            Disclaimer: primo.Disclaimer,
            Principle: primo.Principle,
            Works: works);
    }

    /// <summary>
    /// Parole troppo comuni per discriminare. Senza questo elenco una domanda come
    /// "quanto costa il servizio mensa" viene vinta dai testi piu' lunghi, che
    /// contengono molte volte "il" e "servizio" senza parlare di mense.
    /// </summary>
    private static readonly HashSet<string> Vuote = new(StringComparer.Ordinal)
    {
        "il", "lo", "la", "gli", "le", "un", "uno", "una", "di", "del", "dei", "della",
        "delle", "dello", "da", "dal", "dai", "in", "nel", "nei", "nella", "con", "su",
        "sul", "per", "tra", "fra", "ed", "che", "chi", "cosa", "come", "quanto",
        "quanta", "quanti", "quante", "quando", "dove", "qual", "quale", "quali", "non",
        "piu", "meno", "al", "allo", "alla", "ai", "agli", "alle", "si", "se", "sono",
        "essere", "ho", "hai", "ha", "mi", "ti", "ci", "vi", "ne", "io", "tu", "lui",
        "lei", "noi", "voi", "loro", "questo", "questa", "quello", "quella", "vorrei",
        "posso", "devo", "mio", "mia", "sul", "sulla", "sui", "anche", "ma", "o", "e"
    };

    public IReadOnlyList<SearchHit> Search(string query, int limit = 5)
    {
        var index = _snapshot.Index;
        var tutti = Tokenizza(query).Where(t => t.Length > 1).Distinct().ToArray();
        // Le parole vuote si scartano, ma se la domanda e' fatta solo di quelle si
        // ripiega su tutto: meglio un risultato mediocre che nessun risultato.
        var terms = tutti.Where(t => t.Length >= 3 && !Vuote.Contains(t)).ToArray();
        if (terms.Length == 0) terms = tutti;
        if (terms.Length == 0) return [];

        return index
            .Select(e => (e.Work, e.Passage, Score: Score(e.Tokens, e.TitleTokens, terms, _snapshot.LunghezzaMedia)))
            .Where(e => e.Score > 0)
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.Passage.Seq)
            .Take(limit)
            .Select(e => new SearchHit(
                e.Passage.Id, e.Passage.Version, e.Passage.Hash, e.Passage.Text,
                e.Work.Id, e.Work.Title, Math.Round(e.Score, 2)))
            .ToList();
    }

    /// <summary>
    /// Punteggio di un passaggio, in stile BM25 semplificato.
    /// <list type="bullet">
    /// <item>confronto per parola intera, non per sottostringa: senza, "il" fa
    /// punteggio dentro "facile" e i testi lunghi vincono per accumulo;</item>
    /// <item>saturazione della frequenza: ripetere venti volte un termine non vale
    /// venti volte una;</item>
    /// <item>premio a chi copre piu' termini della domanda;</item>
    /// <item>penalita' di lunghezza mite, per non far vincere il passaggio piu' corto
    /// solo perche' e' corto.</item>
    /// </list>
    /// </summary>
    private static double Score(string[] corpo, string[] titolo, string[] terms, double lunghezzaMedia)
    {
        const double Saturazione = 1.2;
        double score = 0;
        var coperti = 0;

        foreach (var term in terms)
        {
            var radice = term.Length >= 5 ? term[..^1] : term;

            int esatte = 0, affini = 0;
            foreach (var tok in corpo)
            {
                if (tok.Equals(term, StringComparison.Ordinal)) esatte++;
                // Riduzione morfologica povera ma efficace: senza, "costa" non trova
                // "costi" e "eventi" non trova "evento", che e' come si fanno le domande.
                else if (term.Length >= 5 && tok.StartsWith(radice, StringComparison.Ordinal)) affini++;
            }
            var frequenza = esatte + 0.4 * affini;

            // Il titolo dell'area e' un campo a se': dice di CHE COSA parla il passaggio,
            // e vale come una corrispondenza forte ma non si somma per ripetizione. Senza
            // questa separazione, tutti i passaggi di una scheda risultano ugualmente
            // pertinenti e a decidere resta solo la lunghezza.
            var nelTitolo = titolo.Contains(term)
                || (term.Length >= 5 && titolo.Any(t => t.StartsWith(radice, StringComparison.Ordinal)));

            if (frequenza <= 0 && !nelTitolo) continue;
            coperti++;

            if (frequenza > 0) score += frequenza * (Saturazione + 1) / (frequenza + Saturazione);
            if (nelTitolo) score += 2;
        }

        if (coperti == 0) return 0;
        score += 1.5 * coperti;

        var rapporto = corpo.Length / Math.Max(lunghezzaMedia, 1);
        return score / (0.7 + 0.3 * rapporto);
    }

    /// <summary>Normalizza e spezza in parole: e' su queste che si confronta.</summary>
    private static string[] Tokenizza(string s) =>
        Normalize(s).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int CountOccurrences(string text, string term)
    {
        var count = 0;
        var i = 0;
        while ((i = text.IndexOf(term, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += term.Length;
        }
        return count;
    }

    private static string Normalize(string s)
    {
        var formD = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue; // via gli accenti
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sb.ToString();
    }

    /// <summary>Documento e indice pre-calcolato: si sostituiscono insieme, mai a meta'.</summary>
    private sealed record Snapshot(
        CorpusDocument Document,
        IReadOnlyList<(Work Work, Passage Passage, string[] Tokens, string[] TitleTokens)> Index,
        double LunghezzaMedia,
        IReadOnlyList<string> Sorgenti,
        DateTime? CaricatoIl)
    {
        public static readonly Snapshot Vuoto = new(
            new CorpusDocument("vuoto", DateTime.UtcNow.ToString("O"), "", null, []),
            [], 1, [], null);

        public static Snapshot Crea(CorpusDocument document, IReadOnlyList<string> sorgenti)
        {
            var index = document.Works
                .SelectMany(w =>
                {
                    var titolo = Tokenizza(w.Title);
                    return w.Passages.Select(p => (w, p, Tokenizza(p.Text), titolo));
                })
                .ToList();
            var media = index.Count == 0 ? 1 : index.Average(e => e.Item3.Length);
            return new Snapshot(document, index, media, sorgenti, DateTime.UtcNow);
        }
    }
}
