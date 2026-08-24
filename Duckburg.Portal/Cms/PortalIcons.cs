using Microsoft.AspNetCore.Html;

namespace Duckburg.Portal.Cms;

/// <summary>Icone SVG inline in stile fumetto, indicizzate per chiave (Servizio.IconaKey).</summary>
public static class PortalIcons
{
    private const string Open = "<svg viewBox=\"0 0 32 32\" fill=\"none\" stroke=\"#17120E\" stroke-width=\"2.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\">";
    private const string Close = "</svg>";

    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tari"] = "<path d=\"M7 10 h18 l-2 16 h-14 Z\" fill=\"#fff\"/><path d=\"M5 10 h22 M12 10 v-3 h8 v3 M13 15 v7 M19 15 v7\"/>",
        ["acqua"] = "<path d=\"M16 5 l5 8\"/><path d=\"M25 12 l1 9 -6 -1\"/><path d=\"M8 24 l-2 -8 6 0\"/><path d=\"M10 27 h12\"/>",
        ["rossa"] = "<rect x=\"4\" y=\"8\" width=\"24\" height=\"16\" rx=\"2\" fill=\"#fff\"/><circle cx=\"11\" cy=\"15\" r=\"3\"/><path d=\"M7 21 q4 -3 8 0 M18 13 h7 M18 17 h7\"/>",
        ["casa"] = "<path d=\"M5 15 L16 5 l11 10\"/><path d=\"M8 14 v12 h16 v-12\" fill=\"#fff\"/><circle cx=\"16\" cy=\"20\" r=\"4\" fill=\"#FFD84D\"/><path d=\"M16 18 v4 M14.5 19 h3\"/>",
        ["bus"] = "<rect x=\"4\" y=\"7\" width=\"24\" height=\"15\" rx=\"3\" fill=\"#fff\"/><path d=\"M4 15 h24 M8 22 v3 M24 22 v3\"/><circle cx=\"9\" cy=\"19\" r=\"1\" fill=\"#17120E\"/><circle cx=\"23\" cy=\"19\" r=\"1\" fill=\"#17120E\"/>",
        ["pagamento"] = "<path d=\"M8 4 h16 v24 l-3 -2 -3 2 -2 -2 -3 2 -2 -2 -3 2 Z\" fill=\"#fff\"/><path d=\"M12 10 h8 M12 14 h8 M12 18 h5\"/>",
        ["orologio"] = "<circle cx=\"16\" cy=\"16\" r=\"11\" fill=\"#fff\"/><path d=\"M16 9 v7 l5 3\"/>",
        ["documento"] = "<path d=\"M9 4 h10 l5 5 v19 h-15 Z\" fill=\"#fff\"/><path d=\"M19 4 v5 h5 M12 15 h8 M12 19 h8 M12 23 h5\"/>",
    };

    public static IHtmlContent Render(string? key)
    {
        var body = key != null && Paths.TryGetValue(key, out var p) ? p : Paths["documento"];
        return new HtmlString(Open + body + Close);
    }
}
