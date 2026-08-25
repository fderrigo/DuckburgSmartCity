namespace Duckburg.Portal.Cms;

/// <summary>
/// Configurazione del CMS, legata alla sezione "Cms" di appsettings.
/// Governa provider del database e protezione dei contenuti di default.
/// </summary>
public sealed class CmsOptions
{
    public const string SectionName = "Cms";

    /// <summary>Impostazioni del database (provider plug-and-play).</summary>
    public CmsDatabaseOptions Database { get; set; } = new();

    /// <summary>
    /// Se true, i contenuti di default (seed, IsDefault=true) non possono
    /// essere modificati né eliminati dall'area di amministrazione.
    /// </summary>
    public bool ProtectDefaultContent { get; set; } = true;

    /// <summary>Se true, al primo avvio popola il database con i contenuti di default.</summary>
    public bool SeedOnStartup { get; set; } = true;

    /// <summary>Credenziali dell'area di amministrazione del CMS (demo).</summary>
    public CmsAdminOptions Admin { get; set; } = new();
}

public sealed class CmsDatabaseOptions
{
    /// <summary>
    /// Provider del database: Sqlite | SqlServer | PostgreSql | MySql | Oracle.
    /// Cambiando questo valore e la stringa di connessione si passa a un altro
    /// motore senza modificare il codice (plug-and-play).
    /// </summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>Stringa di connessione. Per Sqlite basta un percorso file.</summary>
    public string ConnectionString { get; set; } = "Data Source=App_Data/paperopoli-cms.db";
}

public sealed class CmsAdminOptions
{
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "paperopoli";
}
