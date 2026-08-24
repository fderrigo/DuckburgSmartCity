using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class ServizioModel : PageModel
{
    private readonly ContentService _cms;

    public ServizioModel(ContentService cms) => _cms = cms;

    public Servizio? Servizio { get; private set; }

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        Servizio = await _cms.ServizioBySlug(slug);
        if (Servizio == null) return NotFound();
        return Page();
    }
}
