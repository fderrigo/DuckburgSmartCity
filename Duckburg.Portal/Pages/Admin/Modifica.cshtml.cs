using Duckburg.Portal.Cms;
using Duckburg.Portal.Cms.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Duckburg.Portal.Pages.Admin;

public class ModificaModel : PageModel
{
    private readonly AdminService _admin;
    public ModificaModel(AdminService admin) => _admin = admin;

    public EntityDef Def { get; private set; } = null!;
    public int Id { get; private set; }
    public bool IsNew => Id <= 0;
    public bool Locked { get; private set; }
    public Dictionary<string, string> Valori { get; } = new();
    public Dictionary<string, List<(string Value, string Text)>> Opzioni { get; } = new();

    public async Task<IActionResult> OnGetAsync(string entity, int? id)
    {
        var def = AdminRegistry.Find(entity);
        if (def == null) return NotFound();
        Def = def;
        Id = id ?? 0;

        CmsEntity model;
        if (IsNew)
        {
            model = _admin.New(def);
        }
        else
        {
            var found = _admin.Get(def, Id);
            if (found == null) return NotFound();
            model = found;
            Locked = _admin.IsLocked(found);
        }

        foreach (var f in def.Campi)
        {
            Valori[f.Prop] = _admin.FieldValue(model, f);
            if (f.Kind == FieldKind.Select)
                Opzioni[f.Prop] = await _admin.Options(f);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string entity, int? id)
    {
        var def = AdminRegistry.Find(entity);
        if (def == null) return NotFound();

        var form = new Dictionary<string, string?>();
        foreach (var f in def.Campi)
        {
            if (f.Kind == FieldKind.Bool)
                form[f.Prop] = Request.Form[f.Prop].Count > 0 ? "true" : "false";
            else
                form[f.Prop] = Request.Form[f.Prop];
        }

        var (ok, msg, savedId) = await _admin.Save(def, id ?? 0, form);
        TempData[ok ? "Flash" : "FlashKo"] = msg;
        if (ok) return Redirect($"/admin/{entity}");

        // Ricarica per mostrare l'errore mantenendo la form.
        return await OnGetAsync(entity, id);
    }
}
