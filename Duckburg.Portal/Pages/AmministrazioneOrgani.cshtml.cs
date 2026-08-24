using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class AmministrazioneOrganiModel : PageModel
{
    private readonly ContentService _cms;

    public AmministrazioneOrganiModel(ContentService cms) => _cms = cms;

    public Persona? Sindaco { get; private set; }
    public List<Persona> Giunta { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Sindaco = (await _cms.PersonePerRuolo(RuoloPersona.Sindaco)).FirstOrDefault();
        Giunta = await _cms.Giunta();
    }
}
