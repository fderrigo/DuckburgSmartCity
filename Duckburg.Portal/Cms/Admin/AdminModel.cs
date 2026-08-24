using Microsoft.EntityFrameworkCore;

namespace Duckburg.Portal.Cms.Admin;

public enum FieldKind { Text, Multiline, Html, Bool, Int, Date, StringList, Select, Image }

/// <summary>Descrive un campo editabile di un'entità nell'area di amministrazione.</summary>
public sealed class FieldDef
{
    public required string Prop { get; init; }
    public required string Label { get; init; }
    public FieldKind Kind { get; init; } = FieldKind.Text;
    public string? Aiuto { get; init; }
    public Type? EnumType { get; init; }
    /// <summary>Sezione del form in cui raggruppare il campo (editor a sezioni).</summary>
    public string Sezione { get; init; } = "Contenuto";
    /// <summary>Se true il campo occupa mezza riga (layout a due colonne).</summary>
    public bool Mezza { get; init; }
    /// <summary>Fornitore di opzioni per i campi Select verso altre entità (valore, testo).</summary>
    public Func<CmsDbContext, Task<List<(string Value, string Text)>>>? Options { get; init; }
}

/// <summary>Descrive un tipo di contenuto gestibile dall'area di amministrazione.</summary>
public sealed class EntityDef
{
    public required string Key { get; init; }
    public required string Singolare { get; init; }
    public required string Plurale { get; init; }
    public required string Emoji { get; init; }
    public required Type Type { get; init; }
    public required string[] ColonneLista { get; init; }
    public required List<FieldDef> Campi { get; init; }
    public string Descrizione { get; init; } = "";
}

/// <summary>Riga di lista generica mostrata nelle tabelle dell'admin.</summary>
public sealed class AdminRow
{
    public required int Id { get; init; }
    public required string[] Valori { get; init; }
    public bool IsDefault { get; init; }
    public bool Locked { get; init; }
    public bool IsPublished { get; init; }
}
