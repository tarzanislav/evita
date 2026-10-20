using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Kalendarium;

/// <summary>Pôvod záznamu v Kalendáriu.</summary>
public static class Povod
{
    public const string Zabudovane = "zabudovane";
    public const string Wikipedia = "wikipedia";
    public const string Pouzivatel = "pouzivatel";
    public const string Import = "import";
}

/// <summary>Druh výročia.</summary>
public enum DruhVyrocia
{
    Udalost,
    Narodenie,
    Umrtie,
}

/// <summary>Citát (alebo príslovie). Skryté: 0 zobrazené, 1 skryté, 2 vymazané zabudované (už sa neobnoví).</summary>
public sealed record Citat(int Id, string Guid, string Jazyk, string Text, string? Autor, string Povod, int Skryte)
{
    public bool JeSkryty => Skryte != 0;
    public bool JeVlastny => Povod is Kalendarium.Povod.Pouzivatel or Kalendarium.Povod.Import;
}

/// <summary>Výročie (udalosť, narodenie alebo úmrtie) v daný deň roka.</summary>
public sealed record Vyrocie(int Id, string Guid, string Jazyk, int Mesiac, int Den, int? Rok, DruhVyrocia Druh, string Text,
    bool Domace, string Povod, int Skryte)
{
    public bool JeSkryte => Skryte != 0;
    public bool JeVlastne => Povod is Kalendarium.Povod.Pouzivatel or Kalendarium.Povod.Import;

    /// <summary>Počet rokov od udalosti v danom roku (null bez roku).</summary>
    public int? RokovV(int rok) => Rok is { } r && r <= rok ? rok - r : null;
}

/// <summary>Nové výročie (napr. zo stránky Wikipédie) pred uložením.</summary>
public sealed record NoveVyrocie(int? Rok, DruhVyrocia Druh, string Text, bool Domace);

/// <summary>Výsledok importu citátov a výročí.</summary>
public sealed record VysledokImportuKalendaria(int Citaty, int Vyrocia, int Preskocene);

public sealed partial class KalendariumDb
{
    private static void VytvorTabulkyCitatovAVyroci(SqliteConnection spojenie)
    {
        Vykonaj(spojenie, """
            CREATE TABLE IF NOT EXISTS Citaty (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Guid TEXT NOT NULL UNIQUE,
                Jazyk TEXT NOT NULL,
                Text TEXT NOT NULL,
                Autor TEXT,
                Povod TEXT NOT NULL,
                Zdroj TEXT,
                Skryte INTEGER NOT NULL DEFAULT 0,
                Pridane TEXT NOT NULL,
                Zmenene TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_Citaty_Jazyk ON Citaty (Jazyk, Skryte);
            CREATE TABLE IF NOT EXISTS Vyrocia (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Guid TEXT NOT NULL UNIQUE,
                Jazyk TEXT NOT NULL,
                Mesiac INTEGER NOT NULL,
                Den INTEGER NOT NULL,
                Rok INTEGER,
                Druh TEXT NOT NULL,
                Text TEXT NOT NULL,
                Domace INTEGER NOT NULL DEFAULT 0,
                Povod TEXT NOT NULL,
                Zdroj TEXT,
                Skryte INTEGER NOT NULL DEFAULT 0,
                Pridane TEXT NOT NULL,
                Zmenene TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_Vyrocia_Datum ON Vyrocia (Jazyk, Mesiac, Den);
            CREATE TABLE IF NOT EXISTS StiahnuteDni (
                Jazyk TEXT NOT NULL,
                Mesiac INTEGER NOT NULL,
                Den INTEGER NOT NULL,
                Stiahnute TEXT NOT NULL,
                Zdroj TEXT,
                PRIMARY KEY (Jazyk, Mesiac, Den));
            """);
    }

    /// <summary>Doplní zabudované citáty, ktoré v databáze ešte nie sú (vymazané sa neobnovia).</summary>
    private static void DoplnZabudovaneCitaty(SqliteConnection spojenie, SqliteTransaction transakcia)
    {
        using var zdroj = typeof(KalendariumDb).Assembly.GetManifestResourceStream("Kalendarium.citaty.json");
        if (zdroj is null)
        {
            return;
        }

        var jazyky = JsonSerializer.Deserialize<Dictionary<string, string?[][]>>(zdroj) ?? new();
        var teraz = Teraz();
        foreach (var (jazyk, citaty) in jazyky)
        {
            foreach (var citat in citaty)
            {
                if (citat.Length == 0 || string.IsNullOrWhiteSpace(citat[0]))
                {
                    continue;
                }

                Vykonaj(spojenie, """
                    INSERT OR IGNORE INTO Citaty (Guid, Jazyk, Text, Autor, Povod, Zdroj, Skryte, Pridane, Zmenene)
                    VALUES ($guid, $jazyk, $text, $autor, $povod, NULL, 0, $teraz, $teraz);
                    """, transakcia,
                    ("$guid", StalyGuid($"citat|{jazyk}|{citat[0]}")), ("$jazyk", jazyk), ("$text", citat[0]),
                    ("$autor", citat.Length > 1 ? citat[1] : null), ("$povod", Povod.Zabudovane), ("$teraz", teraz));
            }
        }
    }

    // ------------------------------------------------------------------ citáty

    private static Citat CitajCitat(SqliteDataReader r) =>
        new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetInt32(6));

    private const string StlpceCitatu = "Id, Guid, Jazyk, Text, Autor, Povod, Skryte";

    /// <summary>Citáty v jazyku (null = všetky jazyky). Vymazané zabudované sa nevracajú.</summary>
    public IReadOnlyList<Citat> Citaty(string? jazyk = null, bool ajSkryte = false)
    {
        using var spojenie = OtvorSpojenie();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = $"SELECT {StlpceCitatu} FROM Citaty WHERE ($jazyk IS NULL OR Jazyk = $jazyk) AND Skryte <= $skryte ORDER BY Id;";
        prikaz.Parameters.AddWithValue("$jazyk", (object?)jazyk ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$skryte", ajSkryte ? 1 : 0);
        var vysledok = new List<Citat>();
        using var r = prikaz.ExecuteReader();
        while (r.Read())
        {
            vysledok.Add(CitajCitat(r));
        }

        return vysledok;
    }

    /// <summary>Jazyky citátov, ak v jazyku programu nie je žiadny (čeština → slovenčina, ostatné → angličtina).</summary>
    public static IReadOnlyList<string> NahradneJazyky(string jazyk) => jazyk switch
    {
        "sk" => new[] { "sk", "cs", "en" },
        "cs" => new[] { "cs", "sk", "en" },
        _ => new[] { jazyk, "en", "sk" },
    };

    /// <summary>Citát dňa – v daný deň vždy ten istý (kým sa nezmení zoznam citátov).</summary>
    public Citat? CitatDna(DateOnly datum, string jazyk)
    {
        foreach (var j in NahradneJazyky(jazyk))
        {
            var citaty = Citaty(j).OrderBy(c => c.Guid, StringComparer.Ordinal).ToList();
            if (citaty.Count > 0)
            {
                return citaty[(int)((uint)(datum.DayNumber * 37) % (uint)citaty.Count)];
            }
        }

        return null;
    }

    /// <summary>Iný náhodný citát v jazyku (na tlačidlo „ďalší citát“).</summary>
    public Citat? NahodnyCitat(string jazyk, int? okrem)
    {
        foreach (var j in NahradneJazyky(jazyk))
        {
            var citaty = Citaty(j).Where(c => c.Id != okrem).ToList();
            if (citaty.Count > 0)
            {
                return citaty[Random.Shared.Next(citaty.Count)];
            }
        }

        return null;
    }

    public Citat PridajCitat(string jazyk, string text, string? autor, string povod = Povod.Pouzivatel, string? guid = null)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
        {
            throw new ArgumentException("Text citátu je prázdny.", nameof(text));
        }

        var teraz = Teraz();
        guid ??= System.Guid.NewGuid().ToString();
        using var spojenie = OtvorSpojenie();
        Vykonaj(spojenie, """
            INSERT INTO Citaty (Guid, Jazyk, Text, Autor, Povod, Skryte, Pridane, Zmenene)
            VALUES ($guid, $jazyk, $text, $autor, $povod, 0, $teraz, $teraz);
            """, null, ("$guid", guid), ("$jazyk", jazyk), ("$text", text), ("$autor", Prazdne(autor)), ("$povod", povod), ("$teraz", teraz));
        return Citaty(jazyk, true).First(c => c.Guid == guid);
    }

    public void UpravCitat(int id, string text, string? autor)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
        {
            throw new ArgumentException("Text citátu je prázdny.", nameof(text));
        }

        using var spojenie = OtvorSpojenie();
        Vykonaj(spojenie, "UPDATE Citaty SET Text = $text, Autor = $autor, Zmenene = $teraz WHERE Id = $id;", null,
            ("$text", text), ("$autor", Prazdne(autor)), ("$teraz", Teraz()), ("$id", id));
    }

    public void NastavSkrytyCitat(int id, bool skryty)
    {
        using var spojenie = OtvorSpojenie();
        Vykonaj(spojenie, "UPDATE Citaty SET Skryte = $s, Zmenene = $teraz WHERE Id = $id AND Skryte <> 2;", null,
            ("$s", skryty ? 1 : 0), ("$teraz", Teraz()), ("$id", id));
    }

    /// <summary>Vymaže citát. Zabudovaný sa len označí ako vymazaný, aby sa pri aktualizácii znova neobjavil.</summary>
    public void VymazCitat(int id)
    {
        using var spojenie = OtvorSpojenie();
        Vykonaj(spojenie, "DELETE FROM Citaty WHERE Id = $id AND Povod <> $zabudovane;", null, ("$id", id), ("$zabudovane", Povod.Zabudovane));
        Vykonaj(spojenie, "UPDATE Citaty SET Skryte = 2, Zmenene = $teraz WHERE Id = $id;", null, ("$id", id), ("$teraz", Teraz()));
    }

    /// <summary>Vráti vymazané a skryté zabudované citáty.</summary>
    public int ObnovZabudovaneCitaty()
    {
        using var spojenie = OtvorSpojenie();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "UPDATE Citaty SET Skryte = 0, Zmenene = $teraz WHERE Povod = $zabudovane AND Skryte <> 0;";
        prikaz.Parameters.AddWithValue("$teraz", Teraz());
        prikaz.Parameters.AddWithValue("$zabudovane", Povod.Zabudovane);
        return prikaz.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ výročia

    private const string StlpceVyrocia = "Id, Guid, Jazyk, Mesiac, Den, Rok, Druh, Text, Domace, Povod, Skryte";

    private static Vyrocie CitajVyrocie(SqliteDataReader r) =>
        new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.IsDBNull(5) ? null : r.GetInt32(5),
            Enum.TryParse<DruhVyrocia>(r.GetString(6), out var druh) ? druh : DruhVyrocia.Udalost, r.GetString(7),
            r.GetInt32(8) != 0, r.GetString(9), r.GetInt32(10));

    /// <summary>Výročia v daný deň roka (podľa roka zostupne – najnovšie hore).</summary>
    public IReadOnlyList<Vyrocie> Vyrocia(int mesiac, int den, string? jazyk, bool ajSkryte = false)
    {
        using var spojenie = OtvorSpojenie();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = $"""
            SELECT {StlpceVyrocia} FROM Vyrocia
            WHERE Mesiac = $m AND Den = $d AND ($jazyk IS NULL OR Jazyk = $jazyk) AND Skryte <= $skryte
            ORDER BY Rok IS NULL, Rok DESC, Id;
            """;
        prikaz.Parameters.AddWithValue("$m", mesiac);
        prikaz.Parameters.AddWithValue("$d", den);
        prikaz.Parameters.AddWithValue("$jazyk", (object?)jazyk ?? DBNull.Value);
        prikaz.Parameters.AddWithValue("$skryte", ajSkryte ? 1 : 0);
        var vysledok = new List<Vyrocie>();
        using var r = prikaz.ExecuteReader();
        while (r.Read())
        {
            vysledok.Add(CitajVyrocie(r));
        }

        return vysledok;
    }

    /// <summary>Vlastné výročia (pridané používateľom alebo importované) zo všetkých dní.</summary>
    public IReadOnlyList<Vyrocie> VlastneVyrocia()
    {
        using var spojenie = OtvorSpojenie();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = $"SELECT {StlpceVyrocia} FROM Vyrocia WHERE Povod IN ($p, $i) ORDER BY Mesiac, Den, Rok;";
        prikaz.Parameters.AddWithValue("$p", Povod.Pouzivatel);
        prikaz.Parameters.AddWithValue("$i", Povod.Import);
        var vysledok = new List<Vyrocie>();
        using var r = prikaz.ExecuteReader();
        while (r.Read())
        {
            vysledok.Add(CitajVyrocie(r));
        }

        return vysledok;
    }

    /// <summary>Čas, kedy sa výročia dňa naposledy stiahli (null = ešte nie).</summary>
    public DateTime? Stiahnute(string jazyk, int mesiac, int den)
    {
        using var spojenie = OtvorSpojenie();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "SELECT Stiahnute FROM StiahnuteDni WHERE Jazyk = $j AND Mesiac = $m AND Den = $d;";
        prikaz.Parameters.AddWithValue("$j", jazyk);
        prikaz.Parameters.AddWithValue("$m", mesiac);
        prikaz.Parameters.AddWithValue("$d", den);
        return prikaz.ExecuteScalar() is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var cas) ? cas : null;
    }

    /// <summary>Počet dní roka, pre ktoré sú výročia v jazyku stiahnuté.</summary>
    public int PocetStiahnutychDni(string jazyk)
    {
        using var spojenie = OtvorSpojenie();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "SELECT COUNT(*) FROM StiahnuteDni WHERE Jazyk = $j;";
        prikaz.Parameters.AddWithValue("$j", jazyk);
        return Convert.ToInt32(prikaz.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Uloží stiahnuté výročia dňa: predchádzajúce stiahnuté (nie skryté) nahradí novými; skryté a vlastné zostanú.
    /// </summary>
    public int UlozStiahnute(string jazyk, int mesiac, int den, IEnumerable<NoveVyrocie> vyrocia, string zdroj)
    {
        var teraz = Teraz();
        using var spojenie = OtvorSpojenie();
        using var transakcia = spojenie.BeginTransaction();
        Vykonaj(spojenie, "DELETE FROM Vyrocia WHERE Jazyk = $j AND Mesiac = $m AND Den = $d AND Povod = $w AND Skryte = 0;", transakcia,
            ("$j", jazyk), ("$m", mesiac), ("$d", den), ("$w", Povod.Wikipedia));
        var pocet = 0;
        foreach (var v in vyrocia)
        {
            Vykonaj(spojenie, """
                INSERT OR IGNORE INTO Vyrocia (Guid, Jazyk, Mesiac, Den, Rok, Druh, Text, Domace, Povod, Zdroj, Skryte, Pridane, Zmenene)
                VALUES ($guid, $j, $m, $d, $rok, $druh, $text, $domace, $povod, $zdroj, 0, $teraz, $teraz);
                """, transakcia,
                ("$guid", StalyGuid($"vyrocie|{jazyk}|{mesiac}|{den}|{v.Druh}|{v.Rok}|{v.Text}")), ("$j", jazyk), ("$m", mesiac), ("$d", den),
                ("$rok", v.Rok), ("$druh", v.Druh.ToString()), ("$text", v.Text), ("$domace", v.Domace ? 1 : 0),
                ("$povod", Povod.Wikipedia), ("$zdroj", zdroj), ("$teraz", teraz));
            pocet++;
        }

        Vykonaj(spojenie, "INSERT OR REPLACE INTO StiahnuteDni (Jazyk, Mesiac, Den, Stiahnute, Zdroj) VALUES ($j, $m, $d, $teraz, $zdroj);", transakcia,
            ("$j", jazyk), ("$m", mesiac), ("$d", den), ("$teraz", teraz), ("$zdroj", zdroj));
        transakcia.Commit();
        return pocet;
    }

    public Vyrocie PridajVyrocie(string jazyk, int mesiac, int den, int? rok, DruhVyrocia druh, string text, bool domace,
        string povod = Povod.Pouzivatel, string? guid = null)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
        {
            throw new ArgumentException("Text výročia je prázdny.", nameof(text));
        }

        _ = new DateOnly(2024, mesiac, den); // kontrola dňa (aj 29. februára)
        guid ??= System.Guid.NewGuid().ToString();
        var teraz = Teraz();
        using var spojenie = OtvorSpojenie();
        Vykonaj(spojenie, """
            INSERT INTO Vyrocia (Guid, Jazyk, Mesiac, Den, Rok, Druh, Text, Domace, Povod, Skryte, Pridane, Zmenene)
            VALUES ($guid, $j, $m, $d, $rok, $druh, $text, $domace, $povod, 0, $teraz, $teraz);
            """, null, ("$guid", guid), ("$j", jazyk), ("$m", mesiac), ("$d", den), ("$rok", rok), ("$druh", druh.ToString()),
            ("$text", text), ("$domace", domace ? 1 : 0), ("$povod", povod), ("$teraz", teraz));
        return Vyrocia(mesiac, den, jazyk, true).First(v => v.Guid == guid);
    }

    public void NastavSkryteVyrocie(int id, bool skryte)
    {
        using var spojenie = OtvorSpojenie();
        Vykonaj(spojenie, "UPDATE Vyrocia SET Skryte = $s, Zmenene = $teraz WHERE Id = $id AND Skryte <> 2;", null,
            ("$s", skryte ? 1 : 0), ("$teraz", Teraz()), ("$id", id));
    }

    /// <summary>Vymaže výročie. Stiahnuté sa len označí ako vymazané, aby sa pri ďalšom stiahnutí neobjavilo znova.</summary>
    public void VymazVyrocie(int id)
    {
        using var spojenie = OtvorSpojenie();
        Vykonaj(spojenie, "DELETE FROM Vyrocia WHERE Id = $id AND Povod IN ($p, $i);", null, ("$id", id), ("$p", Povod.Pouzivatel), ("$i", Povod.Import));
        Vykonaj(spojenie, "UPDATE Vyrocia SET Skryte = 2, Zmenene = $teraz WHERE Id = $id;", null, ("$id", id), ("$teraz", Teraz()));
    }

    public int PocetCitatov()
    {
        using var spojenie = OtvorSpojenie();
        return Pocet(spojenie, null, "Citaty WHERE Skryte = 0");
    }

    public int PocetVyroci()
    {
        using var spojenie = OtvorSpojenie();
        return Pocet(spojenie, null, "Vyrocia WHERE Skryte = 0");
    }

    // ------------------------------------------------------------------ export a import

    private sealed class SuborKalendaria
    {
        public string Format { get; set; } = "Kalendarium";
        public int Verzia { get; set; } = 1;
        public string? Vytvorene { get; set; }
        public List<CitatPrenos> Citaty { get; set; } = new();
        public List<VyrociePrenos> Vyrocia { get; set; } = new();
    }

    private sealed class CitatPrenos
    {
        public string? Guid { get; set; }
        public string Jazyk { get; set; } = "sk";
        public string Text { get; set; } = "";
        public string? Autor { get; set; }
        public string? Povod { get; set; }
        public bool Skryty { get; set; }
    }

    private sealed class VyrociePrenos
    {
        public string? Guid { get; set; }
        public string Jazyk { get; set; } = "sk";
        public int Mesiac { get; set; }
        public int Den { get; set; }
        public int? Rok { get; set; }
        public string Druh { get; set; } = nameof(DruhVyrocia.Udalost);
        public string Text { get; set; } = "";
        public bool Domace { get; set; }
        public string? Povod { get; set; }
        public bool Skryte { get; set; }
    }

    private static readonly JsonSerializerOptions MoznostiJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Uloží citáty a výročia do súboru JSON (aj pre iné programy). Vymazané sa neukladajú.</summary>
    public (int Citaty, int Vyrocia) Exportuj(string cesta, bool citaty = true, bool vyrocia = true, bool lenVlastne = false)
    {
        var subor = new SuborKalendaria { Vytvorene = Teraz() };
        if (citaty)
        {
            subor.Citaty = Citaty(null, true).Where(c => !lenVlastne || c.JeVlastny)
                .Select(c => new CitatPrenos { Guid = c.Guid, Jazyk = c.Jazyk, Text = c.Text, Autor = c.Autor, Povod = c.Povod, Skryty = c.JeSkryty })
                .ToList();
        }

        if (vyrocia)
        {
            using var spojenie = OtvorSpojenie();
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = $"SELECT {StlpceVyrocia} FROM Vyrocia WHERE Skryte <= 1 ORDER BY Mesiac, Den, Rok;";
            using var r = prikaz.ExecuteReader();
            while (r.Read())
            {
                var v = CitajVyrocie(r);
                if (lenVlastne && !v.JeVlastne)
                {
                    continue;
                }

                subor.Vyrocia.Add(new VyrociePrenos
                {
                    Guid = v.Guid, Jazyk = v.Jazyk, Mesiac = v.Mesiac, Den = v.Den, Rok = v.Rok, Druh = v.Druh.ToString(),
                    Text = v.Text, Domace = v.Domace, Povod = v.Povod, Skryte = v.JeSkryte,
                });
            }
        }

        File.WriteAllText(cesta, JsonSerializer.Serialize(subor, MoznostiJson), new System.Text.UTF8Encoding(false));
        return (subor.Citaty.Count, subor.Vyrocia.Count);
    }

    /// <summary>Načíta citáty a výročia zo súboru JSON. Záznamy s rovnakým identifikátorom alebo textom sa preskočia.</summary>
    public VysledokImportuKalendaria Importuj(string cesta)
    {
        var subor = JsonSerializer.Deserialize<SuborKalendaria>(File.ReadAllText(cesta), MoznostiJson)
            ?? throw new InvalidDataException("Súbor neobsahuje citáty ani výročia.");
        if (!string.Equals(subor.Format, "Kalendarium", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Súbor nie je export Kalendária.");
        }

        var existujuceCitaty = Citaty(null, true).Select(c => (c.Jazyk, Text: Normalizuj(c.Text))).ToHashSet();
        var guidy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var spojenie = OtvorSpojenie())
        {
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = "SELECT Guid FROM Citaty UNION ALL SELECT Guid FROM Vyrocia;";
            using var r = prikaz.ExecuteReader();
            while (r.Read())
            {
                guidy.Add(r.GetString(0));
            }
        }

        int citaty = 0, vyrocia = 0, preskocene = 0;
        foreach (var c in subor.Citaty)
        {
            if (string.IsNullOrWhiteSpace(c.Text) || (c.Guid is { } g && guidy.Contains(g)) || !existujuceCitaty.Add((c.Jazyk, Normalizuj(c.Text))))
            {
                preskocene++;
                continue;
            }

            var novy = PridajCitat(c.Jazyk, c.Text, c.Autor, Povod.Import, c.Guid);
            if (c.Skryty)
            {
                NastavSkrytyCitat(novy.Id, true);
            }

            citaty++;
        }

        foreach (var v in subor.Vyrocia)
        {
            if (string.IsNullOrWhiteSpace(v.Text) || v.Mesiac is < 1 or > 12 || v.Den < 1 || v.Den > DateTime.DaysInMonth(2024, v.Mesiac)
                || (v.Guid is { } g && guidy.Contains(g)))
            {
                preskocene++;
                continue;
            }

            var druh = Enum.TryParse<DruhVyrocia>(v.Druh, true, out var d) ? d : DruhVyrocia.Udalost;
            var nove = PridajVyrocie(v.Jazyk, v.Mesiac, v.Den, v.Rok, druh, v.Text, v.Domace, Povod.Import, v.Guid);
            if (v.Skryte)
            {
                NastavSkryteVyrocie(nove.Id, true);
            }

            vyrocia++;
        }

        return new VysledokImportuKalendaria(citaty, vyrocia, preskocene);
    }

    private static string Normalizuj(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static string? Prazdne(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
