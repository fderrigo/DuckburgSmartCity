using System.Text.Json;
using ChattyDuck.Corpus.Archivio;
using ChattyDuck.Corpus.Modello;
using Microsoft.Extensions.Options;

namespace ChattyDuck.Corpus.Api;

public static class Endpoints
{
    private const string IntestazioneChiave = "X-Corpus-Key";

    // ---------------------------------------------------------------- ingestione

    public static void MapEndpointsIngestione(this WebApplication app)
    {
        // Pubblica un'istantanea intera. Idempotente rispetto al contenuto: ripubblicare
        // la stessa istantanea riporta l'ente nello stesso stato.
        app.MapPut("/api/enti/{ente}/istantanea", async (
            string ente,
            HttpRequest richiesta,
            ArchivioCorpus archivio,
            IOptions<CorpusOptions> opzioni,
            ILoggerFactory logger,
            CancellationToken ct) =>
        {
            var log = logger.CreateLogger("Ingestione");

            if (!ChiaveValida(richiesta, opzioni.Value, ente, out var motivo))
                return Results.Json(new { errore = motivo }, statusCode: StatusCodes.Status401Unauthorized);

            Istantanea? istantanea;
            try
            {
                istantanea = await JsonSerializer.DeserializeAsync<Istantanea>(
                    richiesta.Body, ArchivioCorpus.Json, ct);
            }
            catch (JsonException ex)
            {
                return Results.Json(new { errore = "JSON non interpretabile", dettaglio = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (istantanea is null)
                return Results.Json(new { errore = "Corpo della richiesta vuoto" },
                    statusCode: StatusCodes.Status400BadRequest);

            // L'ente nell'indirizzo e quello nel corpo devono coincidere: una chiave non
            // deve poter scrivere sul corpus di un altro per una svista nel corpo.
            if (!string.Equals(istantanea.Ente.Id, ente, StringComparison.Ordinal))
                return Results.Json(new
                {
                    errore = "Ente incoerente",
                    dettaglio = $"Indirizzo '{ente}', corpo '{istantanea.Ente.Id}'.",
                }, statusCode: StatusCodes.Status400BadRequest);

            var esito = Validatore.Valida(istantanea);
            if (!esito.Valida)
            {
                log.LogWarning("Istantanea di {Ente} rifiutata: {Errori} errori", ente, esito.Errori.Count());
                return Results.Json(new
                {
                    errore = "Istantanea non valida",
                    errori = esito.Errori.Select(p => new { percorso = p.Percorso, messaggio = p.Messaggio }),
                    avvisi = esito.Avvisi.Select(p => new { percorso = p.Percorso, messaggio = p.Messaggio }),
                }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var pubblicata = await archivio.Pubblica(istantanea, esito, ct);

            return Results.Json(new
            {
                versione = pubblicata.Versione,
                contenuti = pubblicata.Contenuti,
                sezioni = pubblicata.Sezioni,
                // Gli avvisi si restituiscono sempre: sono la lista delle cose che
                // l'adattatore puo' migliorare senza che il corpus sia inutilizzabile.
                avvisi = pubblicata.Avvisi.Select(p => new { percorso = p.Percorso, messaggio = p.Messaggio }),
            });
        });

        // Verifica un'istantanea senza pubblicarla. Serve a chi scrive un adattatore:
        // puo' provare finche' non e' pulito senza toccare il corpus vivo.
        app.MapPost("/api/enti/{ente}/istantanea/verifica", async (
            string ente, HttpRequest richiesta, IOptions<CorpusOptions> opzioni, CancellationToken ct) =>
        {
            if (!ChiaveValida(richiesta, opzioni.Value, ente, out var motivo))
                return Results.Json(new { errore = motivo }, statusCode: StatusCodes.Status401Unauthorized);

            Istantanea? istantanea;
            try
            {
                istantanea = await JsonSerializer.DeserializeAsync<Istantanea>(richiesta.Body, ArchivioCorpus.Json, ct);
            }
            catch (JsonException ex)
            {
                return Results.Json(new { errore = "JSON non interpretabile", dettaglio = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
            if (istantanea is null) return Results.BadRequest(new { errore = "Corpo vuoto" });

            var esito = Validatore.Valida(istantanea);
            return Results.Ok(new
            {
                valida = esito.Valida,
                contenuti = esito.Normalizzata.Contenuti.Count,
                sezioni = esito.Normalizzata.Contenuti.Sum(c => c.Sezioni.Count),
                errori = esito.Errori.Select(p => new { percorso = p.Percorso, messaggio = p.Messaggio }),
                avvisi = esito.Avvisi.Select(p => new { percorso = p.Percorso, messaggio = p.Messaggio }),
            });
        });
    }

    // ------------------------------------------------------------------- lettura

    public static void MapEndpointsLettura(this WebApplication app)
    {
        // L'istantanea corrente, servita cosi' com'e' stata ricevuta.
        // L'ETag e' la versione: chi tira periodicamente non riscarica se non e' cambiata.
        app.MapGet("/api/enti/{ente}/istantanea", async (
            string ente, HttpRequest richiesta, HttpResponse risposta,
            ArchivioCorpus archivio, IOptions<CorpusOptions> opzioni, CancellationToken ct) =>
        {
            if (!LetturaConsentita(richiesta, opzioni.Value))
                return Results.Json(new { errore = "Chiave di lettura mancante o errata" },
                    statusCode: StatusCodes.Status401Unauthorized);

            var corrente = await archivio.IstantaneaCorrente(ente, ct);
            if (corrente is null) return Results.NotFound(new { errore = $"Nessun corpus per l'ente '{ente}'" });

            var etag = $"\"{corrente.Value.Versione}\"";
            if (richiesta.Headers.IfNoneMatch.ToString() == etag)
                return Results.StatusCode(StatusCodes.Status304NotModified);

            risposta.Headers.ETag = etag;
            return Results.Content(corrente.Value.Json, "application/json");
        });

        app.MapGet("/api/enti/{ente}/versioni", async (
            string ente, int? limite, ArchivioCorpus archivio, CancellationToken ct) =>
        {
            var righe = await archivio.Versioni(ente, Math.Clamp(limite ?? 20, 1, 200), ct);
            return Results.Ok(righe.Select(r => new
            {
                versione = r.Versione, generato_il = r.GeneratoIl, ricevuto_il = r.RicevutoIl,
                sistema = r.Sistema, contenuti = r.NumeroContenuti, sezioni = r.NumeroSezioni,
            }));
        });

        // Elenco filtrabile: e' cio' che trasforma "quali eventi ci sono" da ricerca
        // testuale sperata a interrogazione con una risposta certa.
        app.MapGet("/api/enti/{ente}/contenuti", async (
            string ente, string? tipo, string? relazione, bool? soloValidi, int? limite,
            ArchivioCorpus archivio, CancellationToken ct) =>
        {
            var validoAl = soloValidi == true ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
            var righe = await archivio.Elenca(ente, tipo, relazione, validoAl, Math.Clamp(limite ?? 100, 1, 1000), ct);
            return Results.Ok(righe.Select(r => new
            {
                id = r.ContenutoId, tipo = r.Tipo, titolo = r.Titolo, sommario = r.Sommario,
                url = r.Url, valido_da = r.ValidoDa, valido_a = r.ValidoA, aggiornato_il = r.AggiornatoIl,
            }));
        });

        app.MapGet("/api/enti/{ente}/contenuti/{id}", async (
            string ente, string id, ArchivioCorpus archivio, CancellationToken ct) =>
        {
            var riga = await archivio.Contenuto(ente, id, ct);
            return riga is null
                ? Results.NotFound(new { errore = $"Contenuto '{id}' non trovato per l'ente '{ente}'" })
                : Results.Content(riga.Json, "application/json");
        });

        app.MapGet("/api/enti", async (ArchivioCorpus archivio, CancellationToken ct) =>
            Results.Ok((await archivio.Enti(ct)).Select(e => new
            {
                id = e.EnteId, nome = e.Nome, url = e.Url,
                versione = e.VersioneCorrente, aggiornato_il = e.AggiornatoIl,
            })));
    }

    // ------------------------------------------------------------------- chiavi

    private static bool ChiaveValida(HttpRequest richiesta, CorpusOptions opzioni, string ente, out string motivo)
    {
        var configurato = opzioni.Enti.FirstOrDefault(e =>
            string.Equals(e.Id, ente, StringComparison.OrdinalIgnoreCase));

        if (configurato is null)
        {
            motivo = $"Ente '{ente}' non configurato su questo servizio.";
            return false;
        }

        var fornita = richiesta.Headers[IntestazioneChiave].ToString();
        if (string.IsNullOrEmpty(configurato.ChiaveIngestione))
        {
            motivo = $"Ente '{ente}' senza chiave di ingestione configurata: pubblicazione disattivata.";
            return false;
        }

        // Confronto a tempo costante: una chiave non deve poter essere indovinata a
        // tentativi misurando quanto ci mette il rifiuto.
        var atteso = System.Text.Encoding.UTF8.GetBytes(configurato.ChiaveIngestione);
        var ricevuto = System.Text.Encoding.UTF8.GetBytes(fornita);
        if (atteso.Length != ricevuto.Length ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(atteso, ricevuto))
        {
            motivo = "Chiave di ingestione mancante o errata.";
            return false;
        }

        motivo = "";
        return true;
    }

    private static bool LetturaConsentita(HttpRequest richiesta, CorpusOptions opzioni)
    {
        if (string.IsNullOrEmpty(opzioni.ChiaveLettura)) return true;
        return richiesta.Headers[IntestazioneChiave].ToString() == opzioni.ChiaveLettura;
    }
}
