using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class DocumentoDettaglioModel : PageModel
{
    private readonly ContentService _cms;

    public DocumentoDettaglioModel(ContentService cms) => _cms = cms;

    public Documento Documento { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var d = await _cms.DocumentoBySlug(slug);
        if (d == null) return NotFound();
        Documento = d;
        return Page();
    }
}
