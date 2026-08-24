using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class AmministrazioneSindacoModel : PageModel
{
    private readonly ContentService _cms;

    public AmministrazioneSindacoModel(ContentService cms) => _cms = cms;

    public Persona? Sindaco { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Sindaco = (await _cms.PersonePerRuolo(RuoloPersona.Sindaco)).FirstOrDefault();
        if (Sindaco == null) return NotFound();
        return Page();
    }
}
