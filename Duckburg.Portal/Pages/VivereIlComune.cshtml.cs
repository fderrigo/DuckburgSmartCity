using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class VivereIlComuneModel : PageModel
{
    private readonly ContentService _cms;

    public VivereIlComuneModel(ContentService cms) => _cms = cms;

    public List<Luogo> Luoghi { get; private set; } = new();
    public List<Evento> Eventi { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Luoghi = await _cms.Luoghi();
        Eventi = await _cms.Eventi();
    }
}
