using System.Net;
using System.Text.RegularExpressions;
using Duckburg.Ingestione.Contratto;
using Duckburg.Portal.Cms;
using Microsoft.EntityFrameworkCore;

namespace Duckburg.Ingestione.Mappatura;

/// <summary>
/// Traduce i contenuti del CMS di Paperopoli nel modello del corpus.
/// <para>
/// E' il pezzo che si riscrive per ogni CMS, ed e' l'unico che conosce le tabelle di
/// partenza. A valle nessuno sa piu' da dove i contenuti vengano: il corpus parla il
/// vocabolario del modello Comuni, che e' lo stesso per ogni ente.
/// </para>
/// <para>
/// La regola che ho seguito nel decidere cosa diventa <c>attributo</c> e cosa diventa
/// <c>sezione</c>: attributo se nel CMS il dato e' gia' strutturato (una data e' una
/// data, un booleano e' un booleano), sezione se nel CMS e' prosa libera. Non provo a
/// estrarre fatti dalla prosa a colpi di espressioni regolari: sarebbe indovinare, e un
/// corpus che si dichiara certificato non puo' contenere fatti indovinati. La ricchezza
/// del corpus resta cosi' limitata da quella della sorgente, e il modello lo rende
/// visibile invece di nasconderlo.
/// </para>
/// </summary>
public sealed class MappaturaDuckburgCms(CmsDbContext db, IConfiguration cfg, ILogger<MappaturaDuckburgCms> log)
{
    private const string Sistema = "duckburg-cms";

    private string BaseUrl => (cfg["Ingestione:UrlPortale"] ?? "http://localhost:5100").TrimEnd('/');

    public async Task<Istantanea> Costruisci(CancellationToken ct)
    {
        var impostazioni = await db.Impostazioni.AsNoTracking()
            .ToDictionaryAsync(i => i.Chiave, i => i.Valore, StringComparer.OrdinalIgnoreCase, ct);
        string Imp(string chiave, string ripiego) =>
            impostazioni.TryGetValue(chiave, out var v) && !string.IsNullOrWhiteSpace(v) ? v : ripiego;

        var contenuti = new List<Contenuto>();
        contenuti.AddRange(await Argomenti(ct));
        contenuti.AddRange(await Categorie(ct));
        contenuti.AddRange(await Uffici(ct));
        contenuti.AddRange(await Persone(ct));
        contenuti.AddRange(await Servizi(ct));
        contenuti.AddRange(await Novita(ct));
        contenuti.AddRange(await Eventi(ct));
        contenuti.AddRange(await Luoghi(ct));
        contenuti.AddRange(await Documenti(ct));
        contenuti.AddRange(await Pagine(ct));
        var faq = await Faq(ct);
        if (faq is not null) contenuti.Add(faq);
        contenuti.Add(Ente(impostazioni));

        log.LogInformation("Mappati {N} contenuti dal CMS", contenuti.Count);

        return new Istantanea
        {
            Ente = new Ente
            {
                Id = cfg["Ingestione:IdEnte"] ?? "comune-paperopoli",
                Nome = Imp("ente.nome", "Comune di Paperopoli"),
                Url = BaseUrl,
            },
            Sorgente = new Sorgente { Sistema = Sistema, Versione = "1.0" },
            Disclaimer = Imp("corpus.disclaimer",
                "Comune di Paperopoli: ente immaginario. Dati di esempio a fini dimostrativi, non hanno alcun valore reale."),
            Principio = Imp("corpus.principio",
                "Rispondi solo dai contenuti qui esposti, citando la sezione. Dati locali di Paperopoli, non regole nazionali generiche."),
            Contenuti = contenuti,
        };
    }

    // ------------------------------------------------------------------ servizi

    private async Task<List<Contenuto>> Servizi(CancellationToken ct)
    {
        var righe = await db.Servizi.AsNoTracking()
            .Include(s => s.UnitaOrganizzativa).Include(s => s.Categoria).Include(s => s.Argomento)
            .Where(s => s.IsPublished).OrderBy(s => s.Ordine).ToListAsync(ct);

        return righe.Select(s =>
        {
            var b = new Costruttore($"servizio:{s.Slug}", "servizio", s.Titolo, s.UpdatedAt);
            b.Url = $"{BaseUrl}/servizi/{s.Slug}";
            b.Sommario = Primo(s.DescrizioneBreve, s.Sottotitolo);

            // Fatti: nel CMS lo stato del servizio e le scadenze sono dati, non prosa.
            if (!s.Attivo) b.Attributo("stato", "Stato del servizio", "testo", "non attivo");
            if (s.Scadenze.Count > 0) b.Attributo("scadenza", "Scadenze", "elenco", s.Scadenze);
            if (!string.IsNullOrWhiteSpace(s.CondizioniServizioUrl))
                b.Attributo("condizioni-url", "Condizioni di servizio", "testo", s.CondizioniServizioUrl);

            // Prosa: le sezioni obbligatorie della scheda servizio del modello Comuni.
            b.Sezione("descrizione", "Descrizione", s.Descrizione);
            b.Sezione("a-chi-e-rivolto", "A chi e' rivolto", s.AChiERivolto);
            b.Sezione("come-fare", "Come fare", s.ComeFare);
            b.Sezione("cosa-serve", "Cosa serve", s.CosaServe);
            b.Sezione("cosa-si-ottiene", "Cosa si ottiene", s.CosaSiOttiene);
            b.Sezione("tempi-e-scadenze", "Tempi e scadenze", s.Tempi);
            b.Sezione("costi", "Costi", s.Costi);
            b.Sezione("condizioni-di-servizio", "Condizioni di servizio", s.CondizioniServizio);
            if (s.Fonti.Count > 0)
                b.Sezione("riferimenti-normativi", "Riferimenti normativi", string.Join("; ", s.Fonti));
            if (!s.Attivo && !string.IsNullOrWhiteSpace(s.MotivoStato))
                b.Sezione("casi-particolari", "Motivo dello stato", s.MotivoStato);

            if (s.UnitaOrganizzativa is { } u)
                b.Relazione("erogato-da", $"unita-organizzativa:{u.Slug}", u.Nome);
            if (s.Argomento is { } a) b.Relazione("argomento", $"argomento:{a.Slug}", a.Nome);
            if (s.Categoria is { } c) b.Relazione("categoria", $"categoria:{c.Slug}", c.Nome);

            return b.Fatto($"Servizio/{s.Id}", b.Url);
        }).ToList();
    }

    // ------------------------------------------------------------------- eventi

    private async Task<List<Contenuto>> Eventi(CancellationToken ct)
    {
        var righe = await db.Eventi.AsNoTracking().Where(e => e.IsPublished)
            .OrderBy(e => e.Ordine).ToListAsync(ct);

        return righe.Select(e =>
        {
            var b = new Costruttore($"evento:{e.Slug}", "evento", e.Titolo, e.UpdatedAt);
            b.Url = $"{BaseUrl}/vivere-il-comune/eventi/{e.Slug}";
            b.Sommario = e.Sommario;

            // Qui i fatti ci sono davvero: le date nel CMS sono date.
            if (e.DataInizio is { } di) b.Attributo("data-inizio", "Data di inizio", "data", di);
            if (e.DataFine is { } df) b.Attributo("data-fine", "Data di fine", "data", df);
            if (!string.IsNullOrWhiteSpace(e.Orario)) b.Attributo("orario", "Orario", "testo", e.Orario);
            if (!string.IsNullOrWhiteSpace(e.Ricorrenza)) b.Attributo("ricorrenza", "Ricorrenza", "testo", e.Ricorrenza);
            if (!string.IsNullOrWhiteSpace(e.Costo)) b.Attributo("costo", "Costo", "testo", e.Costo);

            // La validita' rende un evento passato riconoscibile come tale, invece di
            // farlo citare come se fosse in programma.
            if (e.DataInizio is not null || e.DataFine is not null)
                b.Validita = new Periodo { Da = ToOffset(e.DataInizio), A = ToOffset(e.DataFine) };

            b.Sezione("descrizione", "Descrizione", Testo(e.Descrizione));
            b.Sezione("dove", "Dove", e.LuogoTesto);
            b.Sezione("contatti", "Contatti", e.Contatti);

            return b.Fatto($"Evento/{e.Id}", b.Url);
        }).ToList();
    }

    // -------------------------------------------------------------------- altri

    private async Task<List<Contenuto>> Uffici(CancellationToken ct)
    {
        var righe = await db.Unita.AsNoTracking().Where(u => u.IsPublished)
            .OrderBy(u => u.Ordine).ToListAsync(ct);

        return righe.Select(u =>
        {
            var b = new Costruttore($"unita-organizzativa:{u.Slug}", "unita-organizzativa", u.Nome, u.UpdatedAt);
            b.Url = $"{BaseUrl}/amministrazione/uffici/{u.Slug}";
            b.Attributo("tipo-unita", "Tipo", "testo", u.Tipo.ToString());
            b.Attributo("prenotabile", "Prenotabile online", "booleano", u.Prenotabile);
            if (!string.IsNullOrWhiteSpace(u.Orari)) b.Attributo("orario", "Orari di apertura", "testo", u.Orari);
            if (!string.IsNullOrWhiteSpace(u.Sede)) b.Attributo("indirizzo", "Sede", "testo", u.Sede);
            if (Contatto(u.Telefono, u.Email, u.Pec) is { } contatto)
                b.Attributo("contatto", "Contatti", "contatto", contatto);
            if (u.Competenze.Count > 0) b.Attributo("competenze", "Competenze", "elenco", u.Competenze);

            b.Sezione("descrizione", "Descrizione", u.Descrizione);
            if (u.Competenze.Count > 0) b.Sezione("competenze", "Competenze", string.Join("; ", u.Competenze));
            b.Sezione("responsabile", "Responsabile", u.Responsabile);

            return b.Fatto($"UnitaOrganizzativa/{u.Id}", b.Url);
        }).ToList();
    }

    private async Task<List<Contenuto>> Persone(CancellationToken ct)
    {
        var righe = await db.Persone.AsNoTracking().Where(p => p.IsPublished)
            .OrderBy(p => p.Ordine).ToListAsync(ct);

        return righe.Select(p =>
        {
            var b = new Costruttore($"persona:{p.Slug}", "persona", p.Nome, p.UpdatedAt);
            b.Url = $"{BaseUrl}/amministrazione/{p.Slug}";
            b.Sommario = Primo(p.Carica, p.Ruolo.ToString());
            b.Attributo("carica", "Carica", "testo", Primo(p.Carica, p.Ruolo.ToString()) ?? "");
            if (p.Deleghe.Count > 0) b.Attributo("deleghe", "Deleghe", "elenco", p.Deleghe);
            if (Contatto(p.Telefono, p.Email, null) is { } contatto)
                b.Attributo("contatto", "Contatti", "contatto", contatto);

            b.Sezione("biografia", "Biografia", p.Biografia);
            b.Sezione("ricevimento", "Ricevimento", p.Ricevimento);

            return b.Fatto($"Persona/{p.Id}", b.Url);
        }).ToList();
    }

    private async Task<List<Contenuto>> Novita(CancellationToken ct)
    {
        var righe = await db.Novita.AsNoTracking().Include(n => n.ACuraDi).Include(n => n.Argomento)
            .Where(n => n.IsPublished).OrderByDescending(n => n.Data).ToListAsync(ct);

        return righe.Select(n =>
        {
            var b = new Costruttore($"novita:{n.Slug}", "novita", n.Titolo, n.UpdatedAt);
            b.Url = $"{BaseUrl}/novita/{n.Slug}";
            b.Sommario = n.Sommario;
            b.Attributo("data", "Data di pubblicazione", "data", n.Data);
            b.Attributo("tipo-novita", "Tipo", "testo", n.Tipo.ToString());

            b.Sezione("sommario", "In sintesi", n.Sommario);
            b.Sezione("testo", "Testo", Testo(n.Corpo));

            if (n.Argomento is { } a) b.Relazione("argomento", $"argomento:{a.Slug}", a.Nome);
            if (n.ACuraDi is { } u) b.Relazione("erogato-da", $"unita-organizzativa:{u.Slug}", u.Nome);

            return b.Fatto($"Novita/{n.Id}", b.Url);
        }).ToList();
    }

    private async Task<List<Contenuto>> Luoghi(CancellationToken ct)
    {
        var righe = await db.Luoghi.AsNoTracking().Where(l => l.IsPublished)
            .OrderBy(l => l.Ordine).ToListAsync(ct);

        return righe.Select(l =>
        {
            var b = new Costruttore($"luogo:{l.Slug}", "luogo", l.Nome, l.UpdatedAt);
            b.Url = $"{BaseUrl}/vivere-il-comune/luoghi/{l.Slug}";
            if (!string.IsNullOrWhiteSpace(l.Categoria)) b.Attributo("categoria-luogo", "Categoria", "testo", l.Categoria);
            if (!string.IsNullOrWhiteSpace(l.Indirizzo)) b.Attributo("indirizzo", "Indirizzo", "testo", l.Indirizzo);
            if (!string.IsNullOrWhiteSpace(l.Orari)) b.Attributo("orario", "Orari", "testo", l.Orari);

            b.Sezione("descrizione", "Descrizione", Testo(l.Descrizione));
            b.Sezione("modalita-di-accesso", "Modalita' di accesso", l.ModalitaAccesso);
            b.Sezione("contatti", "Contatti", l.Contatti);

            return b.Fatto($"Luogo/{l.Id}", b.Url);
        }).ToList();
    }

    private async Task<List<Contenuto>> Documenti(CancellationToken ct)
    {
        var righe = await db.Documenti.AsNoTracking().Include(d => d.UfficioResponsabile)
            .Where(d => d.IsPublished).OrderByDescending(d => d.Data).ToListAsync(ct);

        return righe.Select(d =>
        {
            var b = new Costruttore($"documento:{d.Slug}", "documento", d.Titolo, d.UpdatedAt);
            b.Url = $"{BaseUrl}/documenti/{d.Slug}";
            b.Attributo("data", "Data", "data", d.Data);
            b.Attributo("tipo-documento", "Tipo", "testo", d.Tipo.ToString());
            if (!string.IsNullOrWhiteSpace(d.UrlFile)) b.Attributo("file", "File", "testo", d.UrlFile);

            b.Sezione("descrizione", "Descrizione", Testo(d.Descrizione));

            if (d.UfficioResponsabile is { } u)
                b.Relazione("erogato-da", $"unita-organizzativa:{u.Slug}", u.Nome);

            return b.Fatto($"Documento/{d.Id}", b.Url);
        }).ToList();
    }

    private async Task<List<Contenuto>> Pagine(CancellationToken ct)
    {
        var righe = await db.Pagine.AsNoTracking().Where(p => p.IsPublished)
            .OrderBy(p => p.Ordine).ToListAsync(ct);

        return righe.Select(p =>
        {
            var b = new Costruttore($"pagina:{p.Slug}", "pagina", p.Titolo, p.UpdatedAt);
            b.Url = $"{BaseUrl}/{p.Slug}";
            b.Sommario = p.Sottotitolo;
            b.Sezione("testo", "Testo", Testo(p.Corpo));
            return b.Fatto($"Pagina/{p.Id}", b.Url);
        }).ToList();
    }

    private async Task<List<Contenuto>> Argomenti(CancellationToken ct) =>
        (await db.Argomenti.AsNoTracking().Where(a => a.IsPublished).OrderBy(a => a.Ordine).ToListAsync(ct))
        .Select(a =>
        {
            var b = new Costruttore($"argomento:{a.Slug}", "argomento", a.Nome, a.UpdatedAt);
            b.Url = $"{BaseUrl}/argomenti/{a.Slug}";
            b.Sezione("descrizione", "Descrizione", a.Descrizione);
            return b.Fatto($"Argomento/{a.Id}", b.Url);
        }).ToList();

    private async Task<List<Contenuto>> Categorie(CancellationToken ct) =>
        (await db.CategorieServizio.AsNoTracking().Where(c => c.IsPublished).OrderBy(c => c.Ordine).ToListAsync(ct))
        .Select(c =>
        {
            var b = new Costruttore($"categoria:{c.Slug}", "categoria", c.Nome, c.UpdatedAt);
            b.Url = $"{BaseUrl}/servizi/categoria/{c.Slug}";
            b.Sezione("descrizione", "Descrizione", c.Descrizione);
            return b.Fatto($"CategoriaServizio/{c.Id}", b.Url);
        }).ToList();

    /// <summary>Tutte le domande frequenti in un contenuto solo: si consultano come un blocco.</summary>
    private async Task<Contenuto?> Faq(CancellationToken ct)
    {
        var righe = await db.Faq.AsNoTracking().Where(f => f.IsPublished).OrderBy(f => f.Ordine).ToListAsync(ct);
        if (righe.Count == 0) return null;

        var b = new Costruttore("faq:domande-frequenti", "faq", "Domande frequenti", righe.Max(f => f.UpdatedAt));
        b.Url = $"{BaseUrl}/faq";
        foreach (var f in righe)
            b.Sezione(Chiave(f.Domanda), f.Domanda, f.Risposta);
        return b.Fatto("Faq", b.Url);
    }

    private Contenuto Ente(IReadOnlyDictionary<string, string> s)
    {
        string V(string k) => s.TryGetValue(k, out var v) ? v : "";
        var b = new Costruttore("ente:comune-paperopoli", "ente",
            V("ente.nome") is { Length: > 0 } n ? n : "Comune di Paperopoli", DateTime.UtcNow);
        b.Url = BaseUrl;
        if (V("ente.indirizzo") is { Length: > 0 } ind) b.Attributo("indirizzo", "Sede", "testo", ind);
        if (Contatto(V("contatti.telefono"), V("contatti.urp"), V("contatti.pec")) is { } c)
            b.Attributo("contatto", "Contatti", "contatto", c);
        b.Sezione("descrizione", "L'ente",
            $"{V("ente.nome")}. {V("ente.indirizzo")} {V("ente.quartiere")}. Codice fiscale {V("ente.cf")}.".Trim());
        return b.Fatto("Impostazioni", b.Url);
    }

    // ------------------------------------------------------------------ utilita'

    private static DateTimeOffset? ToOffset(DateTime? d) =>
        d is null ? null : new DateTimeOffset(DateTime.SpecifyKind(d.Value, DateTimeKind.Utc));

    private static object? Contatto(string? telefono, string? email, string? pec)
    {
        var c = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(telefono)) c["telefono"] = telefono;
        if (!string.IsNullOrWhiteSpace(email)) c["email"] = email;
        if (!string.IsNullOrWhiteSpace(pec)) c["pec"] = pec;
        return c.Count > 0 ? c : null;
    }

    private static string? Primo(params string?[] valori) =>
        valori.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>I corpi HTML del CMS diventano testo semplice: il corpus contiene prosa, non markup.</summary>
    private static string Testo(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var t = Regex.Replace(html, "<(br|/p|/h[1-6]|/li|/div)[^>]*>", " ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, "<[^>]+>", "");
        return Regex.Replace(WebUtility.HtmlDecode(t), @"\s+", " ").Trim();
    }

    private static string Chiave(string testo)
    {
        var s = Regex.Replace(testo.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return s.Length > 60 ? s[..60].TrimEnd('-') : s;
    }

    /// <summary>Accumula un contenuto, tenendo fuori i campi vuoti.</summary>
    private sealed class Costruttore(string id, string tipo, string titolo, DateTime aggiornatoIl)
    {
        private readonly List<Attributo> _attributi = [];
        private readonly List<Relazione> _relazioni = [];
        private readonly List<Sezione> _sezioni = [];
        private readonly string _versione =
            (aggiornatoIl == default ? DateTime.UtcNow : aggiornatoIl.ToUniversalTime()).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        public string? Url { get; set; }
        public string? Sommario { get; set; }
        public Periodo? Validita { get; set; }

        public void Attributo(string chiave, string etichetta, string tipoValore, object? valore)
        {
            if (valore is null) return;
            if (valore is string s && string.IsNullOrWhiteSpace(s)) return;
            _attributi.Add(new Attributo { Chiave = chiave, Etichetta = etichetta, Tipo = tipoValore, Valore = valore });
        }

        public void Sezione(string chiave, string etichetta, string? testo)
        {
            var pulito = string.IsNullOrWhiteSpace(testo) ? "" : Regex.Replace(testo, @"\s+", " ").Trim();
            if (pulito.Length == 0) return;
            _sezioni.Add(new Sezione
            {
                Chiave = chiave, Etichetta = etichetta, Ordine = _sezioni.Count + 1,
                Testo = pulito, Versione = _versione,
            });
        }

        public void Relazione(string tipoRelazione, string verso, string? etichetta = null) =>
            _relazioni.Add(new Relazione { Tipo = tipoRelazione, Verso = verso, Etichetta = etichetta });

        public Contenuto Fatto(string idSorgente, string? urlSorgente) => new()
        {
            Id = id, Tipo = tipo, Titolo = titolo, Sommario = Sommario, Url = Url,
            Validita = Validita,
            AggiornatoIl = aggiornatoIl == default
                ? DateTimeOffset.UtcNow
                : new DateTimeOffset(DateTime.SpecifyKind(aggiornatoIl, DateTimeKind.Utc)),
            Attributi = _attributi, Relazioni = _relazioni, Sezioni = _sezioni,
            Provenienza = new Provenienza
            {
                Sistema = Sistema, IdSorgente = idSorgente, UrlSorgente = urlSorgente,
                EstrattoIl = DateTimeOffset.UtcNow, Metodo = "mappatura",
            },
        };
    }
}
