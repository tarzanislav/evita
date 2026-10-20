using System.IO;
using Evita.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Evita.Data;

/// <summary>Umiestnenie databázy, jej vytvorenie a pomocné nastavenia.</summary>
public static class Databaza
{
    public const int VerziaSchemy = 1;

    public const string KlucVerziaSchemy = "Schema.Verzia";
    public const string KlucPoslednePrirastkove = "PrirastkoveCislo.Posledne";
    public const string KlucUkazkoveData = "UkazkoveData";

    /// <summary>Počiatočný zoznam kategórií (zobrazuje sa vždy abecedne).</summary>
    public static readonly string[] PociatocneKategorie =
    {
        "All-in-One", "interaktívna tabuľa", "kopírka", "multiboard", "multifunkcia",
        "notebook", "PC", "príslušenstvo", "projektor", "router", "skener",
        "slúchadlá", "switch", "tablet", "tlačiareň",
    };

    public static string Priecinok =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EviTA", "Data");

    public static string Cesta => Path.Combine(Priecinok, "evita.db");

    public static string PriecinokZaloh =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EviTA", "Zálohy");

    public static bool Existuje => File.Exists(Cesta);

    public static EvitaDbContext Otvor() => new(Cesta);

    public static SqliteConnection OtvorSpojenie()
    {
        var spojenie = new SqliteConnection($"Data Source={Cesta}");
        spojenie.Open();
        return spojenie;
    }

    /// <summary>Vytvorí databázu, ak neexistuje, a doplní základné údaje.</summary>
    public static void Inicializuj()
    {
        Directory.CreateDirectory(Priecinok);

        using (var db = Otvor())
        {
            db.Database.EnsureCreated();

            if (!db.Kategorie.Any())
            {
                foreach (var nazov in PociatocneKategorie)
                {
                    db.Kategorie.Add(new Kategoria { Nazov = nazov });
                }
            }

            if (Citaj(db, KlucVerziaSchemy) is null)
            {
                Zapis(db, KlucVerziaSchemy, VerziaSchemy.ToString());
            }

            db.SaveChanges();
        }

        using var spojenie = OtvorSpojenie();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "PRAGMA journal_mode=WAL;";
        prikaz.ExecuteNonQuery();
    }

    public static string? Citaj(EvitaDbContext db, string kluc) =>
        db.Nastavenia.Where(n => n.Kluc == kluc).Select(n => n.Hodnota).FirstOrDefault();

    public static void Zapis(EvitaDbContext db, string kluc, string? hodnota)
    {
        var existujuce = db.Nastavenia.Find(kluc);
        if (existujuce is null)
        {
            db.Nastavenia.Add(new Nastavenie { Kluc = kluc, Hodnota = hodnota });
        }
        else
        {
            existujuce.Hodnota = hodnota;
        }
    }

    /// <summary>Ďalšie voľné prírastkové číslo. Čísla sa nepoužijú znova ani po vymazaní položky.</summary>
    public static int DalsiePrirastkoveCislo(EvitaDbContext db)
    {
        var posledne = int.TryParse(Citaj(db, KlucPoslednePrirastkove), out var p) ? p : 0;
        var najvacsie = db.Polozky.Max(x => (int?)x.PrirastkoveCislo) ?? 0;
        return Math.Max(posledne, najvacsie) + 1;
    }

    public static bool SuUkazkoveData()
    {
        if (!Existuje)
        {
            return false;
        }

        using var db = Otvor();
        return Citaj(db, KlucUkazkoveData) == "1";
    }
}
