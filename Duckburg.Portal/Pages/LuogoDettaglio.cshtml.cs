using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class LuogoDettaglioModel : PageModel
{
    private readonly ContentService _cms;

    public LuogoDettaglioModel(ContentService cms) => _cms = cms;

    public Luogo Luogo { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var l = await _cms.LuogoBySlug(slug);
        if (l == null) return NotFound();
        Luogo = l;
        return Page();
    }
}
