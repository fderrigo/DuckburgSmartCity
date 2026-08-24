using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class UfficioDettaglioModel : PageModel
{
    private readonly ContentService _cms;

    public UfficioDettaglioModel(ContentService cms) => _cms = cms;

    public UnitaOrganizzativa Ufficio { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var u = await _cms.UfficioBySlug(slug);
        if (u == null) return NotFound();
        Ufficio = u;
        return Page();
    }
}
