using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class NovitaTipoModel : PageModel
{
    private readonly ContentService _cms;

    public NovitaTipoModel(ContentService cms) => _cms = cms;

    public string Titolo { get; private set; } = "";
    public List<Novita> Novita { get; private set; } = new();

    private static readonly Dictionary<string, (TipoNovita Tipo, string Titolo)> Rotte = new()
    {
        ["notizie"] = (TipoNovita.Notizia, "Notizie"),
        ["comunicati"] = (TipoNovita.Comunicato, "Comunicati"),
        ["avvisi"] = (TipoNovita.Avviso, "Avvisi"),
    };

    public async Task<IActionResult> OnGetAsync(string tipo)
    {
        if (!Rotte.TryGetValue(tipo.ToLowerInvariant(), out var r)) return NotFound();
        Titolo = r.Titolo;
        Novita = await _cms.NovitaPerTipo(r.Tipo);
        return Page();
    }
}
