using Evita.Models;
using Microsoft.EntityFrameworkCore;

namespace Evita.Data;

/// <summary>
/// Jediné miesto, ktoré pozná konkrétnu databázu. Pri prechode na zdieľanú databázu
/// sa vymení len poskytovateľ v <see cref="OnConfiguring"/>.
/// </summary>
public sealed class EvitaDbContext : DbContext
{
    private readonly string _cesta;

    public EvitaDbContext(string cesta)
    {
        _cesta = cesta;
    }

    public DbSet<Polozka> Polozky => Set<Polozka>();
    public DbSet<Kategoria> Kategorie => Set<Kategoria>();
    public DbSet<Miestnost> Miestnosti => Set<Miestnost>();
    public DbSet<Osoba> Osoby => Set<Osoba>();
    public DbSet<HistoriaZmeny> Historia => Set<HistoriaZmeny>();
    public DbSet<Nastavenie> Nastavenia => Set<Nastavenie>();
    public DbSet<ZaznamZalohy> ZaznamyZaloh => Set<ZaznamZalohy>();
    public DbSet<TypUdalosti> TypyUdalosti => Set<TypUdalosti>();
    public DbSet<Udalost> Udalosti => Set<Udalost>();
    public DbSet<Uloha> Ulohy => Set<Uloha>();
    public DbSet<Poznamka> Poznamky => Set<Poznamka>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={_cesta}");
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Polozka>(e =>
        {
            e.ToTable("Polozky");
            e.HasIndex(x => x.PrirastkoveCislo).IsUnique();
            e.HasIndex(x => x.Guid).IsUnique();
            e.HasIndex(x => x.Stav);
            e.HasIndex(x => x.DatumPridania);
            e.HasIndex(x => x.SerioveCislo);
            e.Property(x => x.Nazov).IsRequired();
            e.Property(x => x.Verzia).IsConcurrencyToken();
            e.HasOne(x => x.Kategoria).WithMany().HasForeignKey(x => x.KategoriaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Miestnost).WithMany().HasForeignKey(x => x.MiestnostId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Osoba).WithMany().HasForeignKey(x => x.OsobaId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Kategoria>(e =>
        {
            e.ToTable("Kategorie");
            e.Property(x => x.Nazov).IsRequired();
        });

        model.Entity<Miestnost>(e =>
        {
            e.ToTable("Miestnosti");
            e.Property(x => x.Nazov).IsRequired();
        });

        model.Entity<Osoba>(e =>
        {
            e.ToTable("Osoby");
            e.Property(x => x.Priezvisko).IsRequired();
            e.Ignore(x => x.CeleMeno);
        });

        model.Entity<HistoriaZmeny>(e =>
        {
            e.ToTable("HistoriaZmien");
            e.HasIndex(x => x.PolozkaId);
            e.HasIndex(x => x.Cas);
        });

        model.Entity<Nastavenie>(e =>
        {
            e.ToTable("Nastavenia");
            e.HasKey(x => x.Kluc);
        });

        model.Entity<TypUdalosti>(e =>
        {
            e.ToTable("TypyUdalosti");
            e.Property(x => x.Nazov).IsRequired();
            e.Property(x => x.Farba).IsRequired();
        });

        model.Entity<Udalost>(e =>
        {
            e.ToTable("Udalosti");
            e.HasIndex(x => x.Zaciatok);
            e.HasIndex(x => x.Guid).IsUnique();
            e.Property(x => x.Nazov).IsRequired();
            e.HasOne(x => x.TypUdalosti).WithMany().HasForeignKey(x => x.TypUdalostiId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Polozka).WithMany().HasForeignKey(x => x.PolozkaId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Miestnost).WithMany().HasForeignKey(x => x.MiestnostId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Osoba).WithMany().HasForeignKey(x => x.OsobaId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<Uloha>(e =>
        {
            e.ToTable("Ulohy");
            e.HasIndex(x => x.Guid).IsUnique();
            e.HasIndex(x => x.Hotovo);
            e.Property(x => x.Text).IsRequired();
            e.HasOne(x => x.Polozka).WithMany().HasForeignKey(x => x.PolozkaId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<Poznamka>(e =>
        {
            e.ToTable("Poznamky");
            e.HasIndex(x => x.Guid).IsUnique();
            e.HasIndex(x => x.Datum);
            e.Property(x => x.Nazov).IsRequired();
            e.Property(x => x.Text).IsRequired();
            e.Property(x => x.Farba).IsRequired();
            e.HasOne(x => x.Polozka).WithMany().HasForeignKey(x => x.PolozkaId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Udalost).WithMany().HasForeignKey(x => x.UdalostId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<ZaznamZalohy>(e =>
        {
            e.ToTable("ZaznamyZaloh");
            e.HasIndex(x => x.Cas);
            e.Property(x => x.Typ).IsRequired();
        });
    }
}
