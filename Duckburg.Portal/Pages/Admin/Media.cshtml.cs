using Duckburg.Portal.Cms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Duckburg.Portal.Pages.Admin;

public class MediaModel : PageModel
{
    private readonly CmsDbContext _db;
    private readonly IWebHostEnvironment _env;

    public MediaModel(CmsDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public List<MediaFile> Files { get; private set; } = new();

    /// <summary>Modalità picker: la pagina è aperta in un popup per scegliere un file.</summary>
    [FromQuery] public bool Picker { get; set; }

    private static readonly Dictionary<string, string> EstensioniAmmesse = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".svg"] = "image/svg+xml",
        [".pdf"] = "application/pdf",
    };

    public async Task OnGetAsync() =>
        Files = await _db.Media.AsNoTracking().OrderByDescending(m => m.CreatedAt).ToListAsync();

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file, string? alt)
    {
        if (file is { Length: > 0 and <= 10 * 1024 * 1024 })
        {
            var ext = Path.GetExtension(file.FileName);
            if (EstensioniAmmesse.TryGetValue(ext, out var contentType))
            {
                var dir = Path.Combine(_env.WebRootPath, "media");
                Directory.CreateDirectory(dir);
                var nomeBase = Path.GetFileNameWithoutExtension(file.FileName);
                nomeBase = string.Concat(nomeBase.ToLowerInvariant()
                    .Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
                var nome = $"{nomeBase}-{Guid.NewGuid().ToString("N")[..6]}{ext.ToLowerInvariant()}";
                await using (var fs = System.IO.File.Create(Path.Combine(dir, nome)))
                    await file.CopyToAsync(fs);

                _db.Media.Add(new MediaFile
                {
                    FileName = file.FileName,
                    Url = $"/media/{nome}",
                    ContentType = contentType,
                    Size = file.Length,
                    Alt = alt ?? "",
                    Slug = nome,
                });
                await _db.SaveChangesAsync();
                TempData["Flash"] = "File caricato nella libreria.";
            }
            else
            {
                TempData["FlashKo"] = "Formato non ammesso. Usa immagini (jpg, png, gif, webp, svg) o PDF.";
            }
        }
        else
        {
            TempData["FlashKo"] = "File mancante o troppo grande (max 10 MB).";
        }
        return RedirectToPage(new { picker = Picker ? "true" : null });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var m = await _db.Media.FindAsync(id);
        if (m != null)
        {
            var fisico = Path.Combine(_env.WebRootPath, m.Url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(fisico)) System.IO.File.Delete(fisico);
            _db.Media.Remove(m);
            await _db.SaveChangesAsync();
            TempData["Flash"] = "File eliminato.";
        }
        return RedirectToPage();
    }
}
