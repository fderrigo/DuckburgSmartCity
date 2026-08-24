using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class FaqModel : PageModel
{
    private readonly ContentService _cms;

    public FaqModel(ContentService cms) => _cms = cms;

    public List<IGrouping<string, FaqItem>> Gruppi { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var faq = await _cms.Faq();
        Gruppi = faq.GroupBy(f => f.Categoria).ToList();
    }
}
