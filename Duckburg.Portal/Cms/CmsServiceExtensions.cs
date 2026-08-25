using Microsoft.EntityFrameworkCore;

namespace Duckburg.Portal.Cms;

public static class CmsServiceExtensions
{
    /// <summary>
    /// Registra il CMS: opzioni, DbContext sul provider configurato e servizi.
    /// Il provider è scelto da <c>Cms:Database:Provider</c> senza toccare il codice.
    /// </summary>
    public static IServiceCollection AddPortalCms(this IServiceCollection services, IConfiguration config, string contentRoot)
    {
        var section = config.GetSection(CmsOptions.SectionName);
        services.Configure<CmsOptions>(section);
        var options = section.Get<CmsOptions>() ?? new CmsOptions();

        options.Database.ConnectionString = RisolviPercorsoSqlite(options.Database, contentRoot);

        services.AddDbContext<CmsDbContext>(db => ConfigureProvider(db, options.Database));

        services.AddScoped<ContentService>();
        services.AddScoped<CmsSeeder>();
        services.AddScoped<Admin.AdminService>();
        return services;
    }

    /// <summary>Applica il provider EF Core corretto in base alla configurazione.</summary>
    public static void ConfigureProvider(DbContextOptionsBuilder db, CmsDatabaseOptions dbOptions)
    {
        var provider = (dbOptions.Provider ?? "Sqlite").Trim().ToLowerInvariant();
        var cs = dbOptions.ConnectionString;

        switch (provider)
        {
            case "sqlite":
                // Il percorso e' gia' stato reso assoluto da RisolviPercorsoSqlite.
                db.UseSqlite(cs);
                break;
            case "sqlserver":
            case "mssql":
                db.UseSqlServer(cs);
                break;
            case "postgres":
            case "postgresql":
            case "npgsql":
                db.UseNpgsql(cs);
                break;
            case "mysql":
            case "mariadb":
                db.UseMySql(cs, ServerVersion.AutoDetect(cs));
                break;
            case "oracle":
                db.UseOracle(cs);
                break;
            default:
                throw new InvalidOperationException(
                    $"Provider CMS non riconosciuto: '{dbOptions.Provider}'. " +
                    "Valori ammessi: Sqlite, SqlServer, PostgreSql, MySql, Oracle.");
        }
    }

    /// <summary>
    /// Rende assoluto il percorso del file SQLite, ancorandolo alla radice del
    /// contenuto dell'applicazione.
    /// <para>
    /// Serve perche' un <c>Data Source</c> relativo viene risolto rispetto alla
    /// directory corrente del processo, che sotto IIS in-process non e' quella
    /// dell'applicazione ma quella del worker (<c>C:\Windows\System32\inetsrv</c>).
    /// Il risultato e' che il database viene cercato dove non esiste e non si puo'
    /// scrivere, con un "SQLite Error 14: unable to open database file" che sembra
    /// un problema di permessi e non lo e'.
    /// </para>
    /// </summary>
    private static string RisolviPercorsoSqlite(CmsDatabaseOptions dbOptions, string contentRoot)
    {
        var provider = (dbOptions.Provider ?? "Sqlite").Trim().ToLowerInvariant();
        if (provider != "sqlite") return dbOptions.ConnectionString;

        const string key = "Data Source=";
        var cs = dbOptions.ConnectionString;
        var idx = cs.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return cs;

        var inizio = idx + key.Length;
        var fine = cs.IndexOf(';', inizio);
        var percorso = (fine >= 0 ? cs[inizio..fine] : cs[inizio..]).Trim();

        // I nomi speciali di SQLite non sono percorsi.
        if (percorso.Length == 0 ||
            percorso.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
            percorso.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return cs;

        if (!Path.IsPathRooted(percorso))
            percorso = Path.GetFullPath(Path.Combine(contentRoot, percorso));

        var dir = Path.GetDirectoryName(percorso);
        if (!string.IsNullOrEmpty(dir)) PreparaCartellaDati(dir);

        return string.Concat(cs.AsSpan(0, inizio), percorso, fine >= 0 ? cs.AsSpan(fine) : "");
    }

    /// <summary>
    /// Si assicura che la cartella del database esista e sia scrivibile, e se non lo e'
    /// lo dice in modo che si capisca cosa fare.
    /// <para>
    /// Due errori diversi arrivano qui, e nessuno dei due si spiega da solo. Se la
    /// cartella manca, CreateDirectory fallisce con "Access to the path is denied" e uno
    /// stack che parla di IO. Se la cartella c'e' ma non e' scrivibile, l'avvio riesce e
    /// il guasto arriva molto dopo, come "SQLite Error 14: unable to open database file",
    /// che sembra un percorso sbagliato. In tutti e due i casi la causa e' la stessa e la
    /// soluzione e' una riga di icacls.
    /// </para>
    /// <para>
    /// Vale la pena ricordarlo nel messaggio: ricopiare la cartella dell'applicazione
    /// durante un deploy azzera i permessi concessi a mano, quindi lo stesso guasto
    /// tende a tornare a ogni pubblicazione.
    /// </para>
    /// </summary>
    private static void PreparaCartellaDati(string cartella)
    {
        try
        {
            if (!Directory.Exists(cartella)) Directory.CreateDirectory(cartella);

            // Esistere non basta: il database va scritto. Meglio scoprirlo adesso.
            var prova = Path.Combine(cartella, ".permessi-" + Environment.ProcessId);
            File.WriteAllText(prova, "");
            File.Delete(prova);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                $"La cartella dei dati '{cartella}' non e' scrivibile dal processo, che gira " +
                $"come '{Environment.UserName}'. Sotto IIS concedi la scrittura all'identita' " +
                $"dell'app pool, per esempio: icacls \"{cartella}\" /grant \"IIS_IUSRS:(OI)(CI)M\". " +
                "Ricorda che ricopiare la cartella dell'applicazione azzera i permessi dati prima.",
                ex);
        }
    }

    /// <summary>Crea lo schema (se assente) e popola i contenuti di default.</summary>
    public static async Task InitializeCmsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        await db.Database.EnsureCreatedAsync();

        var opts = scope.ServiceProvider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<CmsOptions>>().Value;
        if (opts.SeedOnStartup)
        {
            var seeder = scope.ServiceProvider.GetRequiredService<CmsSeeder>();
            await seeder.SeedAsync();
        }
    }
}
