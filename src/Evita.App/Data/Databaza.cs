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
    public const int VerziaSchemy = 6;

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
        (3, "pracovný kalendár", new[]
        {
            """CREATE TABLE IF NOT EXISTS "TypyUdalosti" ("Id" INTEGER NOT NULL CONSTRAINT "PK_TypyUdalosti" PRIMARY KEY AUTOINCREMENT, "Nazov" TEXT NOT NULL, "Farba" TEXT NOT NULL, "Aktivny" INTEGER NOT NULL, "Poradie" INTEGER NOT NULL);""",
            """CREATE TABLE IF NOT EXISTS "Udalosti" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Udalosti" PRIMARY KEY AUTOINCREMENT, "Guid" TEXT NOT NULL, "Nazov" TEXT NOT NULL, "Popis" TEXT NULL, "TypUdalostiId" INTEGER NOT NULL, "Druh" INTEGER NOT NULL, "Zaciatok" TEXT NOT NULL, "Koniec" TEXT NULL, "CelyDen" INTEGER NOT NULL, "Opakovanie" INTEGER NOT NULL, "OpakovanieDo" TEXT NULL, "Pripomienka" INTEGER NOT NULL, "PolozkaId" INTEGER NULL, "MiestnostId" INTEGER NULL, "OsobaId" INTEGER NULL, "Stav" INTEGER NOT NULL, "Vytvorene" TEXT NOT NULL, "Zmenene" TEXT NOT NULL, CONSTRAINT "FK_Udalosti_TypyUdalosti_TypUdalostiId" FOREIGN KEY ("TypUdalostiId") REFERENCES "TypyUdalosti" ("Id") ON DELETE RESTRICT, CONSTRAINT "FK_Udalosti_Polozky_PolozkaId" FOREIGN KEY ("PolozkaId") REFERENCES "Polozky" ("Id") ON DELETE SET NULL, CONSTRAINT "FK_Udalosti_Miestnosti_MiestnostId" FOREIGN KEY ("MiestnostId") REFERENCES "Miestnosti" ("Id") ON DELETE SET NULL, CONSTRAINT "FK_Udalosti_Osoby_OsobaId" FOREIGN KEY ("OsobaId") REFERENCES "Osoby" ("Id") ON DELETE SET NULL);""",
            """CREATE INDEX IF NOT EXISTS "IX_Udalosti_Zaciatok" ON "Udalosti" ("Zaciatok");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Udalosti_Guid" ON "Udalosti" ("Guid");""",
            """CREATE INDEX IF NOT EXISTS "IX_Udalosti_TypUdalostiId" ON "Udalosti" ("TypUdalostiId");""",
            """CREATE INDEX IF NOT EXISTS "IX_Udalosti_PolozkaId" ON "Udalosti" ("PolozkaId");""",
            """CREATE INDEX IF NOT EXISTS "IX_Udalosti_MiestnostId" ON "Udalosti" ("MiestnostId");""",
            """CREATE INDEX IF NOT EXISTS "IX_Udalosti_OsobaId" ON "Udalosti" ("OsobaId");""",
        }),
        (4, "úlohy a poznámky", new[]
        {
            """CREATE TABLE IF NOT EXISTS "Ulohy" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Ulohy" PRIMARY KEY AUTOINCREMENT, "Guid" TEXT NOT NULL, "Text" TEXT NOT NULL, "Popis" TEXT NULL, "Termin" TEXT NULL, "Priorita" INTEGER NOT NULL, "Hotovo" INTEGER NOT NULL, "DatumHotovo" TEXT NULL, "Vytvorene" TEXT NOT NULL, "Poradie" INTEGER NOT NULL, "PolozkaId" INTEGER NULL, CONSTRAINT "FK_Ulohy_Polozky_PolozkaId" FOREIGN KEY ("PolozkaId") REFERENCES "Polozky" ("Id") ON DELETE SET NULL);""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Ulohy_Guid" ON "Ulohy" ("Guid");""",
            """CREATE INDEX IF NOT EXISTS "IX_Ulohy_Hotovo" ON "Ulohy" ("Hotovo");""",
            """CREATE INDEX IF NOT EXISTS "IX_Ulohy_PolozkaId" ON "Ulohy" ("PolozkaId");""",
            """CREATE TABLE IF NOT EXISTS "Poznamky" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Poznamky" PRIMARY KEY AUTOINCREMENT, "Guid" TEXT NOT NULL, "Nazov" TEXT NOT NULL, "Text" TEXT NOT NULL, "Farba" TEXT NOT NULL, "Pripnuta" INTEGER NOT NULL, "Vytvorene" TEXT NOT NULL, "Zmenene" TEXT NOT NULL, "Datum" TEXT NULL, "PolozkaId" INTEGER NULL, "UdalostId" INTEGER NULL, CONSTRAINT "FK_Poznamky_Polozky_PolozkaId" FOREIGN KEY ("PolozkaId") REFERENCES "Polozky" ("Id") ON DELETE SET NULL, CONSTRAINT "FK_Poznamky_Udalosti_UdalostId" FOREIGN KEY ("UdalostId") REFERENCES "Udalosti" ("Id") ON DELETE SET NULL);""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Poznamky_Guid" ON "Poznamky" ("Guid");""",
            """CREATE INDEX IF NOT EXISTS "IX_Poznamky_Datum" ON "Poznamky" ("Datum");""",
            """CREATE INDEX IF NOT EXISTS "IX_Poznamky_PolozkaId" ON "Poznamky" ("PolozkaId");""",
            """CREATE INDEX IF NOT EXISTS "IX_Poznamky_UdalostId" ON "Poznamky" ("UdalostId");""",
        }),
        (5, "skenovanie a inventúra", new[]
        {
            // pridanie stĺpca sa pri opakovanom spustení preskočí (SQLite nemá ADD COLUMN IF NOT EXISTS)
            """ALTER TABLE "Polozky" ADD COLUMN "Ean" TEXT NULL;""",
            """CREATE INDEX IF NOT EXISTS "IX_Polozky_Ean" ON "Polozky" ("Ean");""",
            """CREATE INDEX IF NOT EXISTS "IX_Polozky_InventarneCislo" ON "Polozky" ("InventarneCislo");""",
            """CREATE TABLE IF NOT EXISTS "Inventury" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Inventury" PRIMARY KEY AUTOINCREMENT, "Guid" TEXT NOT NULL, "Nazov" TEXT NOT NULL, "Zaciatok" TEXT NOT NULL, "Koniec" TEXT NULL, "Stav" INTEGER NOT NULL, "Poznamka" TEXT NULL, "AktualnaMiestnostId" INTEGER NULL);""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Inventury_Guid" ON "Inventury" ("Guid");""",
            """CREATE TABLE IF NOT EXISTS "InventuraZaznamy" ("Id" INTEGER NOT NULL CONSTRAINT "PK_InventuraZaznamy" PRIMARY KEY AUTOINCREMENT, "InventuraId" INTEGER NOT NULL, "PolozkaId" INTEGER NULL, "Kod" TEXT NOT NULL, "MiestnostId" INTEGER NULL, "EvidovanaMiestnostId" INTEGER NULL, "Cas" TEXT NOT NULL, "Sposob" TEXT NOT NULL, "Pouzivatel" TEXT NOT NULL, CONSTRAINT "FK_InventuraZaznamy_Inventury_InventuraId" FOREIGN KEY ("InventuraId") REFERENCES "Inventury" ("Id") ON DELETE CASCADE, CONSTRAINT "FK_InventuraZaznamy_Polozky_PolozkaId" FOREIGN KEY ("PolozkaId") REFERENCES "Polozky" ("Id") ON DELETE SET NULL);""",
            """CREATE INDEX IF NOT EXISTS "IX_InventuraZaznamy_InventuraId" ON "InventuraZaznamy" ("InventuraId");""",
            """CREATE INDEX IF NOT EXISTS "IX_InventuraZaznamy_PolozkaId" ON "InventuraZaznamy" ("PolozkaId");""",
        }),
        (6, "ikony kategórií", new[]
        {
            """ALTER TABLE "Kategorie" ADD COLUMN "Ikona" TEXT NULL;""",
            """ALTER TABLE "Kategorie" ADD COLUMN "Farba" TEXT NULL;""",
        }),
    };

    public const string KlucVerziaSchemy = "Schema.Verzia";

    /// <summary>Jazyk, v ktorom sú v databáze počiatočné kategórie a typy udalostí (staršie databázy: slovenčina).</summary>
    public const string KlucJazykNazvov = "Schema.JazykNazvov";
    public const string KlucPoslednePrirastkove = "PrirastkoveCislo.Posledne";
    public const string KlucUkazkoveData = "UkazkoveData";

    /// <summary>Počiatočné typy udalostí kalendára s farbami (dajú sa upraviť a doplniť). Do databázy sa zapíšu v jazyku programu.</summary>
    public static readonly (string Nazov, string Farba)[] PociatocneTypyUdalosti =
    {
        (Preklad.N("servis"), "#E67E22"), (Preklad.N("revízia"), "#C0392B"), (Preklad.N("nákup"), "#27AE60"), (Preklad.N("inventúra"), "#8E44AD"),
        (Preklad.N("porada"), "#2E86C1"), (Preklad.N("školenie"), "#16A085"), (Preklad.N("obdobie"), "#D4AC0D"), (Preklad.N("iné"), "#7F8C8D"),
    };

    /// <summary>Počiatočný zoznam kategórií (zobrazuje sa vždy abecedne). Do databázy sa zapíšu v jazyku programu.</summary>
    public static readonly string[] PociatocneKategorie =
    {
        Preklad.N("All-in-One"), Preklad.N("interaktívna tabuľa"), Preklad.N("kopírka"), Preklad.N("multiboard"), Preklad.N("multifunkcia"),
        Preklad.N("notebook"), Preklad.N("PC"), Preklad.N("príslušenstvo"), Preklad.N("projektor"), Preklad.N("router"), Preklad.N("skener"),
        Preklad.N("slúchadlá"), Preklad.N("switch"), Preklad.N("tablet"), Preklad.N("tlačiareň"),
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
                // názvy v jazyku programu (pri prvom spustení angličtina)
                foreach (var nazov in PociatocneKategorie)
                {
                    db.Kategorie.Add(new Kategoria { Nazov = Preklad.TAkJe(nazov) });
                }
            }

            if (nova)
            {
                Zapis(db, KlucVerziaSchemy, VerziaSchemy.ToString());
                Zapis(db, KlucJazykNazvov, Preklad.Aktualny.Kod);
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

        DoplnTypyUdalosti();
        PrelozPociatocneNazvy();
    }

    /// <summary>
    /// Po zmene jazyka programu premenuje počiatočné kategórie a typy udalostí do nového jazyka – len tie,
    /// ktoré majú stále pôvodný názov (vlastné a premenované zostávajú, ako sú).
    /// </summary>
    public static void PrelozPociatocneNazvy()
    {
        using var db = Otvor();
        var povodny = Citaj(db, KlucJazykNazvov) ?? "sk";
        var novy = Preklad.Aktualny.Kod;
        if (povodny == novy)
        {
            return;
        }

        var slovnikPovodny = povodny == "sk" ? null : Preklad.NacitajSlovnik(povodny);
        var slovnikNovy = novy == "sk" ? null : Preklad.NacitajSlovnik(novy);
        static string V(Dictionary<string, string>? slovnik, string kluc) =>
            slovnik is not null && slovnik.TryGetValue(kluc, out var preklad) ? preklad : kluc;

        var kategorie = db.Kategorie.ToList();
        foreach (var kluc in PociatocneKategorie)
        {
            var z = V(slovnikPovodny, kluc);
            var na = V(slovnikNovy, kluc);
            if (z != na && kategorie.FirstOrDefault(k => k.Nazov == z) is { } kategoria && kategorie.All(k => k.Nazov != na))
            {
                kategoria.Nazov = na;
            }
        }

        var typy = db.TypyUdalosti.ToList();
        foreach (var (kluc, _) in PociatocneTypyUdalosti)
        {
            var z = V(slovnikPovodny, kluc);
            var na = V(slovnikNovy, kluc);
            if (z != na && typy.FirstOrDefault(t => t.Nazov == z) is { } typ && typy.All(t => t.Nazov != na))
            {
                typ.Nazov = na;
            }
        }

        Zapis(db, KlucJazykNazvov, novy);
        db.SaveChanges();
    }

    /// <summary>Ak v databáze nie sú žiadne typy udalostí, doplní počiatočné.</summary>
    public static void DoplnTypyUdalosti()
    {
        using var db = Otvor();
        if (db.TypyUdalosti.Any())
        {
            return;
        }

        var poradie = 0;
        foreach (var (nazov, farba) in PociatocneTypyUdalosti)
        {
            db.TypyUdalosti.Add(new TypUdalosti { Nazov = Preklad.TAkJe(nazov), Farba = farba, Poradie = poradie++ });
        }

        db.SaveChanges();
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
                Preklad.F($"Databáza pochádza z novšej verzie programu EviTA (verzia štruktúry {verzia}). Nainštalujte najnovšiu verziu programu."));
        }

        if (verzia == VerziaSchemy)
        {
            return;
        }

        // bezpečnostná kópia pred úpravou štruktúry
        var kopia = ZalohaService.CislovanaCesta(PriecinokZaloh, $"EviTA_pred_upravou_struktury_v{verzia}_{DateTime.Now:yyyy-MM-dd_HHmmss}", ".db");

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
                if (PridavaExistujuciStlpec(spojenie, transakcia, sql))
                {
                    continue;
                }

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

    /// <summary>Príkaz ALTER TABLE … ADD COLUMN pre stĺpec, ktorý už v tabuľke je (napr. po čiastočnej oprave databázy).</summary>
    private static bool PridavaExistujuciStlpec(SqliteConnection spojenie, SqliteTransaction transakcia, string sql)
    {
        var zhoda = System.Text.RegularExpressions.Regex.Match(sql, "^ALTER TABLE \"(\\w+)\" ADD COLUMN \"(\\w+)\"");
        if (!zhoda.Success)
        {
            return false;
        }

        using var prikaz = spojenie.CreateCommand();
        prikaz.Transaction = transakcia;
        prikaz.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{zhoda.Groups[1].Value}') WHERE name = $stlpec;";
        prikaz.Parameters.AddWithValue("$stlpec", zhoda.Groups[2].Value);
        return Convert.ToInt64(prikaz.ExecuteScalar()) > 0;
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
