using System.Text.Json;
using ChattyDuck.Corpus.Modello;
using Microsoft.EntityFrameworkCore;

namespace ChattyDuck.Corpus.Archivio;

public sealed record EsitoPubblicazione(string Versione, int Contenuti, int Sezioni, IReadOnlyList<Problema> Avvisi);

/// <summary>
/// Il magazzino del corpus: accetta istantanee e le rende interrogabili.
/// </summary>
public sealed class ArchivioCorpus(CorpusDbContext db, ILogger<ArchivioCorpus> log)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>
    /// Sostituisce il corpus di un ente con l'istantanea ricevuta.
    /// <para>
    /// La sostituzione e' integrale e non incrementale. Un adattatore che gira ogni ora
    /// non sa cosa e' cambiato nel CMS: sa solo com'e' adesso. Chiedergli di calcolare
    /// differenze significherebbe chiedergli di tenere uno stato, e sarebbe la prima
    /// cosa a divergere. Cosi' invece ogni pubblicazione riporta l'ente in uno stato noto.
    /// </para>
    /// </summary>
    public async Task<EsitoPubblicazione> Pubblica(Istantanea istantanea, EsitoValidazione esito, CancellationToken ct)
    {
        var i = esito.Normalizzata;
        var enteId = i.Ente.Id;
        var versione = i.GeneratoIl.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");

        // Se un adattatore ripubblica nello stesso secondo, si distingue con un suffisso:
        // meglio due versioni vicine che una collisione silenziosa.
        var esistenti = await db.Istantanee
            .Where(x => x.EnteId == enteId && x.Versione.StartsWith(versione))
            .CountAsync(ct);
        if (esistenti > 0) versione = $"{versione}.{esistenti}";

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var ente = await db.Enti.FirstOrDefaultAsync(x => x.EnteId == enteId, ct);
        if (ente is null)
        {
            ente = new RigaEnte { EnteId = enteId };
            db.Enti.Add(ente);
        }
        ente.Nome = i.Ente.Nome;
        ente.Url = i.Ente.Url;
        ente.VersioneCorrente = versione;
        ente.AggiornatoIl = DateTimeOffset.UtcNow;

        var sezioniTotali = i.Contenuti.Sum(c => c.Sezioni.Count);

        db.Istantanee.Add(new RigaIstantanea
        {
            EnteId = enteId,
            Versione = versione,
            GeneratoIl = i.GeneratoIl,
            RicevutoIl = DateTimeOffset.UtcNow,
            Sistema = i.Sorgente?.Sistema,
            NumeroContenuti = i.Contenuti.Count,
            NumeroSezioni = sezioniTotali,
            Json = JsonSerializer.Serialize(i, Json),
        });

        // La proiezione corrente si ricostruisce da zero: e' l'unico modo per garantire
        // che non sopravvivano contenuti cancellati nel CMS.
        await db.Contenuti.Where(x => x.EnteId == enteId).ExecuteDeleteAsync(ct);
        await db.Sezioni.Where(x => x.EnteId == enteId).ExecuteDeleteAsync(ct);
        await db.Attributi.Where(x => x.EnteId == enteId).ExecuteDeleteAsync(ct);
        await db.Relazioni.Where(x => x.EnteId == enteId).ExecuteDeleteAsync(ct);

        foreach (var c in i.Contenuti)
        {
            db.Contenuti.Add(new RigaContenuto
            {
                EnteId = enteId,
                ContenutoId = c.Id,
                Tipo = c.Tipo,
                Titolo = c.Titolo,
                Sommario = c.Sommario,
                Url = c.Url,
                Lingua = c.Lingua,
                ValidoDa = c.Validita?.Da,
                ValidoA = c.Validita?.A,
                AggiornatoIl = c.AggiornatoIl,
                Json = JsonSerializer.Serialize(c, Json),
            });

            foreach (var s in c.Sezioni)
                db.Sezioni.Add(new RigaSezione
                {
                    EnteId = enteId, SezioneId = s.Id!, ContenutoId = c.Id,
                    Chiave = s.Chiave, Etichetta = s.Etichetta, Ordine = s.Ordine,
                    Testo = s.Testo, Versione = s.Versione, Hash = s.Hash,
                });

            foreach (var a in c.Attributi)
            {
                var (testo, numero, data) = Normalizza(a);
                db.Attributi.Add(new RigaAttributo
                {
                    EnteId = enteId, ContenutoId = c.Id, Chiave = a.Chiave,
                    Etichetta = a.Etichetta, Tipo = a.Tipo,
                    ValoreJson = a.Valore.ValueKind == JsonValueKind.Undefined ? "null" : a.Valore.GetRawText(),
                    ValoreTesto = testo, ValoreNumero = numero, ValoreData = data,
                });
            }

            foreach (var r in c.Relazioni)
                db.Relazioni.Add(new RigaRelazione
                {
                    EnteId = enteId, DaId = c.Id, Tipo = r.Tipo,
                    VersoId = r.Verso, Etichetta = r.Etichetta,
                });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        log.LogInformation(
            "Corpus di {Ente} pubblicato: versione {Versione}, {Contenuti} contenuti, {Sezioni} sezioni, {Avvisi} avvisi",
            enteId, versione, i.Contenuti.Count, sezioniTotali, esito.Avvisi.Count());

        return new EsitoPubblicazione(versione, i.Contenuti.Count, sezioniTotali, esito.Avvisi.ToList());
    }

    /// <summary>
    /// Estrae dal valore JSON una forma scalare confrontabile, quando esiste.
    /// Serve a poter chiedere "le scadenze del mese" invece di leggerle una per una.
    /// </summary>
    private static (string? Testo, double? Numero, DateTimeOffset? Data) Normalizza(Attributo a)
    {
        var v = a.Valore;
        try
        {
            return a.Tipo switch
            {
                "testo" when v.ValueKind == JsonValueKind.String => (v.GetString(), null, null),
                "numero" when v.ValueKind == JsonValueKind.Number => (null, v.GetDouble(), null),
                "booleano" when v.ValueKind is JsonValueKind.True or JsonValueKind.False
                    => (v.GetBoolean().ToString().ToLowerInvariant(), v.GetBoolean() ? 1 : 0, null),
                "data" when v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), out var d)
                    => (v.GetString(), null, d),
                "importo" when v.ValueKind == JsonValueKind.Object && v.TryGetProperty("valore", out var imp)
                    && imp.ValueKind == JsonValueKind.Number => (null, imp.GetDouble(), null),
                "periodo" when v.ValueKind == JsonValueKind.Object && v.TryGetProperty("da", out var da)
                    && da.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(da.GetString(), out var dd)
                    => (null, null, dd),
                _ => (null, null, null),
            };
        }
        catch
        {
            // Un valore malformato non deve impedire la pubblicazione: resta nel JSON
            // grezzo, semplicemente non e' interrogabile come scalare.
            return (null, null, null);
        }
    }

    public Task<RigaEnte?> Ente(string enteId, CancellationToken ct) =>
        db.Enti.AsNoTracking().FirstOrDefaultAsync(x => x.EnteId == enteId, ct);

    public Task<List<RigaEnte>> Enti(CancellationToken ct) =>
        db.Enti.AsNoTracking().OrderBy(x => x.EnteId).ToListAsync(ct);

    public async Task<(string Versione, string Json)?> IstantaneaCorrente(string enteId, CancellationToken ct)
    {
        var ente = await db.Enti.AsNoTracking().FirstOrDefaultAsync(x => x.EnteId == enteId, ct);
        if (ente?.VersioneCorrente is null) return null;
        var riga = await db.Istantanee.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EnteId == enteId && x.Versione == ente.VersioneCorrente, ct);
        return riga is null ? null : (riga.Versione, riga.Json);
    }

    public Task<List<RigaIstantanea>> Versioni(string enteId, int limite, CancellationToken ct) =>
        db.Istantanee.AsNoTracking()
            .Where(x => x.EnteId == enteId)
            .OrderByDescending(x => x.RicevutoIl)
            .Take(limite)
            .Select(x => new RigaIstantanea
            {
                Id = x.Id, EnteId = x.EnteId, Versione = x.Versione, GeneratoIl = x.GeneratoIl,
                RicevutoIl = x.RicevutoIl, Sistema = x.Sistema,
                NumeroContenuti = x.NumeroContenuti, NumeroSezioni = x.NumeroSezioni, Json = "",
            })
            .ToListAsync(ct);

    /// <summary>Elenco dei contenuti, filtrabile. E' cio' che rende "quali eventi ci sono" una domanda con risposta.</summary>
    public async Task<List<RigaContenuto>> Elenca(
        string enteId, string? tipo, string? relazioneVerso, DateTimeOffset? validoAl, int limite, CancellationToken ct)
    {
        var q = db.Contenuti.AsNoTracking().Where(x => x.EnteId == enteId);

        if (!string.IsNullOrWhiteSpace(tipo)) q = q.Where(x => x.Tipo == tipo);

        if (!string.IsNullOrWhiteSpace(relazioneVerso))
        {
            var ids = db.Relazioni.AsNoTracking()
                .Where(r => r.EnteId == enteId && r.VersoId == relazioneVerso)
                .Select(r => r.DaId);
            q = q.Where(x => ids.Contains(x.ContenutoId));
        }

        if (validoAl is { } istante)
            q = q.Where(x => (x.ValidoDa == null || x.ValidoDa <= istante)
                          && (x.ValidoA == null || x.ValidoA >= istante));

        return await q.OrderBy(x => x.Tipo).ThenBy(x => x.Titolo).Take(limite).ToListAsync(ct);
    }

    public Task<RigaContenuto?> Contenuto(string enteId, string contenutoId, CancellationToken ct) =>
        db.Contenuti.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EnteId == enteId && x.ContenutoId == contenutoId, ct);
}
