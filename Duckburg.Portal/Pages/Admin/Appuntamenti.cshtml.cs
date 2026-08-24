using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Duckburg.Portal.Pages.Admin;

public class AppuntamentiModel : PageModel
{
    private readonly CmsDbContext _db;
    public AppuntamentiModel(CmsDbContext db) => _db = db;

    public List<Appuntamento> Appuntamenti { get; private set; } = new();

    public async Task OnGetAsync() =>
        Appuntamenti = await _db.Appuntamenti.AsNoTracking().Include(a => a.Ufficio)
            .OrderByDescending(a => a.Data).ThenBy(a => a.Ora).ToListAsync();

    public async Task<IActionResult> OnPostAnnullaAsync(int id)
    {
        var a = await _db.Appuntamenti.FindAsync(id);
        if (a != null) { a.Annullato = true; await _db.SaveChangesAsync(); TempData["Flash"] = $"Appuntamento {a.Codice} annullato."; }
        return RedirectToPage();
    }
}
