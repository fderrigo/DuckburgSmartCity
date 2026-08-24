using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class NovitaDettaglioModel : PageModel
{
    private readonly ContentService _cms;

    public NovitaDettaglioModel(ContentService cms) => _cms = cms;

    public Novita? Novita { get; private set; }

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        Novita = await _cms.NovitaBySlug(slug);
        if (Novita == null) return NotFound();
        return Page();
    }
}
