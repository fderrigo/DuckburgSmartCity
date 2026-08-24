using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class AmministrazioneGiuntaModel : PageModel
{
    private readonly ContentService _cms;

    public AmministrazioneGiuntaModel(ContentService cms) => _cms = cms;

    public List<Persona> Giunta { get; private set; } = new();

    public async Task OnGetAsync() => Giunta = await _cms.Giunta();
}
