using Duckburg.Portal.Cms.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages.Admin;

public class ListaModel : PageModel
{
    private readonly AdminService _admin;
    public ListaModel(AdminService admin) => _admin = admin;

    public EntityDef Def { get; private set; } = null!;
    public List<AdminRow> Righe { get; private set; } = new();

    public IActionResult OnGet(string entity)
    {
        var def = AdminRegistry.Find(entity);
        if (def == null) return NotFound();
        Def = def;
        Righe = _admin.List(def);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string entity, int id)
    {
        var def = AdminRegistry.Find(entity);
        if (def == null) return NotFound();
        var (ok, msg) = await _admin.Delete(def, id);
        TempData[ok ? "Flash" : "FlashKo"] = msg;
        return RedirectToPage(new { entity });
    }
}
