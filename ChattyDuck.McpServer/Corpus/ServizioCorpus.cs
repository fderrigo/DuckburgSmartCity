using System.Net;
using System.Text.Json;

namespace ChattyDuck.McpServer.Corpus;

/// <summary>Stato dell'indice, per chi chiede e per chi diagnostica.</summary>
public enum StatoCorpus
{
    /// <summary>Mai caricato: il servizio e' partito ma il corpus non ha ancora risposto.</summary>
    Allineamento,
    /// <summary>Indice in memoria e utilizzabile.</summary>
    Pronto,
    /// <summary>Indice utilizzabile, ma l'ultimo tentativo di riallineamento e' fallito.</summary>
    ProntoNonAggiornato,
}

/// <summary>
/// Tiene in memoria l'indice del corpus e lo riallinea al servizio che lo custodisce.
/// <para>
/// Il server MCP non possiede i dati: li legge dal corpus, che e' l'unico a scriverli.
/// Cosi' si possono avere piu' server MCP sullo stesso corpus, e riavviarne uno non
/// tocca nulla.
/// </para>
/// <para>
/// Parte sempre, anche se il corpus non risponde. L'ordine di avvio dei servizi non deve
/// essere un requisito: basterebbe un riavvio della macchina nell'ordine sbagliato per
/// lasciare il sistema giu' da solo. Finche' l'indice non c'e', gli strumenti dicono che
/// l'allineamento e' in corso invece di rispondere sul vuoto, che sarebbe peggio: un
/// elenco vuoto verrebbe letto come "questa informazione non esiste".
/// </para>
/// </summary>
public sealed class ServizioCorpus(IHttpClientFactory http, IConfiguration cfg, ILogger<ServizioCorpus> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private volatile IndiceCorpus? _indice;
    private string? _etag;

    public IndiceCorpus? Indice => _indice;
    public bool Pronto => _indice is not null;
    public DateTimeOffset? CaricatoIl { get; private set; }
    public string? Versione => _etag?.Trim('"');
    public string? UltimoErrore { get; private set; }
    public int TentativiFalliti { get; private set; }

    public StatoCorpus Stato => _indice is null
        ? StatoCorpus.Allineamento
        : UltimoErrore is null ? StatoCorpus.Pronto : StatoCorpus.ProntoNonAggiornato;

    /// <summary>Messaggio adatto a chi legge: interfaccia, diagnostica, o il modello stesso.</summary>
    public string Descrizione => Stato switch
    {
        StatoCorpus.Pronto => "Corpus allineato.",
        StatoCorpus.ProntoNonAggiornato =>
            "Corpus disponibile ma non aggiornato: l'ultimo riallineamento non e' riuscito.",
        _ => "Allineamento del corpus in corso: i contenuti non sono ancora disponibili.",
    };

    private string UrlIstantanea =>
        $"{(cfg["Corpus:Url"] ?? "http://localhost:5200").TrimEnd('/')}/api/enti/{cfg["Corpus:Ente"] ?? "comune-paperopoli"}/istantanea";

    /// <summary>
    /// Riallinea l'indice. Con l'ETag, se il corpus non e' cambiato non si riscarica nulla.
    /// Non solleva mai: un corpus irraggiungibile e' una condizione prevista, non un guasto.
    /// </summary>
    /// <returns>true se l'indice e' stato sostituito.</returns>
    public async Task<bool> Riallinea(CancellationToken ct = default)
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
                UltimoErrore = null;
                TentativiFalliti = 0;
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
            UltimoErrore = null;
            TentativiFalliti = 0;

            log.LogInformation(
                "Corpus di {Ente} caricato: versione {Versione}, {Contenuti} contenuti, {Sezioni} sezioni (prima: {Prima})",
                istantanea.Ente.Id, Versione, nuovo.NumeroContenuti, nuovo.NumeroSezioni,
                precedente?.NumeroContenuti ?? 0);

            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            UltimoErrore = ex.Message;
            TentativiFalliti++;

            // Al primo avvio e' normale: il corpus potrebbe non essere ancora su. Si
            // registra come informazione finche' i tentativi sono pochi, poi come avviso.
            if (_indice is null && TentativiFalliti <= 3)
                log.LogInformation("Corpus non ancora disponibile su {Url}: riprovo. ({Errore})", UrlIstantanea, ex.Message);
            else
                log.LogWarning(ex, "Riallineamento fallito ({Tentativi} tentativi). {Stato}",
                    TentativiFalliti, _indice is null ? "Nessun indice in memoria." : "Resta in uso quello precedente.");

            return false;
        }
    }
}

/// <summary>
/// Riallinea il corpus, insistendo finche' non c'e'.
/// <para>
/// Due ritmi. Finche' l'indice manca riprova spesso, con attesa crescente fino a un
/// minuto: e' la fase in cui il corpus sta ancora partendo o l'ingestione non ha ancora
/// pubblicato. Una volta allineato passa all'intervallo configurato.
/// </para>
/// </summary>
public sealed class Allineatore(
    ServizioCorpus corpus, IConfiguration cfg, ILogger<Allineatore> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minuti = cfg.GetValue<int?>("Corpus:RiallineamentoMinuti") ?? 5;
        log.LogInformation("Allineamento del corpus: subito, poi ogni {Minuti} minuti.", minuti);

        // Fase di attesa: il servizio e' gia' in ascolto, quindi non blocca nessuno.
        var attesa = TimeSpan.FromSeconds(2);
        while (!stoppingToken.IsCancellationRequested && !corpus.Pronto)
        {
            await corpus.Riallinea(stoppingToken);
            if (corpus.Pronto) break;
            try { await Task.Delay(attesa, stoppingToken); } catch (OperationCanceledException) { return; }
            attesa = TimeSpan.FromSeconds(Math.Min(attesa.TotalSeconds * 2, 60));
        }

        if (minuti <= 0)
        {
            log.LogInformation("Riallineamento periodico disattivato: resta POST /corpus/reload.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minuti));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await corpus.Riallinea(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogError(ex, "Riallineamento fallito."); }
        }
    }
}
