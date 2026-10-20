using System.IO;
using Evita.Models;
using Evita.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Evita.Data;

/// <summary>Umiestnenie databázy, jej vytvorenie a pomocné nastavenia.</summary>
public static class Databaza
{
    /// <summary>Aktuálna verzia dátovej štruktúry. Pri každej zmene štruktúry sa zvýši a pribudne krok v <see cref="Migracie"/>.</summary>
    public const int VerziaSchemy = 2;

    /// <summary>
    /// Kroky, ktoré upravia staršiu databázu na novú verziu štruktúry. Každý príkaz musí byť bezpečný aj pri
    /// opakovanom spustení (IF NOT EXISTS), lebo rovnaké kroky sa používajú aj pri záchrane dát.
    /// </summary>
    private static readonly (int Verzia, string Popis, string[] Prikazy)[] Migracie =
    {
        (2, "záznam záloh", new[]
        {
            """CREATE TABLE IF NOT EXISTS "ZaznamyZaloh" ("Id" INTEGER NOT NULL CONSTRAINT "PK_ZaznamyZaloh" PRIMARY KEY AUTOINCREMENT, "Cas" TEXT NOT NULL, "Typ" TEXT NOT NULL, "Cesta" TEXT NULL, "Velkost" INTEGER NOT NULL, "Uspech" INTEGER NOT NULL, "Zasifrovana" INTEGER NOT NULL, "Chyba" TEXT NULL);""",
            """CREATE INDEX IF NOT EXISTS "IX_ZaznamyZaloh_Cas" ON "ZaznamyZaloh" ("Cas");""",
        }),
    };

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

    /// <summary>Iný priečinok s dátami (na testovanie). Bežne nie je nastavený.</summary>
    private static string? PrepisPriecinka => Environment.GetEnvironmentVariable("EVITA_DATA_DIR") is { Length: > 0 } p ? p : null;

    public static string Priecinok =>
        PrepisPriecinka ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EviTA", "Data");

    public static string Cesta => Path.Combine(Priecinok, "evita.db");

    public static string PriecinokZaloh =>
        PrepisPriecinka is { } prepis
            ? Path.Combine(prepis, "Zalohy")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EviTA", "Zálohy");

    public static bool Existuje => File.Exists(Cesta);

    public static EvitaDbContext Otvor() => new(Cesta);

    public static SqliteConnection OtvorSpojenie()
    {
        var spojenie = new SqliteConnection($"Data Source={Cesta}");
        spojenie.Open();
        return spojenie;
    }

    /// <summary>
    /// Vytvorí databázu, ak neexistuje, doplní základné údaje a staršiu databázu
    /// upraví na aktuálnu verziu štruktúry (pred úpravou ju zálohuje).
    /// </summary>
    public static void Inicializuj()
    {
        Directory.CreateDirectory(Priecinok);
        bool nova;

        using (var db = Otvor())
        {
            nova = db.Database.EnsureCreated();

            if (!db.Kategorie.Any())
            {
                foreach (var nazov in PociatocneKategorie)
                {
                    db.Kategorie.Add(new Kategoria { Nazov = nazov });
                }
            }

            if (nova)
            {
                Zapis(db, KlucVerziaSchemy, VerziaSchemy.ToString());
            }

            db.SaveChanges();
        }

        using (var spojenie = OtvorSpojenie())
        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.CommandText = "PRAGMA journal_mode=WAL;";
            prikaz.ExecuteNonQuery();
        }

        if (!nova)
        {
            Aktualizuj();
        }
    }

    /// <summary>Verzia štruktúry uložená v databáze (staršie databázy bez záznamu majú verziu 1).</summary>
    public static int VerziaVDatabaze()
    {
        using var db = Otvor();
        return int.TryParse(Citaj(db, KlucVerziaSchemy), out var v) ? v : 1;
    }

    private static void Aktualizuj()
    {
        var verzia = VerziaVDatabaze();
        if (verzia > VerziaSchemy)
        {
            throw new EvitaException(
                $"Databáza pochádza z novšej verzie programu EviTA (verzia štruktúry {verzia}). Nainštalujte najnovšiu verziu programu.");
        }

        if (verzia == VerziaSchemy)
        {
            return;
        }

        // bezpečnostná kópia pred úpravou štruktúry
        Directory.CreateDirectory(PriecinokZaloh);
        var kopia = Path.Combine(PriecinokZaloh, $"EviTA_pred_aktualizaciou_v{verzia}_{DateTime.Now:yyyy-MM-dd_HHmmss}.db");

        using var spojenie = OtvorSpojenie();
        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.CommandText = "VACUUM INTO $cesta;";
            prikaz.Parameters.AddWithValue("$cesta", kopia);
            prikaz.ExecuteNonQuery();
        }

        using var transakcia = spojenie.BeginTransaction();
        foreach (var krok in Migracie.Where(m => m.Verzia > verzia).OrderBy(m => m.Verzia))
        {
            foreach (var sql in krok.Prikazy)
            {
                using var prikaz = spojenie.CreateCommand();
                prikaz.Transaction = transakcia;
                prikaz.CommandText = sql;
                prikaz.ExecuteNonQuery();
            }
        }

        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.Transaction = transakcia;
            prikaz.CommandText = "INSERT OR REPLACE INTO \"Nastavenia\" (\"Kluc\", \"Hodnota\") VALUES ($kluc, $hodnota);";
            prikaz.Parameters.AddWithValue("$kluc", KlucVerziaSchemy);
            prikaz.Parameters.AddWithValue("$hodnota", VerziaSchemy.ToString());
            prikaz.ExecuteNonQuery();
        }

        transakcia.Commit();
    }

    /// <summary>Všetky príkazy migrácií (na záchranu dát – vytvoria chýbajúce tabuľky).</summary>
    public static IEnumerable<string> VsetkyPrikazyMigracii() => Migracie.SelectMany(m => m.Prikazy);

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
