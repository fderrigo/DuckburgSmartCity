using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class ArgomentoDettaglioModel : PageModel
{
    private readonly ContentService _cms;

    public ArgomentoDettaglioModel(ContentService cms) => _cms = cms;

    public Argomento Argomento { get; private set; } = null!;
    public List<Servizio> Servizi { get; private set; } = new();
    public List<Novita> Novita { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var a = await _cms.ArgomentoBySlug(slug);
        if (a == null) return NotFound();
        Argomento = a;
        Servizi = await _cms.ServiziPerArgomento(a.Id);
        Novita = await _cms.NovitaPerArgomento(a.Id);
        return Page();
    }
}
