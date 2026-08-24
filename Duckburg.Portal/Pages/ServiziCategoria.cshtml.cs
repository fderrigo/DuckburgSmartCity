using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class ServiziCategoriaModel : PageModel
{
    private readonly ContentService _cms;

    public ServiziCategoriaModel(ContentService cms) => _cms = cms;

    public CategoriaServizio Categoria { get; private set; } = null!;
    public List<Servizio> Servizi { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var c = await _cms.CategoriaBySlug(slug);
        if (c == null) return NotFound();
        Categoria = c;
        Servizi = await _cms.ServiziPerCategoria(c.Id);
        return Page();
    }
}
