using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class AmministrazioneUfficiModel : PageModel
{
    private readonly ContentService _cms;

    public AmministrazioneUfficiModel(ContentService cms) => _cms = cms;

    public List<UnitaOrganizzativa> Uffici { get; private set; } = new();

    public async Task OnGetAsync() => Uffici = await _cms.Uffici();
}
