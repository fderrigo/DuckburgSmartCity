using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Duckburg.Ingestione.Mappatura;

namespace Duckburg.Ingestione;

public sealed record EsitoEsecuzione(
    DateTimeOffset Istante, bool Riuscita, string? Versione,
    int Contenuti, int Sezioni, IReadOnlyList<string> Avvisi, string? Errore, TimeSpan Durata);

/// <summary>
/// Legge il CMS, costruisce l'istantanea e la pubblica sul corpus.
/// </summary>
public sealed class ServizioIngestione(
    IServiceScopeFactory scopeFactory, IHttpClientFactory http, IConfiguration cfg, ILogger<ServizioIngestione> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Ultima esecuzione, per la pagina di stato. In memoria: non e' un dato da conservare.</summary>
    public EsitoEsecuzione? Ultima { get; private set; }

    public async Task<EsitoEsecuzione> Esegui(CancellationToken ct)
    {
        var avvio = DateTimeOffset.UtcNow;
        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var mappatura = scope.ServiceProvider.GetRequiredService<MappaturaDuckburgCms>();

            var istantanea = await mappatura.Costruisci(ct);

            var baseUrl = (cfg["Ingestione:UrlCorpus"] ?? "http://localhost:5200").TrimEnd('/');
            var ente = cfg["Ingestione:IdEnte"] ?? "comune-paperopoli";
            var chiave = cfg["Ingestione:ChiaveCorpus"] ?? "";

            var client = http.CreateClient(nameof(ServizioIngestione));
            client.Timeout = TimeSpan.FromMinutes(2);

            using var richiesta = new HttpRequestMessage(HttpMethod.Put, $"{baseUrl}/api/enti/{ente}/istantanea")
            {
                Content = JsonContent.Create(istantanea, options: Json),
            };
            richiesta.Headers.TryAddWithoutValidation("X-Corpus-Key", chiave);

            using var risposta = await client.SendAsync(richiesta, ct);
            var corpo = await risposta.Content.ReadAsStringAsync(ct);

            if (!risposta.IsSuccessStatusCode)
            {
                // Il corpo di un rifiuto contiene l'elenco puntuale dei problemi: e' la
                // cosa piu' utile da registrare, molto piu' del codice di stato.
                log.LogError("Corpus ha rifiutato l'istantanea ({Stato}): {Corpo}", (int)risposta.StatusCode, corpo);
                return Ultima = new EsitoEsecuzione(avvio, false, null, 0, 0, [],
                    $"HTTP {(int)risposta.StatusCode}: {Tronca(corpo, 2000)}", cronometro.Elapsed);
            }

            var esito = JsonSerializer.Deserialize<RispostaCorpus>(corpo, Json);
            var avvisi = esito?.Avvisi?.Select(a => $"{a.Percorso}: {a.Messaggio}").ToList() ?? [];

            log.LogInformation(
                "Istantanea pubblicata: versione {Versione}, {Contenuti} contenuti, {Sezioni} sezioni, {Avvisi} avvisi, in {Ms} ms",
                esito?.Versione, esito?.Contenuti, esito?.Sezioni, avvisi.Count, cronometro.ElapsedMilliseconds);

            return Ultima = new EsitoEsecuzione(avvio, true, esito?.Versione,
                esito?.Contenuti ?? 0, esito?.Sezioni ?? 0, avvisi, null, cronometro.Elapsed);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Ingestione fallita");
            return Ultima = new EsitoEsecuzione(avvio, false, null, 0, 0, [], ex.Message, cronometro.Elapsed);
        }
    }

    private static string Tronca(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    private sealed record RispostaCorpus(string? Versione, int Contenuti, int Sezioni, List<AvvisoCorpus>? Avvisi);
    private sealed record AvvisoCorpus(string Percorso, string Messaggio);
}

/// <summary>
/// Il pianificatore, dentro il servizio e non fuori.
/// <para>
/// Un cron di sistema legherebbe l'ingestione alla macchina che la ospita. Cosi' invece
/// resta un servizio autosufficiente: si installa dove serve e si porta dietro la propria
/// pianificazione, che e' quello che serve quando gira dentro la rete di un cliente.
/// </para>
/// <para>
/// Insiste finche' la prima esecuzione non riesce, con attesa crescente. Al primo avvio
/// il corpus o il CMS potrebbero non essere ancora pronti, e aspettare l'intero
/// intervallo lascerebbe l'assistente senza contenuti per un quarto d'ora buono senza
/// che nessuno lo abbia deciso.
/// </para>
/// </summary>
public sealed class Pianificatore(
    ServizioIngestione ingestione, IConfiguration cfg, ILogger<Pianificatore> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minuti = cfg.GetValue<int?>("Ingestione:IntervalloMinuti") ?? 15;

        // Prima esecuzione: si insiste finche' non riesce.
        var attesa = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            var esito = await ingestione.Esegui(stoppingToken);
            if (esito.Riuscita) break;

            log.LogInformation(
                "Prima ingestione non riuscita ({Errore}). Riprovo fra {Secondi} secondi.",
                esito.Errore, (int)attesa.TotalSeconds);

            try { await Task.Delay(attesa, stoppingToken); } catch (OperationCanceledException) { return; }
            attesa = TimeSpan.FromSeconds(Math.Min(attesa.TotalSeconds * 2, 120));
        }

        if (minuti <= 0)
        {
            log.LogInformation("Pianificazione disattivata: resta l'innesco manuale su POST /esegui.");
            return;
        }

        log.LogInformation("Ingestione pianificata ogni {Minuti} minuti.", minuti);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minuti));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await ingestione.Esegui(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogError(ex, "Esecuzione pianificata fallita"); }
        }
    }
}
