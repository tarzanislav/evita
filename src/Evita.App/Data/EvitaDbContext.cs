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

        model.Entity<ZaznamZalohy>(e =>
        {
            e.ToTable("ZaznamyZaloh");
            e.HasIndex(x => x.Cas);
            e.Property(x => x.Typ).IsRequired();
        });
    }
}
