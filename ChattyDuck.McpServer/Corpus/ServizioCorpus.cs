using System.Net;
using System.Text.Json;

namespace ChattyDuck.McpServer.Corpus;

/// <summary>
/// Tiene in memoria l'indice del corpus e lo riallinea al servizio che lo custodisce.
/// <para>
/// Il server MCP non possiede i dati: li legge dal corpus, che e' l'unico a scriverli.
/// Cosi' si possono avere piu' server MCP sullo stesso corpus, e riavviarne uno non
/// tocca nulla.
/// </para>
/// </summary>
public sealed class ServizioCorpus(IHttpClientFactory http, IConfiguration cfg, ILogger<ServizioCorpus> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private volatile IndiceCorpus? _indice;
    private string? _etag;

    public IndiceCorpus Indice => _indice
        ?? throw new InvalidOperationException("Corpus non ancora caricato.");

    public bool Pronto => _indice is not null;
    public DateTimeOffset? CaricatoIl { get; private set; }
    public string? Versione => _etag?.Trim('"');

    private string UrlIstantanea =>
        $"{(cfg["Corpus:Url"] ?? "http://localhost:5200").TrimEnd('/')}/api/enti/{cfg["Corpus:Ente"] ?? "comune-paperopoli"}/istantanea";

    /// <summary>
    /// Riallinea l'indice. Con l'ETag, se il corpus non e' cambiato non si riscarica
    /// nulla e l'indice resta quello.
    /// </summary>
    /// <param name="obbligatorio">All'avvio un corpus assente e' un errore fatale: meglio non partire che rispondere sul vuoto.</param>
    public async Task<bool> Riallinea(bool obbligatorio, CancellationToken ct = default)
    {
        try
        {
            var client = http.CreateClient(nameof(ServizioCorpus));
            client.Timeout = TimeSpan.FromSeconds(30);

            using var richiesta = new HttpRequestMessage(HttpMethod.Get, UrlIstantanea);
            if (_etag is not null) richiesta.Headers.TryAddWithoutValidation("If-None-Match", _etag);
            if (cfg["Corpus:Chiave"] is { Length: > 0 } chiave)
                richiesta.Headers.TryAddWithoutValidation("X-Corpus-Key", chiave);

            using var risposta = await client.SendAsync(richiesta, ct);

            if (risposta.StatusCode == HttpStatusCode.NotModified)
            {
                log.LogDebug("Corpus invariato ({Etag})", _etag);
                return false;
            }

            risposta.EnsureSuccessStatusCode();

            var testo = await risposta.Content.ReadAsStringAsync(ct);
            var istantanea = JsonSerializer.Deserialize<Istantanea>(testo, Json)
                ?? throw new InvalidOperationException("Istantanea non interpretabile.");

            var nuovo = new IndiceCorpus(istantanea);
            var precedente = _indice;
            _indice = nuovo;                                  // scambio atomico
            _etag = risposta.Headers.ETag?.Tag;
            CaricatoIl = DateTimeOffset.UtcNow;

            log.LogInformation(
                "Corpus di {Ente} caricato: versione {Versione}, {Contenuti} contenuti, {Sezioni} sezioni (prima: {Prima})",
                istantanea.Ente.Id, Versione, nuovo.NumeroContenuti, nuovo.NumeroSezioni,
                precedente?.NumeroContenuti ?? 0);

            return true;
        }
        catch (Exception ex)
        {
            if (obbligatorio)
                throw new InvalidOperationException(
                    $"Corpus non raggiungibile all'avvio su {UrlIstantanea}: {ex.Message}", ex);

            // A regime un corpus momentaneamente irraggiungibile non deve spegnere
            // l'assistente: resta valido l'indice gia' in memoria.
            log.LogWarning(ex, "Riallineamento fallito: resta in uso il corpus caricato in precedenza.");
            return false;
        }
    }
}

/// <summary>Riallinea il corpus a intervalli regolari.</summary>
public sealed class RiallineamentoPeriodico(
    ServizioCorpus corpus, IConfiguration cfg, ILogger<RiallineamentoPeriodico> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minuti = cfg.GetValue<int?>("Corpus:RiallineamentoMinuti") ?? 5;
        if (minuti <= 0)
        {
            log.LogInformation("Riallineamento periodico disattivato: resta POST /corpus/reload.");
            return;
        }

        log.LogInformation("Riallineamento del corpus ogni {Minuti} minuti.", minuti);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minuti));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await corpus.Riallinea(obbligatorio: false, stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogError(ex, "Riallineamento fallito."); }
        }
    }
}
