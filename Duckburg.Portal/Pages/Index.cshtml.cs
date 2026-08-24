using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class IndexModel : PageModel
{
    private readonly ContentService _cms;

    public IndexModel(ContentService cms) => _cms = cms;

    public IReadOnlyDictionary<string, string> Settings { get; private set; } = new Dictionary<string, string>();
    public List<Argomento> Argomenti { get; private set; } = new();
    public List<Servizio> ServiziInEvidenza { get; private set; } = new();
    public List<Novita> Novita { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Settings = await _cms.Settings();
        Argomenti = await _cms.Argomenti();
        ServiziInEvidenza = await _cms.ServiziInEvidenza(6);
        Novita = await _cms.UltimeNovita(3);
    }

    public string Get(string key, string fallback = "") =>
        Settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
}
