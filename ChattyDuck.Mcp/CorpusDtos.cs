using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChattyDuck.Mcp;

/// <summary>
/// Una fonte citabile: la sezione di una scheda, con tutto cio' che serve a verificarla.
/// <para>
/// Copia lato assistente del contratto del server MCP. Il confine e' il protocollo, non
/// un riferimento a progetto: l'assistente consuma il corpus come lo consumerebbe un
/// client di terzi.
/// </para>
/// </summary>
public sealed record Fonte(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("scheda")] string Scheda,
    [property: JsonPropertyName("etichetta")] string? Etichetta,
    [property: JsonPropertyName("testo")] string Testo,
    [property: JsonPropertyName("versione")] string? Versione,
    [property: JsonPropertyName("hash")] string? Hash,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("valido")] bool Valido);

/// <summary>
/// Ricava le fonti citabili dal risultato di uno strumento MCP.
/// <para>
/// Gli strumenti sono tre e restituiscono forme diverse: <c>cerca</c> da' schede con
/// dentro le sezioni, <c>scheda</c> da' una scheda intera, <c>elenca</c> da' un elenco
/// di titoli senza testo. Solo i primi due producono qualcosa da citare: un elenco
/// serve a orientarsi, non a rispondere, e mostrarlo fra le fonti farebbe credere che
/// l'assistente abbia letto contenuti che non ha letto.
/// </para>
/// </summary>
public static class EstrattoreFonti
{
    public static IReadOnlyList<Fonte> Estrai(string? nomeStrumento, string testoRisultato)
    {
        if (string.IsNullOrWhiteSpace(testoRisultato)) return [];

        try
        {
            using var doc = JsonDocument.Parse(testoRisultato);
            var radice = doc.RootElement;

            return radice.ValueKind switch
            {
                // cerca: elenco di schede con le sezioni pertinenti
                JsonValueKind.Array => radice.EnumerateArray().SelectMany(DaScheda).ToList(),
                // scheda: una sola scheda, con tutte le sezioni
                JsonValueKind.Object => DaScheda(radice).ToList(),
                _ => [],
            };
        }
        catch (JsonException)
        {
            // Il risultato non era una forma nota: nessuna fonte da mostrare, e nessun
            // motivo di far fallire la risposta.
            return [];
        }
    }

    private static IEnumerable<Fonte> DaScheda(JsonElement scheda)
    {
        if (scheda.ValueKind != JsonValueKind.Object) yield break;
        if (!scheda.TryGetProperty("sezioni", out var sezioni) || sezioni.ValueKind != JsonValueKind.Array)
            yield break;

        var titolo = Stringa(scheda, "titolo") ?? Stringa(scheda, "id") ?? "";
        var url = Stringa(scheda, "url");
        var valido = !scheda.TryGetProperty("valido", out var v) || v.ValueKind != JsonValueKind.False;

        foreach (var s in sezioni.EnumerateArray())
        {
            var testo = Stringa(s, "testo");
            if (string.IsNullOrWhiteSpace(testo)) continue;
            yield return new Fonte(
                Stringa(s, "id") ?? "",
                titolo,
                Stringa(s, "etichetta") ?? Stringa(s, "chiave"),
                testo,
                Stringa(s, "versione"),
                Stringa(s, "hash"),
                url,
                valido);
        }
    }

    private static string? Stringa(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
