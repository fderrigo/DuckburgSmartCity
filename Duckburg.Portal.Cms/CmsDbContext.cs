using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Duckburg.Portal.Cms;

public sealed class CmsDbContext : DbContext
{
    public CmsDbContext(DbContextOptions<CmsDbContext> options) : base(options) { }

    public DbSet<Argomento> Argomenti => Set<Argomento>();
    public DbSet<CategoriaServizio> CategorieServizio => Set<CategoriaServizio>();
    public DbSet<Servizio> Servizi => Set<Servizio>();
    public DbSet<Novita> Novita => Set<Novita>();
    public DbSet<Evento> Eventi => Set<Evento>();
    public DbSet<Luogo> Luoghi => Set<Luogo>();
    public DbSet<Persona> Persone => Set<Persona>();
    public DbSet<UnitaOrganizzativa> Unita => Set<UnitaOrganizzativa>();
    public DbSet<Documento> Documenti => Set<Documento>();
    public DbSet<Pagina> Pagine => Set<Pagina>();
    public DbSet<VoceMenu> VociMenu => Set<VoceMenu>();
    public DbSet<Impostazione> Impostazioni => Set<Impostazione>();
    public DbSet<FaqItem> Faq => Set<FaqItem>();
    public DbSet<Appuntamento> Appuntamenti => Set<Appuntamento>();
    public DbSet<Segnalazione> Segnalazioni => Set<Segnalazione>();
    public DbSet<ValutazionePagina> Valutazioni => Set<ValutazionePagina>();
    public DbSet<MediaFile> Media => Set<MediaFile>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Converter JSON per liste di stringhe: portabile su ogni provider (colonna testo).
        var listConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => string.IsNullOrWhiteSpace(v)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

        var listComparer = new ValueComparer<List<string>>(
            (a, c) => (a ?? new()).SequenceEqual(c ?? new()),
            v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
            v => v.ToList());

        void JsonList<T>(System.Linq.Expressions.Expression<Func<T, List<string>>> prop) where T : class
        {
            b.Entity<T>().Property(prop).HasConversion(listConverter).Metadata.SetValueComparer(listComparer);
        }

        JsonList<Servizio>(x => x.Scadenze);
        JsonList<Servizio>(x => x.Fonti);
        JsonList<Persona>(x => x.Deleghe);
        JsonList<UnitaOrganizzativa>(x => x.Competenze);
        JsonList<Segnalazione>(x => x.Allegati);
        JsonList<ValutazionePagina>(x => x.Risposte);

        // Slug indicizzati per lookup delle pagine pubbliche.
        b.Entity<Servizio>().HasIndex(x => x.Slug);
        b.Entity<Novita>().HasIndex(x => x.Slug);
        b.Entity<Persona>().HasIndex(x => x.Slug);
        b.Entity<Pagina>().HasIndex(x => x.Slug);
        b.Entity<Argomento>().HasIndex(x => x.Slug);
        b.Entity<CategoriaServizio>().HasIndex(x => x.Slug);
        b.Entity<Impostazione>().HasIndex(x => x.Chiave).IsUnique();
        b.Entity<Appuntamento>().HasIndex(x => new { x.UfficioId, x.Data, x.Ora });

        b.Entity<Servizio>()
            .HasOne(x => x.Argomento).WithMany(a => a.Servizi)
            .HasForeignKey(x => x.ArgomentoId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Servizio>()
            .HasOne(x => x.Categoria).WithMany(c => c.Servizi)
            .HasForeignKey(x => x.CategoriaId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Servizio>()
            .HasOne(x => x.UnitaOrganizzativa).WithMany()
            .HasForeignKey(x => x.UnitaOrganizzativaId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Novita>()
            .HasOne(x => x.Argomento).WithMany(a => a.Novita)
            .HasForeignKey(x => x.ArgomentoId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Novita>()
            .HasOne(x => x.ACuraDi).WithMany()
            .HasForeignKey(x => x.ACuraDiId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Documento>()
            .HasOne(x => x.UfficioResponsabile).WithMany()
            .HasForeignKey(x => x.UfficioResponsabileId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Appuntamento>()
            .HasOne(x => x.Ufficio).WithMany()
            .HasForeignKey(x => x.UfficioId).OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(b);
    }

    /// <summary>Timestamp automatici su create/update.</summary>
    public override int SaveChanges()
    {
        Stamp();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Stamp();
        return base.SaveChangesAsync(ct);
    }

    private void Stamp()
    {
        var now = DateTime.UtcNow;
        foreach (var e in ChangeTracker.Entries<CmsEntity>())
        {
            if (e.State == EntityState.Added)
            {
                if (e.Entity.CreatedAt == default) e.Entity.CreatedAt = now;
                e.Entity.UpdatedAt = now;
            }
            else if (e.State == EntityState.Modified)
            {
                e.Entity.UpdatedAt = now;
            }
        }
    }
}
