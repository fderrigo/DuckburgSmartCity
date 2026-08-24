using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class LuoghiModel : PageModel
{
    private readonly ContentService _cms;

    public LuoghiModel(ContentService cms) => _cms = cms;

    public List<Luogo> Luoghi { get; private set; } = new();

    public async Task OnGetAsync() => Luoghi = await _cms.Luoghi();
}
