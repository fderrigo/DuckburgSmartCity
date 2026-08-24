using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages;

public class SegnalazioneDisservizioModel : PageModel
{
    private readonly ContentService _cms;
    private readonly IWebHostEnvironment _env;

    public SegnalazioneDisservizioModel(ContentService cms, IWebHostEnvironment env)
    {
        _cms = cms;
        _env = env;
    }

    [BindProperty] public string Categoria { get; set; } = "";
    [BindProperty] public string Indirizzo { get; set; } = "";
    [BindProperty] public string Oggetto { get; set; } = "";
    [BindProperty] public string Descrizione { get; set; } = "";
    [BindProperty] public string Nome { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public List<IFormFile> Immagini { get; set; } = new();
    [BindProperty] public List<IFormFile> Documenti { get; set; } = new();

    public string? CodiceInviato { get; private set; }
    public string? Errore { get; private set; }

    public static readonly string[] CategorieDisponibili =
    {
        "Strade e marciapiedi", "Illuminazione pubblica", "Rifiuti e pulizia",
        "Verde pubblico e parchi", "Segnaletica", "Acqua e fognature", "Altro"
    };

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Categoria) || string.IsNullOrWhiteSpace(Indirizzo) ||
            string.IsNullOrWhiteSpace(Oggetto) || string.IsNullOrWhiteSpace(Descrizione))
        {
            Errore = "Compila tutti i campi obbligatori: categoria, luogo, oggetto e descrizione.";
            return Page();
        }

        var allegati = new List<string>();
        var dir = Path.Combine(_env.WebRootPath, "media", "segnalazioni");
        Directory.CreateDirectory(dir);
        foreach (var file in Immagini.Concat(Documenti).Take(6))
        {
            if (file.Length is 0 or > 8 * 1024 * 1024) continue;
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".pdf" or ".doc" or ".docx")) continue;
            var nome = $"{Guid.NewGuid():N}{ext}";
            await using var fs = System.IO.File.Create(Path.Combine(dir, nome));
            await file.CopyToAsync(fs);
            allegati.Add($"/media/segnalazioni/{nome}");
        }

        var s = await _cms.InviaSegnalazione(new Segnalazione
        {
            Categoria = Categoria ?? "", Indirizzo = Indirizzo ?? "", Oggetto = Oggetto ?? "",
            Descrizione = Descrizione ?? "", Nome = Nome ?? "", Email = Email ?? "", Allegati = allegati
        });
        CodiceInviato = s.Codice;
        return Page();
    }
}
