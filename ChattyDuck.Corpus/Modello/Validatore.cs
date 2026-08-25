using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ChattyDuck.Corpus.Modello;

public sealed record Problema(string Percorso, string Messaggio, bool Bloccante);

public sealed record EsitoValidazione(IReadOnlyList<Problema> Problemi, Istantanea Normalizzata)
{
    public bool Valida => !Problemi.Any(p => p.Bloccante);
    public IEnumerable<Problema> Errori => Problemi.Where(p => p.Bloccante);
    public IEnumerable<Problema> Avvisi => Problemi.Where(p => !p.Bloccante);
}

/// <summary>
/// Controlla un'istantanea prima di accettarla, e la normalizza.
/// <para>
/// Gli adattatori li scrivono altri, in linguaggi diversi, spesso senza poter provare
/// contro un corpus vero. Il controllo sta quindi qui, dove e' uno solo: quello che
/// entra e' gia' coerente, e chi legge non deve difendersi da dati storti.
/// </para>
/// <para>
/// Distinzione voluta fra errori e avvisi. Bloccante e' cio' che rende il corpus
/// inutilizzabile o non verificabile: identificatori duplicati, impronte che non
/// corrispondono, tipi sconosciuti nei campi strutturali. Avviso e' cio' che riduce la
/// qualita' senza compromettere l'uso: una relazione che punta a un contenuto assente,
/// una chiave fuori vocabolario. Rifiutare un'istantanea intera per un avviso
/// significherebbe lasciare un ente senza assistente per un dettaglio.
/// </para>
/// </summary>
public static class Validatore
{
    private static readonly Regex IdContenuto = new(@"^[a-z0-9-]+:[a-z0-9._-]+$", RegexOptions.Compiled);
    private static readonly Regex IdEnte = new(@"^[a-z0-9-]{2,64}$", RegexOptions.Compiled);

    public static EsitoValidazione Valida(Istantanea istantanea)
    {
        var problemi = new List<Problema>();

        if (istantanea.Modello != Vocabolario.VersioneModello)
            problemi.Add(new("modello",
                $"Versione del modello '{istantanea.Modello}' non gestita: attesa '{Vocabolario.VersioneModello}'.", true));

        if (!IdEnte.IsMatch(istantanea.Ente.Id))
            problemi.Add(new("ente.id",
                $"Identificatore dell'ente non valido: '{istantanea.Ente.Id}'. Minuscole, cifre e trattini.", true));

        if (istantanea.Contenuti.Count == 0)
            problemi.Add(new("contenuti",
                "Istantanea senza contenuti: sarebbe un corpus vuoto. Se e' voluto, cancella l'ente.", true));

        var visti = new HashSet<string>(StringComparer.Ordinal);
        var idSezioni = new HashSet<string>(StringComparer.Ordinal);
        var contenutiNormalizzati = new List<Contenuto>(istantanea.Contenuti.Count);

        foreach (var (c, i) in istantanea.Contenuti.Select((c, i) => (c, i)))
        {
            var p = $"contenuti[{i}]";

            if (!IdContenuto.IsMatch(c.Id))
                problemi.Add(new($"{p}.id", $"Identificatore non valido: '{c.Id}'. Atteso 'tipo:slug'.", true));
            else if (!visti.Add(c.Id))
                problemi.Add(new($"{p}.id", $"Identificatore duplicato: '{c.Id}'.", true));
            else if (!c.Id.StartsWith(c.Tipo + ":", StringComparison.Ordinal))
                problemi.Add(new($"{p}.id",
                    $"L'identificatore '{c.Id}' non inizia con il proprio tipo '{c.Tipo}:'.", true));

            if (!Vocabolario.Tipi.Contains(c.Tipo))
                problemi.Add(new($"{p}.tipo",
                    $"Tipo '{c.Tipo}' fuori vocabolario: sara' conservato ma non filtrabile.", false));

            if (string.IsNullOrWhiteSpace(c.Titolo))
                problemi.Add(new($"{p}.titolo", "Titolo mancante.", true));

            if (c.Url is { Length: > 0 } url && !Uri.TryCreate(url, UriKind.Absolute, out _))
                problemi.Add(new($"{p}.url", $"URL non assoluta: '{url}'.", false));

            if (c.Validita is { Da: not null, A: not null } v && v.Da > v.A)
                problemi.Add(new($"{p}.validita", "Inizio della validita' successivo alla fine.", true));

            foreach (var (a, j) in c.Attributi.Select((a, j) => (a, j)))
            {
                if (!Vocabolario.TipiAttributo.Contains(a.Tipo))
                    problemi.Add(new($"{p}.attributi[{j}].tipo",
                        $"Tipo di attributo sconosciuto: '{a.Tipo}'. Ammessi: {string.Join(", ", Vocabolario.TipiAttributo)}.", true));
                if (string.IsNullOrWhiteSpace(a.Chiave))
                    problemi.Add(new($"{p}.attributi[{j}].chiave", "Chiave mancante.", true));
            }

            foreach (var (r, j) in c.Relazioni.Select((r, j) => (r, j)))
            {
                if (!Vocabolario.TipiRelazione.Contains(r.Tipo))
                    problemi.Add(new($"{p}.relazioni[{j}].tipo",
                        $"Tipo di relazione fuori vocabolario: '{r.Tipo}'.", false));
                if (!IdContenuto.IsMatch(r.Verso))
                    problemi.Add(new($"{p}.relazioni[{j}].verso",
                        $"Destinazione non valida: '{r.Verso}'.", true));
            }

            var sezioni = new List<Sezione>(c.Sezioni.Count);
            foreach (var (s, j) in c.Sezioni.Select((s, j) => (s, j)))
            {
                var ps = $"{p}.sezioni[{j}]";

                if (string.IsNullOrWhiteSpace(s.Testo))
                {
                    problemi.Add(new($"{ps}.testo", "Sezione senza testo: scartata.", false));
                    continue;
                }

                var atteso = string.IsNullOrWhiteSpace(s.Id) ? $"{c.Id}#{s.Chiave}" : s.Id!;
                if (!atteso.StartsWith(c.Id + "#", StringComparison.Ordinal))
                    problemi.Add(new($"{ps}.id",
                        $"L'identificatore della sezione '{atteso}' non appartiene al contenuto '{c.Id}'.", true));
                if (!idSezioni.Add(atteso))
                    problemi.Add(new($"{ps}.id", $"Identificatore di sezione duplicato: '{atteso}'.", true));

                // L'impronta si calcola qui se manca, e si verifica se dichiarata: e' la
                // proprieta' su cui poggia la verificabilita' di ogni risposta.
                var calcolato = Impronta(s.Testo);
                if (s.Hash is { Length: > 0 } dichiarato && !string.Equals(dichiarato, calcolato, StringComparison.OrdinalIgnoreCase))
                    problemi.Add(new($"{ps}.hash",
                        $"Impronta dichiarata non corrispondente al testo. Attesa {calcolato}, ricevuta {dichiarato}.", true));

                sezioni.Add(s with { Id = atteso, Hash = calcolato });
            }

            if (c.Provenienza is { } prov)
            {
                if (!Vocabolario.Metodi.Contains(prov.Metodo))
                    problemi.Add(new($"{p}.provenienza.metodo",
                        $"Metodo sconosciuto: '{prov.Metodo}'. Ammessi: {string.Join(", ", Vocabolario.Metodi)}.", true));
                if (prov.Metodo == Vocabolario.MetodoEstrazione && prov.Confidenza is null)
                    problemi.Add(new($"{p}.provenienza.confidenza",
                        "Un contenuto estratto automaticamente deve dichiarare la propria confidenza.", false));
                if (prov.Confidenza is { } conf && (conf < 0 || conf > 1))
                    problemi.Add(new($"{p}.provenienza.confidenza", "Confidenza fuori dall'intervallo 0-1.", true));
            }

            if (c.Attributi.Count == 0 && sezioni.Count == 0)
                problemi.Add(new(p, $"Il contenuto '{c.Id}' non porta ne' fatti ne' prosa.", false));

            contenutiNormalizzati.Add(c with { Sezioni = sezioni });
        }

        // Le relazioni si verificano alla fine, quando l'insieme degli id e' completo.
        foreach (var (c, i) in contenutiNormalizzati.Select((c, i) => (c, i)))
            foreach (var (r, j) in c.Relazioni.Select((r, j) => (r, j)))
                if (IdContenuto.IsMatch(r.Verso) && !visti.Contains(r.Verso))
                    problemi.Add(new($"contenuti[{i}].relazioni[{j}].verso",
                        $"Relazione verso un contenuto assente dall'istantanea: '{r.Verso}'.", false));

        return new EsitoValidazione(problemi, istantanea with { Contenuti = contenutiNormalizzati });
    }

    public static string Impronta(string testo) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testo))).ToLowerInvariant();
}
