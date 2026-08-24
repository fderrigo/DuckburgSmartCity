using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Duckburg.Portal.Cms;

/// <summary>
/// Accesso ai contenuti per le pagine pubbliche e per l'area di amministrazione.
/// Centralizza anche la regola di protezione dei contenuti di default.
/// </summary>
public sealed class ContentService
{
    private readonly CmsDbContext _db;
    private readonly CmsOptions _opts;

    public ContentService(CmsDbContext db, IOptions<CmsOptions> opts)
    {
        _db = db;
        _opts = opts.Value;
    }

    public bool ProtectDefaultContent => _opts.ProtectDefaultContent;

    /// <summary>Un contenuto è bloccato se è di default e la protezione è attiva.</summary>
    public bool IsLocked(CmsEntity e) => _opts.ProtectDefaultContent && e.IsDefault;

    // ---- Servizi ----

    public Task<List<Servizio>> ServiziInEvidenza(int take = 6) =>
        _db.Servizi.AsNoTracking().Where(s => s.IsPublished && s.InEvidenza)
            .OrderBy(s => s.Ordine).Take(take).ToListAsync();

    public Task<List<Servizio>> TuttiIServizi() =>
        _db.Servizi.AsNoTracking().Include(s => s.Argomento).Include(s => s.Categoria)
            .Where(s => s.IsPublished)
            .OrderBy(s => s.Ordine).ThenBy(s => s.Titolo).ToListAsync();

    public Task<Servizio?> ServizioBySlug(string slug) =>
        _db.Servizi.AsNoTracking()
            .Include(s => s.UnitaOrganizzativa).Include(s => s.Argomento).Include(s => s.Categoria)
            .FirstOrDefaultAsync(s => s.Slug == slug && s.IsPublished);

    public Task<List<CategoriaServizio>> Categorie() =>
        _db.CategorieServizio.AsNoTracking().Where(c => c.IsPublished)
            .OrderBy(c => c.Ordine).ToListAsync();

    public Task<CategoriaServizio?> CategoriaBySlug(string slug) =>
        _db.CategorieServizio.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug && c.IsPublished);

    public Task<List<Servizio>> ServiziPerCategoria(int categoriaId) =>
        _db.Servizi.AsNoTracking().Include(s => s.Argomento)
            .Where(s => s.IsPublished && s.CategoriaId == categoriaId)
            .OrderBy(s => s.Ordine).ToListAsync();

    // ---- Argomenti ----

    public Task<List<Argomento>> Argomenti() =>
        _db.Argomenti.AsNoTracking().Where(a => a.IsPublished)
            .OrderBy(a => a.Ordine).ToListAsync();

    public Task<Argomento?> ArgomentoBySlug(string slug) =>
        _db.Argomenti.AsNoTracking().FirstOrDefaultAsync(a => a.Slug == slug && a.IsPublished);

    public Task<List<Servizio>> ServiziPerArgomento(int argomentoId) =>
        _db.Servizi.AsNoTracking().Where(s => s.IsPublished && s.ArgomentoId == argomentoId)
            .OrderBy(s => s.Ordine).ToListAsync();

    public Task<List<Novita>> NovitaPerArgomento(int argomentoId, int take = 6) =>
        _db.Novita.AsNoTracking().Where(n => n.IsPublished && n.ArgomentoId == argomentoId)
            .OrderByDescending(n => n.Data).Take(take).ToListAsync();

    // ---- Novità ----

    public Task<List<Novita>> UltimeNovita(int take = 4) =>
        _db.Novita.AsNoTracking().Where(n => n.IsPublished)
            .OrderByDescending(n => n.Data).Take(take).ToListAsync();

    public Task<List<Novita>> NovitaPerTipo(TipoNovita tipo, int take = 50) =>
        _db.Novita.AsNoTracking().Where(n => n.IsPublished && n.Tipo == tipo)
            .OrderByDescending(n => n.Data).Take(take).ToListAsync();

    public Task<Novita?> NovitaBySlug(string slug) =>
        _db.Novita.AsNoTracking().Include(n => n.Argomento).Include(n => n.ACuraDi)
            .FirstOrDefaultAsync(n => n.Slug == slug && n.IsPublished);

    // ---- Amministrazione ----

    public Task<List<Persona>> PersonePerRuolo(RuoloPersona ruolo) =>
        _db.Persone.AsNoTracking().Where(p => p.IsPublished && p.Ruolo == ruolo)
            .OrderBy(p => p.Ordine).ToListAsync();

    public Task<Persona?> PersonaBySlug(string slug) =>
        _db.Persone.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished);

    public Task<List<Persona>> Giunta() =>
        _db.Persone.AsNoTracking()
            .Where(p => p.IsPublished && (p.Ruolo == RuoloPersona.Vicesindaco || p.Ruolo == RuoloPersona.Assessore))
            .OrderBy(p => p.Ordine).ToListAsync();

    public Task<List<UnitaOrganizzativa>> Uffici() =>
        _db.Unita.AsNoTracking().Where(u => u.IsPublished)
            .OrderBy(u => u.Ordine).ToListAsync();

    public Task<UnitaOrganizzativa?> UfficioBySlug(string slug) =>
        _db.Unita.AsNoTracking().FirstOrDefaultAsync(u => u.Slug == slug && u.IsPublished);

    public Task<List<UnitaOrganizzativa>> UfficiPrenotabili() =>
        _db.Unita.AsNoTracking().Where(u => u.IsPublished && u.Prenotabile)
            .OrderBy(u => u.Ordine).ToListAsync();

    public Task<List<Documento>> Documenti() =>
        _db.Documenti.AsNoTracking().Include(d => d.UfficioResponsabile)
            .Where(d => d.IsPublished)
            .OrderByDescending(d => d.Data).ToListAsync();

    public Task<Documento?> DocumentoBySlug(string slug) =>
        _db.Documenti.AsNoTracking().Include(d => d.UfficioResponsabile)
            .FirstOrDefaultAsync(d => d.Slug == slug && d.IsPublished);

    // ---- Vivere il Comune ----

    public Task<List<Luogo>> Luoghi() =>
        _db.Luoghi.AsNoTracking().Where(l => l.IsPublished)
            .OrderBy(l => l.Ordine).ToListAsync();

    public Task<Luogo?> LuogoBySlug(string slug) =>
        _db.Luoghi.AsNoTracking().FirstOrDefaultAsync(l => l.Slug == slug && l.IsPublished);

    public Task<List<Evento>> Eventi() =>
        _db.Eventi.AsNoTracking().Where(e => e.IsPublished)
            .OrderBy(e => e.Ordine).ToListAsync();

    public Task<Evento?> EventoBySlug(string slug) =>
        _db.Eventi.AsNoTracking().FirstOrDefaultAsync(e => e.Slug == slug && e.IsPublished);

    // ---- Pagine, menu, impostazioni, FAQ ----

    public Task<Pagina?> PaginaBySlug(string slug) =>
        _db.Pagine.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished);

    public Task<List<VoceMenu>> Menu(PosizioneMenu pos) =>
        _db.VociMenu.AsNoTracking().Where(v => v.IsPublished && v.Posizione == pos)
            .OrderBy(v => v.Ordine).ToListAsync();

    public Task<List<FaqItem>> Faq() =>
        _db.Faq.AsNoTracking().Where(f => f.IsPublished)
            .OrderBy(f => f.Ordine).ToListAsync();

    /// <summary>Dizionario chiave→valore delle impostazioni del sito.</summary>
    public async Task<IReadOnlyDictionary<string, string>> Settings()
    {
        var list = await _db.Impostazioni.AsNoTracking().ToListAsync();
        return list.ToDictionary(x => x.Chiave, x => x.Valore, StringComparer.OrdinalIgnoreCase);
    }

    // ---- Funzionalità: appuntamenti, segnalazioni, valutazioni ----

    /// <summary>Slot disponibili per un ufficio in una data (finestra 9:00–12:30, mezz'ora).</summary>
    public async Task<List<TimeOnly>> SlotDisponibili(int ufficioId, DateOnly data)
    {
        var occupati = await _db.Appuntamenti.AsNoTracking()
            .Where(a => a.UfficioId == ufficioId && a.Data == data && !a.Annullato)
            .Select(a => a.Ora).ToListAsync();
        var slots = new List<TimeOnly>();
        for (var t = new TimeOnly(9, 0); t < new TimeOnly(12, 30); t = t.AddMinutes(30))
            if (!occupati.Contains(t)) slots.Add(t);
        return slots;
    }

    public async Task<Appuntamento> PrenotaAppuntamento(Appuntamento a)
    {
        a.Codice = $"PAP-{Random.Shared.Next(100000, 999999)}";
        a.Slug = a.Codice.ToLowerInvariant();
        _db.Appuntamenti.Add(a);
        await _db.SaveChangesAsync();
        return a;
    }

    public async Task<Segnalazione> InviaSegnalazione(Segnalazione s)
    {
        s.Codice = $"SEG-{Random.Shared.Next(100000, 999999)}";
        s.Slug = s.Codice.ToLowerInvariant();
        _db.Segnalazioni.Add(s);
        await _db.SaveChangesAsync();
        return s;
    }

    public async Task SalvaValutazione(ValutazionePagina v)
    {
        v.Slug = Guid.NewGuid().ToString("N")[..12];
        _db.Valutazioni.Add(v);
        await _db.SaveChangesAsync();
    }
}
