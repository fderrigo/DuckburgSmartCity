using Microsoft.EntityFrameworkCore;

namespace ChattyDuck.Corpus.Archivio;

/// <summary>Configurazione del servizio, legata alla sezione "Corpus".</summary>
public sealed class CorpusOptions
{
    public const string SectionName = "Corpus";

    public DatabaseOptions Database { get; set; } = new();

    /// <summary>
    /// Enti serviti da questa installazione. Ognuno con la propria chiave di scrittura:
    /// e' la chiave che l'adattatore di quell'ente usa per pubblicare, e non gli permette
    /// di toccare il corpus di nessun altro.
    /// </summary>
    public List<EnteConfigurato> Enti { get; set; } = new();

    /// <summary>
    /// Chiave richiesta in lettura. Vuota significa lettura aperta: e' il caso normale,
    /// perche' il corpus contiene solo cio' che l'ente pubblica gia' sul proprio sito.
    /// </summary>
    public string? ChiaveLettura { get; set; }

    /// <summary>Quante istantanee conservare per ente. Zero le conserva tutte.</summary>
    public int StoriaDaConservare { get; set; } = 30;
}

public sealed class EnteConfigurato
{
    public string Id { get; set; } = "";
    public string ChiaveIngestione { get; set; } = "";
}

public sealed class DatabaseOptions
{
    /// <summary>Sqlite | SqlServer | PostgreSql | MySql.</summary>
    public string Provider { get; set; } = "Sqlite";
    public string ConnectionString { get; set; } = "Data Source=App_Data/corpus.db";
}

public static class CorpusServiceExtensions
{
    public static IServiceCollection AddCorpus(this IServiceCollection services, IConfiguration config, string contentRoot)
    {
        var section = config.GetSection(CorpusOptions.SectionName);
        services.Configure<CorpusOptions>(section);
        var opts = section.Get<CorpusOptions>() ?? new CorpusOptions();

        opts.Database.ConnectionString = RisolviPercorsoSqlite(opts.Database, contentRoot);

        services.AddDbContext<CorpusDbContext>(db =>
        {
            var provider = (opts.Database.Provider ?? "Sqlite").Trim().ToLowerInvariant();
            var cs = opts.Database.ConnectionString;
            switch (provider)
            {
                case "sqlite": db.UseSqlite(cs); break;
                case "sqlserver" or "mssql": db.UseSqlServer(cs); break;
                case "postgres" or "postgresql" or "npgsql": db.UseNpgsql(cs); break;
                case "mysql" or "mariadb": db.UseMySql(cs, ServerVersion.AutoDetect(cs)); break;
                default:
                    throw new InvalidOperationException(
                        $"Provider non riconosciuto: '{opts.Database.Provider}'. Ammessi: Sqlite, SqlServer, PostgreSql, MySql.");
            }
        });

        services.AddScoped<ArchivioCorpus>();
        return services;
    }

    /// <summary>
    /// Rende assoluto il percorso del file SQLite ancorandolo alla radice del contenuto.
    /// Un percorso relativo verrebbe altrimenti risolto rispetto alla directory corrente
    /// del processo, che sotto IIS non e' quella dell'applicazione.
    /// </summary>
    private static string RisolviPercorsoSqlite(DatabaseOptions db, string contentRoot)
    {
        if (!string.Equals(db.Provider, "Sqlite", StringComparison.OrdinalIgnoreCase)) return db.ConnectionString;

        const string chiave = "Data Source=";
        var cs = db.ConnectionString;
        var idx = cs.IndexOf(chiave, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return cs;

        var inizio = idx + chiave.Length;
        var fine = cs.IndexOf(';', inizio);
        var percorso = (fine >= 0 ? cs[inizio..fine] : cs[inizio..]).Trim();

        if (percorso.Length == 0 || percorso.Equals(":memory:", StringComparison.OrdinalIgnoreCase)) return cs;
        if (!Path.IsPathRooted(percorso)) percorso = Path.GetFullPath(Path.Combine(contentRoot, percorso));

        var cartella = Path.GetDirectoryName(percorso);
        if (!string.IsNullOrEmpty(cartella)) Directory.CreateDirectory(cartella);

        return string.Concat(cs.AsSpan(0, inizio), percorso, fine >= 0 ? cs.AsSpan(fine) : "");
    }

    public static async Task InizializzaCorpusAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CorpusDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
