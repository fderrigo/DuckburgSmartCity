using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class EventiModel : PageModel
{
    private readonly ContentService _cms;

    public EventiModel(ContentService cms) => _cms = cms;

    public List<Evento> Eventi { get; private set; } = new();

    public async Task OnGetAsync() => Eventi = await _cms.Eventi();
}
