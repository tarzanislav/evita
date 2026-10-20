using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Kalendarium;

/// <summary>Druh sviatku podľa zákona č. 241/1993 Z. z.</summary>
public enum DruhSviatku
{
    /// <summary>Štátny sviatok (§ 1).</summary>
    StatnySviatok,

    /// <summary>Ďalší deň pracovného pokoja (§ 2 ods. 2).</summary>
    DenPracovnehoPokoja,

    /// <summary>Pamätný deň (§ 3).</summary>
    PamatnyDen,
}

/// <summary>Sviatok v konkrétnom roku.</summary>
public sealed record SviatokDna(int Id, DateOnly Datum, string Nazov, DruhSviatku Druh, bool DenPracovnehoPokoja, bool ZmenenePouzivatelom)
{
    public string DruhText => Druh switch
    {
        DruhSviatku.StatnySviatok => "štátny sviatok",
        DruhSviatku.DenPracovnehoPokoja => "sviatok",
        _ => "pamätný deň",
    };
}

/// <summary>
/// Kalendárium – samostatná lokálna databáza (SQLite) spoločná pre všetky naše programy na počítači.
/// Obsahuje meniny a sviatky; každý záznam má jedinečný identifikátor, pôvod a zdroj.
/// </summary>
public sealed class KalendariumDb
{
    public const int VerziaSchemy = 1;
    private const string ZdrojMenin = "Kalendár mien SR (údaje name-day-calendar, licencia MIT)";
    private const string ZdrojSviatkov = "zákon č. 241/1993 Z. z. o štátnych sviatkoch, dňoch pracovného pokoja a pamätných dňoch";

    private static readonly Lazy<KalendariumDb> Predvolena = new(() => new KalendariumDb());

    public KalendariumDb(string? cesta = null)
    {
        Cesta = cesta ?? Path.Combine(PredvolenyPriecinok, "kalendarium.db");
    }

    /// <summary>Spoločná inštancia pre celý program.</summary>
    public static KalendariumDb Spolocna => Predvolena.Value;

    /// <summary>C:\ProgramData\Kalendarium (na testovanie sa dá zmeniť premennou KALENDARIUM_DIR).</summary>
    public static string PredvolenyPriecinok =>
        Environment.GetEnvironmentVariable("KALENDARIUM_DIR") is { Length: > 0 } prepis
            ? prepis
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Kalendarium");

    public string Cesta { get; }

    public bool Existuje => File.Exists(Cesta);

    public SqliteConnection OtvorSpojenie()
    {
        var spojenie = new SqliteConnection($"Data Source={Cesta}");
        spojenie.Open();
        return spojenie;
    }

    // ------------------------------------------------------------------ vytvorenie a základné dáta

    /// <summary>Vytvorí databázu, ak neexistuje, a doplní zabudované meniny a sviatky.</summary>
    public void Inicializuj()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Cesta)!);
        using var spojenie = OtvorSpojenie();
        Vykonaj(spojenie, "PRAGMA journal_mode=WAL;");
        Vykonaj(spojenie, """
            CREATE TABLE IF NOT EXISTS Info (Kluc TEXT NOT NULL PRIMARY KEY, Hodnota TEXT);
            CREATE TABLE IF NOT EXISTS Meniny (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Guid TEXT NOT NULL UNIQUE,
                Krajina TEXT NOT NULL,
                Mesiac INTEGER NOT NULL,
                Den INTEGER NOT NULL,
                Meno TEXT NOT NULL,
                Poradie INTEGER NOT NULL,
                Povod TEXT NOT NULL,
                Zdroj TEXT,
                Skryte INTEGER NOT NULL DEFAULT 0,
                Pridane TEXT NOT NULL,
                Zmenene TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_Meniny_Datum ON Meniny (Krajina, Mesiac, Den);
            CREATE TABLE IF NOT EXISTS Sviatky (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Guid TEXT NOT NULL UNIQUE,
                Krajina TEXT NOT NULL,
                Nazov TEXT NOT NULL,
                Druh TEXT NOT NULL,
                Mesiac INTEGER,
                Den INTEGER,
                Pohyblivy TEXT,
                DenPracovnehoPokoja INTEGER NOT NULL,
                PracovnyPokojOdRoku INTEGER,
                PracovnyPokojDoRoku INTEGER,
                Povod TEXT NOT NULL,
                Zdroj TEXT,
                Skryte INTEGER NOT NULL DEFAULT 0,
                Pridane TEXT NOT NULL,
                Zmenene TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS VynimkySviatkov (
                SviatokId INTEGER NOT NULL,
                Rok INTEGER NOT NULL,
                DenPracovnehoPokoja INTEGER NOT NULL,
                Povod TEXT NOT NULL,
                PRIMARY KEY (SviatokId, Rok));
            """);

        using var transakcia = spojenie.BeginTransaction();
        if (Pocet(spojenie, transakcia, "Meniny") == 0)
        {
            NaplnMeniny(spojenie, transakcia);
        }

        if (Pocet(spojenie, transakcia, "Sviatky") == 0)
        {
            NaplnSviatky(spojenie, transakcia);
        }

        Vykonaj(spojenie, "INSERT OR REPLACE INTO Info (Kluc, Hodnota) VALUES ('VerziaSchemy', $v);", transakcia, ("$v", VerziaSchemy.ToString()));
        transakcia.Commit();
    }

    private static void NaplnMeniny(SqliteConnection spojenie, SqliteTransaction transakcia)
    {
        using var zdroj = typeof(KalendariumDb).Assembly.GetManifestResourceStream("Kalendarium.meniny_sk.json")
            ?? throw new InvalidOperationException("Chýbajú zabudované meniny.");
        var dni = JsonSerializer.Deserialize<Dictionary<string, string[]>>(zdroj) ?? new();
        var teraz = Teraz();

        foreach (var (datum, mena) in dni)
        {
            var mesiac = int.Parse(datum[..2], CultureInfo.InvariantCulture);
            var den = int.Parse(datum[3..], CultureInfo.InvariantCulture);
            for (var i = 0; i < mena.Length; i++)
            {
                Vykonaj(spojenie, """
                    INSERT OR IGNORE INTO Meniny (Guid, Krajina, Mesiac, Den, Meno, Poradie, Povod, Zdroj, Pridane, Zmenene)
                    VALUES ($guid, 'SK', $m, $d, $meno, $poradie, 'zabudovane', $zdroj, $cas, $cas);
                    """, transakcia,
                    ("$guid", StalyGuid($"SK|meniny|{datum}|{mena[i]}")), ("$m", mesiac), ("$d", den),
                    ("$meno", mena[i]), ("$poradie", i), ("$zdroj", ZdrojMenin), ("$cas", teraz));
            }
        }
    }

    private static void NaplnSviatky(SqliteConnection spojenie, SqliteTransaction transakcia)
    {
        var teraz = Teraz();
        // (kľúč, názov, druh, mesiac, deň, pohyblivý, deň pracovného pokoja, pokoj od roku, pokoj do roku)
        var sviatky = new (string Kluc, string Nazov, DruhSviatku Druh, int? M, int? D, string? Pohyblivy, bool Pokoj, int? Od, int? Do)[]
        {
            ("01-01", "Deň vzniku Slovenskej republiky", DruhSviatku.StatnySviatok, 1, 1, null, true, null, null),
            ("07-05", "Sviatok svätého Cyrila a svätého Metoda", DruhSviatku.StatnySviatok, 7, 5, null, true, null, null),
            ("08-29", "Výročie Slovenského národného povstania", DruhSviatku.StatnySviatok, 8, 29, null, true, null, null),
            ("09-01", "Deň Ústavy Slovenskej republiky", DruhSviatku.StatnySviatok, 9, 1, null, true, null, 2023),
            ("10-28", "Deň vzniku samostatného česko-slovenského štátu", DruhSviatku.StatnySviatok, 10, 28, null, false, null, null),
            ("11-17", "Deň boja za slobodu a demokraciu", DruhSviatku.StatnySviatok, 11, 17, null, true, null, 2025),
            ("01-06", "Zjavenie Pána (Traja králi)", DruhSviatku.DenPracovnehoPokoja, 1, 6, null, true, null, null),
            ("velky-piatok", "Veľký piatok", DruhSviatku.DenPracovnehoPokoja, null, null, "VelkyPiatok", true, null, null),
            ("velkonocny-pondelok", "Veľkonočný pondelok", DruhSviatku.DenPracovnehoPokoja, null, null, "VelkonocnyPondelok", true, null, null),
            ("05-01", "Sviatok práce", DruhSviatku.DenPracovnehoPokoja, 5, 1, null, true, null, null),
            ("05-08", "Deň víťazstva nad fašizmom", DruhSviatku.DenPracovnehoPokoja, 5, 8, null, true, null, null),
            ("09-15", "Sedembolestná Panna Mária", DruhSviatku.DenPracovnehoPokoja, 9, 15, null, true, null, null),
            ("11-01", "Sviatok Všetkých svätých", DruhSviatku.DenPracovnehoPokoja, 11, 1, null, true, null, null),
            ("12-24", "Štedrý deň", DruhSviatku.DenPracovnehoPokoja, 12, 24, null, true, null, null),
            ("12-25", "Prvý sviatok vianočný", DruhSviatku.DenPracovnehoPokoja, 12, 25, null, true, null, null),
            ("12-26", "Druhý sviatok vianočný", DruhSviatku.DenPracovnehoPokoja, 12, 26, null, true, null, null),
            // pamätné dni (§ 3)
            ("p-03-06", "Deň obetí pandémie COVID-19", DruhSviatku.PamatnyDen, 3, 6, null, false, null, null),
            ("p-03-25", "Deň zápasu za ľudské práva", DruhSviatku.PamatnyDen, 3, 25, null, false, null, null),
            ("p-04-13", "Deň nespravodlivo stíhaných", DruhSviatku.PamatnyDen, 4, 13, null, false, null, null),
            ("p-05-01", "Deň pristúpenia Slovenskej republiky k Európskej únii", DruhSviatku.PamatnyDen, 5, 1, null, false, null, null),
            ("p-05-04", "Výročie úmrtia M. R. Štefánika", DruhSviatku.PamatnyDen, 5, 4, null, false, null, null),
            ("p-06-07", "Výročie Memoranda národa slovenského", DruhSviatku.PamatnyDen, 6, 7, null, false, null, null),
            ("p-06-21", "Deň odchodu okupačných vojsk sovietskej armády z Česko-Slovenska v roku 1991", DruhSviatku.PamatnyDen, 6, 21, null, false, null, null),
            ("p-06-24", "Deň pamiatky obetí komunistického režimu", DruhSviatku.PamatnyDen, 6, 24, null, false, null, null),
            ("p-07-05", "Deň Slovákov žijúcich v zahraničí", DruhSviatku.PamatnyDen, 7, 5, null, false, null, null),
            ("p-07-17", "Výročie Deklarácie o zvrchovanosti Slovenskej republiky", DruhSviatku.PamatnyDen, 7, 17, null, false, null, null),
            ("p-08-04", "Deň Matice slovenskej", DruhSviatku.PamatnyDen, 8, 4, null, false, null, null),
            ("p-08-10", "Deň obetí banských nešťastí", DruhSviatku.PamatnyDen, 8, 10, null, false, null, null),
            ("p-08-21", "Deň obetí okupácie Česko-Slovenska v roku 1968", DruhSviatku.PamatnyDen, 8, 21, null, false, null, null),
            ("p-09-09", "Deň obetí holokaustu a rasového násilia", DruhSviatku.PamatnyDen, 9, 9, null, false, null, null),
            ("p-09-19", "Deň vzniku Slovenskej národnej rady", DruhSviatku.PamatnyDen, 9, 19, null, false, null, null),
            ("p-09-22", "Deň boja proti nenávistným prejavom na deťoch", DruhSviatku.PamatnyDen, 9, 22, null, false, null, null),
            ("p-10-06", "Deň hrdinov Karpatsko-duklianskej operácie", DruhSviatku.PamatnyDen, 10, 6, null, false, null, null),
            ("p-10-12", "Deň samizdatu", DruhSviatku.PamatnyDen, 10, 12, null, false, null, null),
            ("p-10-27", "Deň černovskej tragédie", DruhSviatku.PamatnyDen, 10, 27, null, false, null, null),
            ("p-10-29", "Deň narodenia Ľudovíta Štúra", DruhSviatku.PamatnyDen, 10, 29, null, false, null, null),
            ("p-10-30", "Výročie Deklarácie slovenského národa", DruhSviatku.PamatnyDen, 10, 30, null, false, null, null),
            ("p-10-31", "Deň reformácie", DruhSviatku.PamatnyDen, 10, 31, null, false, null, null),
            ("p-12-30", "Deň vyhlásenia Slovenska za samostatnú cirkevnú provinciu", DruhSviatku.PamatnyDen, 12, 30, null, false, null, null),
        };

        foreach (var s in sviatky)
        {
            Vykonaj(spojenie, """
                INSERT OR IGNORE INTO Sviatky (Guid, Krajina, Nazov, Druh, Mesiac, Den, Pohyblivy, DenPracovnehoPokoja,
                    PracovnyPokojOdRoku, PracovnyPokojDoRoku, Povod, Zdroj, Pridane, Zmenene)
                VALUES ($guid, 'SK', $nazov, $druh, $m, $d, $pohyblivy, $pokoj, $od, $do, 'zabudovane', $zdroj, $cas, $cas);
                """, transakcia,
                ("$guid", StalyGuid($"SK|sviatok|{s.Kluc}")), ("$nazov", s.Nazov), ("$druh", s.Druh.ToString()),
                ("$m", s.M), ("$d", s.D), ("$pohyblivy", s.Pohyblivy), ("$pokoj", s.Pokoj ? 1 : 0),
                ("$od", s.Od), ("$do", s.Do), ("$zdroj", ZdrojSviatkov), ("$cas", teraz));
        }

        // § 4b: v roku 2026 nie sú 8. máj a 15. september dňami pracovného pokoja
        foreach (var kluc in new[] { "05-08", "09-15" })
        {
            Vykonaj(spojenie, """
                INSERT OR IGNORE INTO VynimkySviatkov (SviatokId, Rok, DenPracovnehoPokoja, Povod)
                SELECT Id, 2026, 0, 'zakon' FROM Sviatky WHERE Guid = $guid;
                """, transakcia, ("$guid", StalyGuid($"SK|sviatok|{kluc}")));
        }
    }

    // ------------------------------------------------------------------ meniny

    /// <summary>Mená, ktoré majú v daný deň meniny (29. februára len v priestupnom roku).</summary>
    public IReadOnlyList<string> Meniny(DateOnly datum)
    {
        using var spojenie = OtvorSpojenie();
        return Mena(spojenie, datum.Month, datum.Day);
    }

    /// <summary>Počet mien v kalendári.</summary>
    public int PocetMien()
    {
        using var spojenie = OtvorSpojenie();
        return Pocet(spojenie, null, "Meniny");
    }

    private static List<string> Mena(SqliteConnection spojenie, int mesiac, int den)
    {
        var mena = new List<string>();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "SELECT Meno FROM Meniny WHERE Krajina = 'SK' AND Mesiac = $m AND Den = $d AND Skryte = 0 ORDER BY Poradie, Id;";
        prikaz.Parameters.AddWithValue("$m", mesiac);
        prikaz.Parameters.AddWithValue("$d", den);
        using var citac = prikaz.ExecuteReader();
        while (citac.Read())
        {
            mena.Add(citac.GetString(0));
        }

        return mena;
    }

    // ------------------------------------------------------------------ sviatky

    /// <summary>Všetky sviatky v danom roku zoradené podľa dátumu (aj pamätné dni).</summary>
    public IReadOnlyList<SviatokDna> Sviatky(int rok)
    {
        var vysledok = new List<SviatokDna>();
        using var spojenie = OtvorSpojenie();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = """
            SELECT s.Id, s.Nazov, s.Druh, s.Mesiac, s.Den, s.Pohyblivy, s.DenPracovnehoPokoja,
                   s.PracovnyPokojOdRoku, s.PracovnyPokojDoRoku, v.DenPracovnehoPokoja, v.Povod
            FROM Sviatky s
            LEFT JOIN VynimkySviatkov v ON v.SviatokId = s.Id AND v.Rok = $rok
            WHERE s.Krajina = 'SK' AND s.Skryte = 0;
            """;
        prikaz.Parameters.AddWithValue("$rok", rok);
        using var citac = prikaz.ExecuteReader();
        var velkaNoc = VelkonocnaNedela(rok);

        while (citac.Read())
        {
            DateOnly datum;
            if (!citac.IsDBNull(5))
            {
                datum = citac.GetString(5) switch
                {
                    "VelkyPiatok" => velkaNoc.AddDays(-2),
                    "VelkonocnyPondelok" => velkaNoc.AddDays(1),
                    _ => velkaNoc,
                };
            }
            else
            {
                var mesiac = citac.GetInt32(3);
                var den = citac.GetInt32(4);
                if (mesiac == 2 && den == 29 && !DateTime.IsLeapYear(rok))
                {
                    continue;
                }

                datum = new DateOnly(rok, mesiac, den);
            }

            var pokoj = citac.GetInt32(6) == 1
                        && (citac.IsDBNull(7) || rok >= citac.GetInt32(7))
                        && (citac.IsDBNull(8) || rok <= citac.GetInt32(8));
            var zmenene = false;
            if (!citac.IsDBNull(9))
            {
                pokoj = citac.GetInt32(9) == 1;
                zmenene = citac.GetString(10) == "pouzivatel";
            }

            vysledok.Add(new SviatokDna(citac.GetInt32(0), datum, citac.GetString(1),
                Enum.Parse<DruhSviatku>(citac.GetString(2)), pokoj, zmenene));
        }

        return vysledok.OrderBy(s => s.Datum).ThenBy(s => s.Druh).ToList();
    }

    public IReadOnlyList<SviatokDna> SviatkyDna(DateOnly datum) =>
        Sviatky(datum.Year).Where(s => s.Datum == datum).ToList();

    /// <summary>Je v daný deň sviatok, ktorý je dňom pracovného pokoja? (Víkendy sem nepatria.)</summary>
    public bool JeSviatokVolno(DateOnly datum) => SviatkyDna(datum).Any(s => s.DenPracovnehoPokoja);

    /// <summary>Najbližší sviatok, ktorý je dňom pracovného pokoja, odo dňa <paramref name="od"/> (vrátane).</summary>
    public SviatokDna? NajblizsiDenPracovnehoPokoja(DateOnly od) =>
        Sviatky(od.Year).Concat(Sviatky(od.Year + 1))
            .Where(s => s.DenPracovnehoPokoja && s.Datum >= od)
            .OrderBy(s => s.Datum)
            .FirstOrDefault();

    /// <summary>Zmení, či je sviatok v danom roku dňom pracovného pokoja (zákon sa mení).</summary>
    public void NastavDenPracovnehoPokoja(int sviatokId, int rok, bool denPracovnehoPokoja)
    {
        using var spojenie = OtvorSpojenie();
        Vykonaj(spojenie, """
            INSERT OR REPLACE INTO VynimkySviatkov (SviatokId, Rok, DenPracovnehoPokoja, Povod)
            VALUES ($id, $rok, $pokoj, 'pouzivatel');
            """, null, ("$id", sviatokId), ("$rok", rok), ("$pokoj", denPracovnehoPokoja ? 1 : 0));
    }

    /// <summary>Zruší používateľskú zmenu – platí opäť pravidlo zo zákona.</summary>
    public void ZrusZmenu(int sviatokId, int rok)
    {
        using var spojenie = OtvorSpojenie();
        Vykonaj(spojenie, "DELETE FROM VynimkySviatkov WHERE SviatokId = $id AND Rok = $rok AND Povod = 'pouzivatel';",
            null, ("$id", sviatokId), ("$rok", rok));
    }

    /// <summary>Veľkonočná nedeľa (gregoriánsky kalendár, anonymný algoritmus Meeus/Jones/Butcher).</summary>
    public static DateOnly VelkonocnaNedela(int rok)
    {
        var a = rok % 19;
        var b = rok / 100;
        var c = rok % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var mesiac = (h + l - 7 * m + 114) / 31;
        var den = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(rok, mesiac, den);
    }

    // ------------------------------------------------------------------ záloha

    /// <summary>Konzistentná kópia databázy Kalendária (napr. do zálohy programu).</summary>
    public void SkopirujDo(string ciel)
    {
        using var spojenie = OtvorSpojenie();
        Vykonaj(spojenie, "VACUUM INTO $ciel;", null, ("$ciel", ciel));
    }

    // ------------------------------------------------------------------ pomocné

    /// <summary>Stály identifikátor odvodený z obsahu – rovnaký záznam má na každom počítači rovnaký GUID.</summary>
    private static string StalyGuid(string kluc)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(kluc));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }

    private static string Teraz() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static int Pocet(SqliteConnection spojenie, SqliteTransaction? transakcia, string tabulka)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.Transaction = transakcia;
        prikaz.CommandText = $"SELECT COUNT(*) FROM {tabulka};";
        return Convert.ToInt32(prikaz.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Vykonaj(SqliteConnection spojenie, string sql, SqliteTransaction? transakcia = null,
        params (string Nazov, object? Hodnota)[] parametre)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.Transaction = transakcia;
        prikaz.CommandText = sql;
        foreach (var (nazov, hodnota) in parametre)
        {
            prikaz.Parameters.AddWithValue(nazov, hodnota ?? DBNull.Value);
        }

        prikaz.ExecuteNonQuery();
    }
}
