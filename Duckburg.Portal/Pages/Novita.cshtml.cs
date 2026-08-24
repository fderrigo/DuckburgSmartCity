using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class NovitaModel : PageModel
{
    private readonly ContentService _cms;

    public NovitaModel(ContentService cms) => _cms = cms;

    public List<Novita> Novita { get; private set; } = new();

    public async Task OnGetAsync() => Novita = await _cms.UltimeNovita(50);
}
