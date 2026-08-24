using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Duckburg.Portal.Pages.Admin;

public class SegnalazioniModel : PageModel
{
    private readonly CmsDbContext _db;
    public SegnalazioniModel(CmsDbContext db) => _db = db;

    public List<Segnalazione> Segnalazioni { get; private set; } = new();

    public async Task OnGetAsync() =>
        Segnalazioni = await _db.Segnalazioni.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt).ToListAsync();

    public async Task<IActionResult> OnPostStatoAsync(int id, StatoSegnalazione stato)
    {
        var s = await _db.Segnalazioni.FindAsync(id);
        if (s != null)
        {
            s.Stato = stato;
            await _db.SaveChangesAsync();
            TempData["Flash"] = $"Segnalazione {s.Codice}: stato aggiornato.";
        }
        return RedirectToPage();
    }
}
