using System.Text.Json;
using ChattyDuck.Corpus.Api;
using ChattyDuck.Corpus.Archivio;

var builder = WebApplication.CreateBuilder(args);

// Servizio del corpus: riceve istantanee dagli adattatori e le espone a chi legge.
// Non conosce nessun CMS. E' questo il confine dell'architettura: a monte ogni ente ha
// il proprio gestore di contenuti, a valle nessuno sa piu' da dove i contenuti vengano.
builder.Services.AddCorpus(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = null;   // i nomi li detta il modello
    o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.UseStaticFiles();   // lo schema pubblicato vive in wwwroot/schema

app.MapEndpointsIngestione();
app.MapEndpointsLettura();

app.MapGet("/health", async (ArchivioCorpus archivio, CancellationToken ct) =>
{
    var enti = await archivio.Enti(ct);
    return Results.Ok(new
    {
        status = "ok",
        modello = ChattyDuck.Corpus.Modello.Vocabolario.VersioneModello,
        enti = enti.Select(e => new
        {
            id = e.EnteId, nome = e.Nome,
            versione = e.VersioneCorrente, aggiornato_il = e.AggiornatoIl,
        }),
    });
});

await app.Services.InizializzaCorpusAsync();

app.Run();
