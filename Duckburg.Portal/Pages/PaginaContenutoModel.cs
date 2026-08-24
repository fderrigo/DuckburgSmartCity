using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

/// <summary>Base per le pagine di contenuto (legali, editoriali) caricate dal CMS via slug.</summary>
public abstract class PaginaContenutoModel : PageModel
{
    private readonly ContentService _cms;
    protected PaginaContenutoModel(ContentService cms) => _cms = cms;

    protected abstract string Slug { get; }

    public Pagina? Pagina { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Pagina = await _cms.PaginaBySlug(Slug);
        if (Pagina == null) return NotFound();
        return Page();
    }
}
