using Microsoft.EntityFrameworkCore;

namespace Duckburg.Portal.Cms;

public static class CmsServiceExtensions
{
    /// <summary>
    /// Registra il CMS: opzioni, DbContext sul provider configurato e servizi.
    /// Il provider è scelto da <c>Cms:Database:Provider</c> senza toccare il codice.
    /// </summary>
    public static IServiceCollection AddPortalCms(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(CmsOptions.SectionName);
        services.Configure<CmsOptions>(section);
        var options = section.Get<CmsOptions>() ?? new CmsOptions();

        services.AddDbContext<CmsDbContext>(db => ConfigureProvider(db, options.Database));

        services.AddScoped<ContentService>();
        services.AddScoped<CmsSeeder>();
        services.AddScoped<Admin.AdminService>();
        services.AddScoped<CorpusFeed>();
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
                EnsureSqliteDirectory(cs);
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

    /// <summary>Crea la cartella del file SQLite se indicata nella stringa di connessione.</summary>
    private static void EnsureSqliteDirectory(string connectionString)
    {
        const string key = "Data Source=";
        var idx = connectionString.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;
        var rest = connectionString[(idx + key.Length)..];
        var end = rest.IndexOf(';');
        var path = (end >= 0 ? rest[..end] : rest).Trim();
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
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
