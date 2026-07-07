using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Duckburg.Registry.Corpus;

/// <summary>
/// Unico proprietario dei dati: carica out/corpus.json all'avvio e tiene i passaggi in memoria.
/// </summary>
public sealed class CorpusService
{
    private readonly CorpusDocument _corpus;
    private readonly IReadOnlyList<(Work Work, Passage Passage, string NormalizedText)> _index;

    public CorpusService(IConfiguration configuration, IWebHostEnvironment env, ILogger<CorpusService> logger)
    {
        var path = configuration["Corpus:Path"]
                   ?? throw new InvalidOperationException("Configurazione mancante: Corpus:Path");
        if (!Path.IsPathRooted(path))
            path = Path.GetFullPath(Path.Combine(env.ContentRootPath, path));

        _corpus = JsonSerializer.Deserialize<CorpusDocument>(File.ReadAllText(path))
                  ?? throw new InvalidOperationException($"Corpus non valido: {path}");

        _index = _corpus.Works
            .SelectMany(w => w.Passages.Select(p => (w, p, Normalize($"{w.Title} {p.Text}"))))
            .ToList();

        logger.LogInformation("Corpus {Version} caricato da {Path}: {Works} aree, {Passages} passaggi",
            _corpus.CorpusVersion, path, _corpus.Works.Count, _index.Count);
    }

    public CorpusDocument Document => _corpus;

    public IReadOnlyList<Work> Works => _corpus.Works;

    public Work? GetWork(string id) =>
        _corpus.Works.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<SearchHit> Search(string query, int limit = 5)
    {
        var terms = Normalize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 1)
            .Distinct()
            .ToArray();
        if (terms.Length == 0) return [];

        return _index
            .Select(e => (e.Work, e.Passage, Score: Score(e.NormalizedText, Normalize(e.Work.Title), terms)))
            .Where(e => e.Score > 0)
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.Passage.Seq)
            .Take(limit)
            .Select(e => new SearchHit(
                e.Passage.Id, e.Passage.Version, e.Passage.Hash, e.Passage.Text,
                e.Work.Id, e.Work.Title, Math.Round(e.Score, 2)))
            .ToList();
    }

    private static double Score(string normalizedText, string normalizedTitle, string[] terms)
    {
        double score = 0;
        foreach (var term in terms)
        {
            var occurrences = CountOccurrences(normalizedText, term);
            if (occurrences == 0) continue;
            score += occurrences;
            if (normalizedTitle.Contains(term)) score += 2; // il titolo dell'area pesa di piu
        }
        return score;
    }

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
}
