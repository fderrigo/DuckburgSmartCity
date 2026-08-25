using Microsoft.EntityFrameworkCore;

namespace ChattyDuck.Corpus.Archivio;

/// <summary>
/// Persistenza del corpus.
/// <para>
/// Due strati di proposito. Le <see cref="Istantanee"/> conservano il JSON ricevuto
/// integro: e' la storia, ed e' quella che permette di rispondere a "come era il corpus
/// il giorno in cui l'assistente ha dato quella risposta". Le altre tabelle sono la
/// proiezione dell'istantanea corrente, e servono a interrogare per tipo, validita' e
/// relazioni senza deserializzare tutto ogni volta.
/// </para>
/// </summary>
public sealed class CorpusDbContext(DbContextOptions<CorpusDbContext> options) : DbContext(options)
{
    public DbSet<RigaEnte> Enti => Set<RigaEnte>();
    public DbSet<RigaIstantanea> Istantanee => Set<RigaIstantanea>();
    public DbSet<RigaContenuto> Contenuti => Set<RigaContenuto>();
    public DbSet<RigaSezione> Sezioni => Set<RigaSezione>();
    public DbSet<RigaAttributo> Attributi => Set<RigaAttributo>();
    public DbSet<RigaRelazione> Relazioni => Set<RigaRelazione>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<RigaEnte>(e =>
        {
            e.HasKey(x => x.EnteId);
            e.Property(x => x.EnteId).HasMaxLength(64);
        });

        b.Entity<RigaIstantanea>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EnteId, x.Versione }).IsUnique();
            e.Property(x => x.EnteId).HasMaxLength(64);
            e.Property(x => x.Versione).HasMaxLength(64);
        });

        b.Entity<RigaContenuto>(e =>
        {
            e.HasKey(x => new { x.EnteId, x.ContenutoId });
            e.HasIndex(x => new { x.EnteId, x.Tipo });
            e.Property(x => x.EnteId).HasMaxLength(64);
            e.Property(x => x.ContenutoId).HasMaxLength(256);
            e.Property(x => x.Tipo).HasMaxLength(64);
        });

        b.Entity<RigaSezione>(e =>
        {
            e.HasKey(x => new { x.EnteId, x.SezioneId });
            e.HasIndex(x => new { x.EnteId, x.ContenutoId });
            e.Property(x => x.EnteId).HasMaxLength(64);
            e.Property(x => x.SezioneId).HasMaxLength(320);
            e.Property(x => x.ContenutoId).HasMaxLength(256);
            e.Property(x => x.Chiave).HasMaxLength(64);
        });

        b.Entity<RigaAttributo>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EnteId, x.ContenutoId });
            e.HasIndex(x => new { x.EnteId, x.Chiave });
            e.Property(x => x.EnteId).HasMaxLength(64);
            e.Property(x => x.ContenutoId).HasMaxLength(256);
            e.Property(x => x.Chiave).HasMaxLength(64);
            e.Property(x => x.Tipo).HasMaxLength(32);
        });

        b.Entity<RigaRelazione>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EnteId, x.DaId });
            e.HasIndex(x => new { x.EnteId, x.VersoId });
            e.Property(x => x.EnteId).HasMaxLength(64);
            e.Property(x => x.DaId).HasMaxLength(256);
            e.Property(x => x.VersoId).HasMaxLength(256);
            e.Property(x => x.Tipo).HasMaxLength(64);
        });
    }
}

public sealed class RigaEnte
{
    public string EnteId { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? Url { get; set; }
    /// <summary>Versione dell'istantanea attualmente pubblicata.</summary>
    public string? VersioneCorrente { get; set; }
    public DateTimeOffset AggiornatoIl { get; set; }
}

public sealed class RigaIstantanea
{
    public int Id { get; set; }
    public string EnteId { get; set; } = "";
    public string Versione { get; set; } = "";
    public DateTimeOffset GeneratoIl { get; set; }
    public DateTimeOffset RicevutoIl { get; set; }
    public string? Sistema { get; set; }
    public int NumeroContenuti { get; set; }
    public int NumeroSezioni { get; set; }
    /// <summary>Il JSON ricevuto, integro. Non si rigenera: si riserve.</summary>
    public string Json { get; set; } = "";
}

public sealed class RigaContenuto
{
    public string EnteId { get; set; } = "";
    public string ContenutoId { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Titolo { get; set; } = "";
    public string? Sommario { get; set; }
    public string? Url { get; set; }
    public string Lingua { get; set; } = "it";
    public DateTimeOffset? ValidoDa { get; set; }
    public DateTimeOffset? ValidoA { get; set; }
    public DateTimeOffset AggiornatoIl { get; set; }
    /// <summary>Il contenuto completo, per restituirlo senza ricomporlo dalle tabelle.</summary>
    public string Json { get; set; } = "";
}

public sealed class RigaSezione
{
    public string EnteId { get; set; } = "";
    public string SezioneId { get; set; } = "";
    public string ContenutoId { get; set; } = "";
    public string Chiave { get; set; } = "";
    public string? Etichetta { get; set; }
    public int Ordine { get; set; }
    public string Testo { get; set; } = "";
    public string? Versione { get; set; }
    public string? Hash { get; set; }
}

/// <summary>
/// Un attributo, con il valore sia grezzo sia normalizzato quando e' scalare.
/// La normalizzazione serve a interrogare: "quali scadenze cadono entro il mese" e'
/// una query sulle date, non una lettura di prosa.
/// </summary>
public sealed class RigaAttributo
{
    public int Id { get; set; }
    public string EnteId { get; set; } = "";
    public string ContenutoId { get; set; } = "";
    public string Chiave { get; set; } = "";
    public string? Etichetta { get; set; }
    public string Tipo { get; set; } = "";
    public string ValoreJson { get; set; } = "";
    public string? ValoreTesto { get; set; }
    public double? ValoreNumero { get; set; }
    public DateTimeOffset? ValoreData { get; set; }
}

public sealed class RigaRelazione
{
    public int Id { get; set; }
    public string EnteId { get; set; } = "";
    public string DaId { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string VersoId { get; set; } = "";
    public string? Etichetta { get; set; }
}
