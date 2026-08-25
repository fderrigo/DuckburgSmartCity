using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Duckburg.Portal.Cms;

/// <summary>
/// Proiezione read-only dei contenuti pubblicati del CMS nel formato del corpus MCP.
/// E' la sorgente che <c>Duckburg.Registry</c> scarica da <c>GET /api/corpus</c>: il CMS
/// resta l'unico luogo dove la redazione scrive, il Registry resta l'unico proprietario
/// dell'indice di ricerca. Nessuno schema condiviso fra i due progetti, solo questo JSON.
/// </summary>
public sealed class CorpusFeed
{
    private const string DisclaimerDefault =
        "Comune di Paperopoli: ente immaginario. Dati di esempio a fini dimostrativi, non hanno alcun valore reale.";
    private const string PrincipioDefault =
        "L'assistente risponde solo dai passaggi qui esposti. Dati locali di Paperopoli, non regole nazionali generiche.";

    private readonly CmsDbContext _db;

    public CorpusFeed(CmsDbContext db) => _db = db;

    /// <summary>Costruisce il documento del corpus dai contenuti pubblicati.</summary>
    public async Task<CorpusFeedDocument> Build(CancellationToken ct = default)
    {
        var impostazioni = await _db.Impostazioni.AsNoTracking()
            .ToDictionaryAsync(i => i.Chiave, i => i.Valore, StringComparer.OrdinalIgnoreCase, ct);

        string Impostazione(string chiave, string fallback) =>
            impostazioni.TryGetValue(chiave, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

        var works = new List<CorpusFeedWork>();
        works.AddRange(await Servizi(ct));
        works.AddRange(await Uffici(ct));
        works.AddRange(await Amministratori(ct));
        works.AddRange(await NovitaWorks(ct));
        works.AddRange(await Eventi(ct));
        works.AddRange(await Luoghi(ct));
        works.AddRange(await Documenti(ct));
        works.AddRange(await Pagine(ct));
        var faq = await FaqWork(ct);
        if (faq is not null) works.Add(faq);
        var ente = Ente(impostazioni);
        if (ente is not null) works.Add(ente);

        // La versione cambia quando cambia un contenuto: e' l'istante dell'ultima modifica
        // in tutto il CMS, cosi' il Registry puo' accorgersi di un aggiornamento.
        var ultimaModifica = works
            .SelectMany(w => w.Passages)
            .Select(p => p.Version)
            .DefaultIfEmpty("0")
            .Max();

        return new CorpusFeedDocument(
            CorpusVersion: $"cms-{ultimaModifica}",
            GeneratedAt: DateTime.UtcNow.ToString("O"),
            Disclaimer: Impostazione("corpus.disclaimer", DisclaimerDefault),
            Principle: Impostazione("corpus.principio", PrincipioDefault),
            Works: works.Where(w => w.Passages.Count > 0).ToList());
    }

    // ---- Proiezione per tipo di contenuto ----

    /// <summary>
    /// Schede servizio. L'id del work e' lo slug nudo (es. "tari"): coincide con gli id
    /// del corpus statico, cosi' la scheda del CMS sostituisce quella del file.
    /// </summary>
    private async Task<List<CorpusFeedWork>> Servizi(CancellationToken ct)
    {
        var servizi = await _db.Servizi.AsNoTracking()
            .Include(s => s.UnitaOrganizzativa).Include(s => s.Categoria).Include(s => s.Argomento)
            .Where(s => s.IsPublished)
            .OrderBy(s => s.Ordine).ToListAsync(ct);

        return servizi.Select(s =>
        {
            var b = new PassageBuilder(s.Slug, s.UpdatedAt);
            if (!s.Attivo)
                b.Add("Stato del servizio", $"non attivo. {s.MotivoStato}");
            b.Add("In breve", Coalesce(s.DescrizioneBreve, s.Sottotitolo));
            b.Add("A chi e' rivolto", s.AChiERivolto);
            b.Add("Descrizione", s.Descrizione);
            b.Add("Come fare", s.ComeFare);
            b.Add("Cosa serve", s.CosaServe);
            b.Add("Cosa si ottiene", s.CosaSiOttiene);
            b.Add("Tempi e scadenze", s.Tempi);
            b.Add("Scadenze", Join(s.Scadenze));
            b.Add("Costi", s.Costi);
            b.Add("Condizioni di servizio", s.CondizioniServizio);
            b.Add("Argomento", s.Argomento?.Nome);
            b.Add("Categoria del servizio", s.Categoria?.Nome);
            if (s.UnitaOrganizzativa is { } u)
                b.Add("Ufficio responsabile", $"{u.Nome}. Sede: {u.Sede}. Orari: {u.Orari}. Telefono: {u.Telefono}. Email: {u.Email}");
            b.Add("Riferimenti normativi", Join(s.Fonti));
            b.Add("Pagina sul portale", $"/servizi/{s.Slug}");

            return b.Build($"Servizio: {s.Titolo}", "Scheda servizio del Comune di Paperopoli (CMS)");
        }).ToList();
    }

    private async Task<List<CorpusFeedWork>> Uffici(CancellationToken ct)
    {
        var uffici = await _db.Unita.AsNoTracking().Where(u => u.IsPublished)
            .OrderBy(u => u.Ordine).ToListAsync(ct);

        return uffici.Select(u =>
        {
            var b = new PassageBuilder($"ufficio:{u.Slug}", u.UpdatedAt);
            b.Add("Descrizione", u.Descrizione);
            b.Add("Competenze", Join(u.Competenze));
            b.Add("Sede", u.Sede);
            b.Add("Orari di apertura", u.Orari);
            b.Add("Contatti", $"Telefono: {u.Telefono}. Email: {u.Email}. PEC: {u.Pec}");
            b.Add("Responsabile", u.Responsabile);
            b.Add("Appuntamenti", u.Prenotabile
                ? "L'ufficio si puo' prenotare dalla pagina /prenota-appuntamento del portale."
                : "L'ufficio non e' prenotabile online.");
            return b.Build($"{u.Tipo}: {u.Nome}", "Struttura organizzativa del Comune di Paperopoli (CMS)");
        }).ToList();
    }

    private async Task<List<CorpusFeedWork>> Amministratori(CancellationToken ct)
    {
        var persone = await _db.Persone.AsNoTracking().Where(p => p.IsPublished)
            .OrderBy(p => p.Ordine).ToListAsync(ct);

        return persone.Select(p =>
        {
            var b = new PassageBuilder($"persona:{p.Slug}", p.UpdatedAt);
            b.Add("Carica", Coalesce(p.Carica, p.Ruolo.ToString()));
            b.Add("Deleghe", Join(p.Deleghe));
            b.Add("Biografia", p.Biografia);
            b.Add("Ricevimento", p.Ricevimento);
            b.Add("Contatti", $"Email: {p.Email}. Telefono: {p.Telefono}");
            return b.Build($"Amministrazione: {p.Nome}", "Amministrazione del Comune di Paperopoli (CMS)");
        }).ToList();
    }

    private async Task<List<CorpusFeedWork>> NovitaWorks(CancellationToken ct)
    {
        var novita = await _db.Novita.AsNoTracking().Include(n => n.ACuraDi)
            .Where(n => n.IsPublished)
            .OrderByDescending(n => n.Data).ToListAsync(ct);

        return novita.Select(n =>
        {
            var b = new PassageBuilder($"novita:{n.Slug}", n.UpdatedAt);
            b.Add("Tipo e data", $"{n.Tipo}, pubblicata il {n.Data:dd/MM/yyyy}");
            b.Add("Sommario", n.Sommario);
            b.Add("Testo", Html(n.Corpo));
            b.Add("A cura di", n.ACuraDi?.Nome);
            return b.Build($"{n.Tipo}: {n.Titolo}", "Novita' del Comune di Paperopoli (CMS)");
        }).ToList();
    }

    private async Task<List<CorpusFeedWork>> Eventi(CancellationToken ct)
    {
        var eventi = await _db.Eventi.AsNoTracking().Where(e => e.IsPublished)
            .OrderBy(e => e.Ordine).ToListAsync(ct);

        return eventi.Select(e =>
        {
            var b = new PassageBuilder($"evento:{e.Slug}", e.UpdatedAt);
            b.Add("Sommario", e.Sommario);
            b.Add("Descrizione", Html(e.Descrizione));
            b.Add("Quando", Periodo(e));
            b.Add("Dove", e.LuogoTesto);
            b.Add("Costo", e.Costo);
            b.Add("Contatti", e.Contatti);
            return b.Build($"Evento: {e.Titolo}", "Eventi del Comune di Paperopoli (CMS)");
        }).ToList();
    }

    private async Task<List<CorpusFeedWork>> Luoghi(CancellationToken ct)
    {
        var luoghi = await _db.Luoghi.AsNoTracking().Where(l => l.IsPublished)
            .OrderBy(l => l.Ordine).ToListAsync(ct);

        return luoghi.Select(l =>
        {
            var b = new PassageBuilder($"luogo:{l.Slug}", l.UpdatedAt);
            b.Add("Categoria", l.Categoria);
            b.Add("Descrizione", Html(l.Descrizione));
            b.Add("Indirizzo", l.Indirizzo);
            b.Add("Modalita' di accesso", l.ModalitaAccesso);
            b.Add("Orari", l.Orari);
            b.Add("Contatti", l.Contatti);
            return b.Build($"Luogo: {l.Nome}", "Luoghi del territorio di Paperopoli (CMS)");
        }).ToList();
    }

    private async Task<List<CorpusFeedWork>> Documenti(CancellationToken ct)
    {
        var documenti = await _db.Documenti.AsNoTracking().Include(d => d.UfficioResponsabile)
            .Where(d => d.IsPublished)
            .OrderByDescending(d => d.Data).ToListAsync(ct);

        return documenti.Select(d =>
        {
            var b = new PassageBuilder($"documento:{d.Slug}", d.UpdatedAt);
            b.Add("Tipo e data", $"{d.Tipo}, del {d.Data:dd/MM/yyyy}");
            b.Add("Descrizione", Html(d.Descrizione));
            b.Add("Ufficio responsabile", d.UfficioResponsabile?.Nome);
            b.Add("File", d.UrlFile);
            return b.Build($"{d.Tipo}: {d.Titolo}", "Documenti e dati del Comune di Paperopoli (CMS)");
        }).ToList();
    }

    private async Task<List<CorpusFeedWork>> Pagine(CancellationToken ct)
    {
        var pagine = await _db.Pagine.AsNoTracking().Where(p => p.IsPublished)
            .OrderBy(p => p.Ordine).ToListAsync(ct);

        return pagine.Select(p =>
        {
            var b = new PassageBuilder($"pagina:{p.Slug}", p.UpdatedAt);
            b.Add("Sottotitolo", p.Sottotitolo);
            b.Add("Testo", Html(p.Corpo));
            return b.Build($"Pagina: {p.Titolo}", "Pagine del portale del Comune di Paperopoli (CMS)");
        }).ToList();
    }

    /// <summary>Tutte le FAQ in un unico work: si consultano come un blocco solo.</summary>
    private async Task<CorpusFeedWork?> FaqWork(CancellationToken ct)
    {
        var faq = await _db.Faq.AsNoTracking().Where(f => f.IsPublished)
            .OrderBy(f => f.Ordine).ToListAsync(ct);
        if (faq.Count == 0) return null;

        var b = new PassageBuilder("faq", faq.Max(f => f.UpdatedAt));
        foreach (var f in faq)
            b.Add($"Domanda frequente ({f.Categoria})", $"{f.Domanda} {f.Risposta}");

        return b.Build("Domande frequenti", "FAQ del Comune di Paperopoli (CMS)");
    }

    /// <summary>Dati dell'ente presi dalle impostazioni del sito.</summary>
    private static CorpusFeedWork? Ente(IReadOnlyDictionary<string, string> s)
    {
        if (s.Count == 0) return null;
        string V(string k) => s.TryGetValue(k, out var v) ? v : "";

        var b = new PassageBuilder("ente", DateTime.UtcNow);
        b.Add("Sede", $"{V("ente.indirizzo")} {V("ente.quartiere")}".Trim());
        b.Add("Codice fiscale", V("ente.cf"));
        b.Add("Contatti", $"PEC: {V("contatti.pec")}. URP: {V("contatti.urp")}. Telefono: {V("contatti.telefono")}");
        b.Add("Regione", V("ente.regione"));
        b.Add("Patrono", V("ente.patrono"));

        return b.Build(V("ente.nome") is { Length: > 0 } n ? n : "Comune di Paperopoli",
            "Dati dell'ente dalle impostazioni del sito (CMS)");
    }

    // ---- Utilita' ----

    private static string Periodo(Evento e)
    {
        var parti = new List<string>();
        if (e.DataInizio is { } da && e.DataFine is { } a && da.Date != a.Date)
            parti.Add($"dal {da:dd/MM/yyyy} al {a:dd/MM/yyyy}");
        else if (e.DataInizio is { } d)
            parti.Add($"il {d:dd/MM/yyyy}");
        if (!string.IsNullOrWhiteSpace(e.Orario)) parti.Add(e.Orario);
        if (!string.IsNullOrWhiteSpace(e.Ricorrenza)) parti.Add($"ricorrenza: {e.Ricorrenza}");
        return string.Join(", ", parti);
    }

    private static string Coalesce(params string?[] valori) =>
        valori.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static string Join(List<string>? voci) =>
        voci is null || voci.Count == 0 ? "" : string.Join("; ", voci.Where(v => !string.IsNullOrWhiteSpace(v)));

    /// <summary>I corpi HTML del CMS diventano testo semplice: il modello legge prosa, non markup.</summary>
    private static string Html(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var testo = Regex.Replace(html, "<(br|/p|/h[1-6]|/li|/div)[^>]*>", " ", RegexOptions.IgnoreCase);
        testo = Regex.Replace(testo, "<[^>]+>", "");
        return WebUtility.HtmlDecode(testo);
    }

    /// <summary>
    /// Accumula i passaggi di un work numerandoli e calcolandone hash e versione.
    /// Salta i campi vuoti: un passaggio esiste solo se ha davvero un contenuto.
    /// </summary>
    private sealed class PassageBuilder
    {
        private readonly string _workId;
        private readonly string _version;
        private readonly List<CorpusFeedPassage> _passages = new();

        public PassageBuilder(string workId, DateTime aggiornatoIl)
        {
            _workId = workId;
            var utc = aggiornatoIl == default ? DateTime.UtcNow : aggiornatoIl.ToUniversalTime();
            _version = utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        }

        public void Add(string etichetta, string? testo)
        {
            var pulito = Normalizza(testo);
            if (pulito.Length == 0) return;
            var completo = $"{etichetta}: {pulito}";
            var seq = _passages.Count + 1;
            _passages.Add(new CorpusFeedPassage(
                Id: $"{_workId}:p{seq:00}",
                Seq: seq,
                Version: _version,
                Hash: Hash(completo),
                Text: completo));
        }

        public CorpusFeedWork Build(string titolo, string fonte) =>
            new(Id: _workId,
                Title: titolo,
                Source: fonte,
                License: "CC BY 4.0 - contenuto dimostrativo fittizio",
                PassageCount: _passages.Count,
                Passages: _passages);

        private static string Normalizza(string? testo) =>
            string.IsNullOrWhiteSpace(testo) ? "" : Regex.Replace(testo, @"\s+", " ").Trim();

        private static string Hash(string testo) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testo))).ToLowerInvariant();
    }
}

// ---- Formato di scambio: le stesse proprieta' di corpus.json ----

public sealed record CorpusFeedDocument(
    [property: JsonPropertyName("corpus_version")] string CorpusVersion,
    [property: JsonPropertyName("generated_at")] string GeneratedAt,
    [property: JsonPropertyName("disclaimer")] string Disclaimer,
    [property: JsonPropertyName("principle")] string Principle,
    [property: JsonPropertyName("works")] IReadOnlyList<CorpusFeedWork> Works);

public sealed record CorpusFeedWork(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("license")] string License,
    [property: JsonPropertyName("passage_count")] int PassageCount,
    [property: JsonPropertyName("passages")] IReadOnlyList<CorpusFeedPassage> Passages);

public sealed record CorpusFeedPassage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("seq")] int Seq,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("text")] string Text);
