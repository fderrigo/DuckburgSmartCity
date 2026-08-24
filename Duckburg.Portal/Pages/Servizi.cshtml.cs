using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class ServiziModel : PageModel
{
    private readonly ContentService _cms;

    public ServiziModel(ContentService cms) => _cms = cms;

    public List<CategoriaServizio> Categorie { get; private set; } = new();
    public List<Servizio> Servizi { get; private set; } = new();
    public HashSet<int> CategorieConServizi { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Categorie = await _cms.Categorie();
        Servizi = await _cms.TuttiIServizi();
        CategorieConServizi = Servizi.Where(s => s.CategoriaId.HasValue)
            .Select(s => s.CategoriaId!.Value).ToHashSet();
    }
}
