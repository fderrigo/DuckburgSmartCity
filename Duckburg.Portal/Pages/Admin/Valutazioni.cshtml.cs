using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Duckburg.Portal.Pages.Admin;

public class ValutazioniModel : PageModel
{
    private readonly CmsDbContext _db;
    public ValutazioniModel(CmsDbContext db) => _db = db;

    public sealed record RigaPagina(string Url, string Titolo, int Quante, double Media, int Ultime);

    public List<RigaPagina> PerPagina { get; private set; } = new();
    public List<ValutazionePagina> Ultime { get; private set; } = new();
    public int Totale { get; private set; }
    public double MediaComplessiva { get; private set; }

    public async Task OnGetAsync()
    {
        var tutte = await _db.Valutazioni.AsNoTracking().OrderByDescending(v => v.CreatedAt).ToListAsync();
        Totale = tutte.Count;
        MediaComplessiva = tutte.Count > 0 ? tutte.Average(v => v.Voto) : 0;
        PerPagina = tutte.GroupBy(v => v.Url)
            .Select(g => new RigaPagina(
                g.Key,
                g.First().TitoloPagina,
                g.Count(),
                g.Average(v => v.Voto),
                g.Count(v => v.CreatedAt > DateTime.UtcNow.AddDays(-7))))
            .OrderBy(r => r.Media)
            .ToList();
        Ultime = tutte.Take(20).ToList();
    }
}
