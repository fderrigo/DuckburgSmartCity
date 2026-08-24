using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class ArgomentiModel : PageModel
{
    private readonly ContentService _cms;

    public ArgomentiModel(ContentService cms) => _cms = cms;

    public List<Argomento> Argomenti { get; private set; } = new();

    public async Task OnGetAsync() => Argomenti = await _cms.Argomenti();
}
