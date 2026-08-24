using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Options;

namespace Duckburg.Portal.Cms.Admin;

/// <summary>CRUD generico e reflection-based sui contenuti, con protezione dei default.</summary>
public sealed class AdminService
{
    private readonly CmsDbContext _db;
    private readonly CmsOptions _opts;

    public AdminService(CmsDbContext db, IOptions<CmsOptions> opts)
    {
        _db = db;
        _opts = opts.Value;
    }

    public bool ProtectDefaultContent => _opts.ProtectDefaultContent;

    private IQueryable<CmsEntity> Query(Type t)
    {
        var prop = typeof(CmsDbContext).GetProperties()
            .First(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericArguments()[0] == t);
        return ((IQueryable)prop.GetValue(_db)!).Cast<CmsEntity>();
    }

    public List<AdminRow> List(EntityDef def)
    {
        var items = Query(def.Type).OrderBy(e => e.Ordine).ToList();
        return items.Select(e => new AdminRow
        {
            Id = e.Id,
            IsDefault = e.IsDefault,
            Locked = _opts.ProtectDefaultContent && e.IsDefault,
            IsPublished = e.IsPublished,
            Valori = def.ColonneLista.Select(c => Display(e, c)).ToArray()
        }).ToList();
    }

    public Dictionary<string, int> Counts() =>
        AdminRegistry.All.ToDictionary(d => d.Key, d => Query(d.Type).Count());

    public CmsEntity? Get(EntityDef def, int id) =>
        _db.Find(def.Type, id) as CmsEntity;

    public CmsEntity New(EntityDef def) => (CmsEntity)Activator.CreateInstance(def.Type)!;

    public bool IsLocked(CmsEntity e) => _opts.ProtectDefaultContent && e.IsDefault;

    /// <summary>Valore stringa di un campo per il pre-riempimento del form.</summary>
    public string FieldValue(CmsEntity e, FieldDef f)
    {
        var pi = f.Prop is null ? null : e.GetType().GetProperty(f.Prop);
        var v = pi?.GetValue(e);
        return f.Kind switch
        {
            FieldKind.StringList => v is List<string> l ? string.Join("\n", l) : "",
            FieldKind.Bool => v is true ? "true" : "false",
            FieldKind.Date => v is DateTime d ? d.ToString("yyyy-MM-dd") : "",
            FieldKind.Select when f.EnumType != null => v?.ToString() ?? "",
            _ => v?.ToString() ?? ""
        };
    }

    private static string Display(CmsEntity e, string prop)
    {
        var v = e.GetType().GetProperty(prop)?.GetValue(e);
        return v switch
        {
            null => "",
            DateTime d => d.ToString("dd/MM/yyyy"),
            _ => v.ToString() ?? ""
        };
    }

    /// <summary>Crea o aggiorna un'entità dai valori del form. Restituisce (ok, messaggio).</summary>
    public async Task<(bool Ok, string Message, int Id)> Save(EntityDef def, int id, IReadOnlyDictionary<string, string?> form)
    {
        CmsEntity entity;
        bool isNew = id <= 0;
        if (isNew)
        {
            entity = New(def);
            entity.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            var existing = Get(def, id);
            if (existing == null) return (false, "Contenuto non trovato.", 0);
            if (IsLocked(existing))
                return (false, "Contenuto di default protetto: non modificabile.", id);
            entity = existing;
        }

        foreach (var f in def.Campi)
        {
            var pi = entity.GetType().GetProperty(f.Prop);
            if (pi == null || !pi.CanWrite) continue;
            form.TryGetValue(f.Prop, out var raw);
            SetValue(entity, pi, f, raw);
        }

        if (isNew) _db.Add(entity);
        await _db.SaveChangesAsync();
        return (true, isNew ? "Contenuto creato." : "Contenuto aggiornato.", entity.Id);
    }

    public async Task<(bool Ok, string Message)> Delete(EntityDef def, int id)
    {
        var e = Get(def, id);
        if (e == null) return (false, "Contenuto non trovato.");
        if (IsLocked(e)) return (false, "Contenuto di default protetto: non eliminabile.");
        _db.Remove(e);
        await _db.SaveChangesAsync();
        return (true, "Contenuto eliminato.");
    }

    private static void SetValue(CmsEntity entity, PropertyInfo pi, FieldDef f, string? raw)
    {
        var target = pi.PropertyType;
        var underlying = Nullable.GetUnderlyingType(target) ?? target;

        switch (f.Kind)
        {
            case FieldKind.Bool:
                pi.SetValue(entity, raw == "true" || raw == "on");
                return;
            case FieldKind.Int:
                if (int.TryParse(raw, out var n)) pi.SetValue(entity, n);
                else if (underlying == typeof(int)) pi.SetValue(entity, 0);
                return;
            case FieldKind.Date:
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
                    pi.SetValue(entity, target == typeof(DateTime?) ? d : d);
                else if (target == typeof(DateTime?)) pi.SetValue(entity, null);
                return;
            case FieldKind.StringList:
                var list = (raw ?? "").Replace("\r", "").Split('\n')
                    .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                pi.SetValue(entity, list);
                return;
            case FieldKind.Select when f.EnumType != null:
                if (!string.IsNullOrWhiteSpace(raw) && Enum.TryParse(f.EnumType, raw, out var ev))
                    pi.SetValue(entity, ev);
                return;
            case FieldKind.Select when underlying == typeof(int):
                // FK nullable/int
                if (int.TryParse(raw, out var fk)) pi.SetValue(entity, fk);
                else pi.SetValue(entity, target == typeof(int?) ? null : 0);
                return;
            default:
                pi.SetValue(entity, raw ?? "");
                return;
        }
    }

    /// <summary>Opzioni per un campo Select (enum o FK verso altra entità).</summary>
    public async Task<List<(string Value, string Text)>> Options(FieldDef f)
    {
        if (f.EnumType != null)
            return Enum.GetNames(f.EnumType).Select(n => (n, n)).ToList();
        if (f.Options != null)
            return await f.Options(_db);
        return new();
    }
}
