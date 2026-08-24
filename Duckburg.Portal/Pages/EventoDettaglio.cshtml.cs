using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class EventoDettaglioModel : PageModel
{
    private readonly ContentService _cms;

    public EventoDettaglioModel(ContentService cms) => _cms = cms;

    public Evento Evento { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var e = await _cms.EventoBySlug(slug);
        if (e == null) return NotFound();
        Evento = e;
        return Page();
    }
}
