using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class AmministrazioneDocumentiModel : PageModel
{
    private readonly ContentService _cms;

    public AmministrazioneDocumentiModel(ContentService cms) => _cms = cms;

    public List<Documento> Documenti { get; private set; } = new();

    public async Task OnGetAsync() => Documenti = await _cms.Documenti();
}
