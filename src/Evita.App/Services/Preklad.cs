using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Evita.Services;

/// <summary>Jazyk programu: kód, názov v danom jazyku a formáty dátumu.</summary>
public sealed record Jazyk(string Kod, string Nazov, string Kultura, string Datum, string DatumCas, string KratkyDatum)
{
    public override string ToString() => Nazov;
}

/// <summary>
/// Preklad textov programu. Zdrojom je slovenský text, ktorý je zároveň kľúčom do slovníka daného jazyka
/// (vložené súbory Preklady/xx.json). Ak preklad chýba, zobrazí sa slovenský text.
/// </summary>
public static class Preklad
{
    public const string KlucJazyk = "Program.Jazyk";

    /// <summary>Predvolený jazyk po inštalácii a po „Language reset“.</summary>
    public const string Predvoleny = "en";

    public static readonly IReadOnlyList<Jazyk> Jazyky = new Jazyk[]
    {
        new("en", "English", "en-GB", "dd/MM/yyyy", "dd/MM/yyyy HH:mm", "d MMM"),
        new("sk", "Slovenčina", "sk-SK", "d. M. yyyy", "d. M. yyyy H:mm", "d. M."),
        new("cs", "Čeština", "cs-CZ", "d. M. yyyy", "d. M. yyyy H:mm", "d. M."),
        new("pl", "Polski", "pl-PL", "dd.MM.yyyy", "dd.MM.yyyy HH:mm", "dd.MM"),
        new("hu", "Magyar", "hu-HU", "yyyy. MM. dd.", "yyyy. MM. dd. H:mm", "MM. dd."),
        new("ru", "Русский", "ru-RU", "dd.MM.yyyy", "dd.MM.yyyy HH:mm", "dd.MM"),
        new("zh", "中文（简体）", "zh-CN", "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "M月d日"),
    };

    private static Dictionary<string, string>? _slovnik;

    /// <summary>Aktuálny jazyk (pred načítaním nastavení slovenčina – v nej sú zdrojové texty).</summary>
    public static Jazyk Aktualny { get; private set; } = Jazyky[1];

    public static string Kod => Aktualny.Kod;

    public static CultureInfo Kultura { get; private set; } = new("sk-SK");

    public static bool JeSlovencina => Aktualny.Kod == "sk";

    /// <summary>Pri kontrole prekladu (samotest) sa zbierajú texty, ktoré v slovníku chýbajú.</summary>
    public static bool ZbieratChybajuce { get; set; }

    public static ConcurrentDictionary<string, byte> Chybajuce { get; } = new();

    public static Jazyk Najdi(string? kod) =>
        Jazyky.FirstOrDefault(j => string.Equals(j.Kod, kod, StringComparison.OrdinalIgnoreCase)) ?? Jazyky[0];

    /// <summary>Prepne jazyk programu (texty, kultúra vlákna). Okná otvorené predtým zostanú v pôvodnom jazyku.</summary>
    public static void Nastav(string? kod)
    {
        var jazyk = Najdi(kod);
        _slovnik = jazyk.Kod == "sk" ? null : NacitajSlovnik(jazyk.Kod);
        Aktualny = jazyk;
        var kultura = new CultureInfo(jazyk.Kultura);
        Kultura = kultura;
        CultureInfo.DefaultThreadCurrentCulture = kultura;
        CultureInfo.DefaultThreadCurrentUICulture = kultura;
        Thread.CurrentThread.CurrentCulture = kultura;
        Thread.CurrentThread.CurrentUICulture = kultura;
        Sk.ZmenaJazyka();
    }

    /// <summary>Slovník jazyka z vloženého súboru (slovenský text → preklad).</summary>
    public static Dictionary<string, string> NacitajSlovnik(string kod)
    {
        var zostava = typeof(Preklad).Assembly;
        using var prud = zostava.GetManifestResourceStream($"Evita.Preklady.{kod}.json");
        if (prud is null)
        {
            return new Dictionary<string, string>();
        }

        var slovnik = JsonSerializer.Deserialize<Dictionary<string, string>>(prud) ?? new Dictionary<string, string>();
        return new Dictionary<string, string>(slovnik.Where(p => !string.IsNullOrEmpty(p.Value)), StringComparer.Ordinal);
    }

    /// <summary>Preloží slovenský text do aktuálneho jazyka.</summary>
    public static string T(string text)
    {
        if (_slovnik is null || string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (_slovnik.TryGetValue(text, out var preklad))
        {
            return preklad;
        }

        if (ZbieratChybajuce && Potrebuje(text))
        {
            Chybajuce.TryAdd(text, 0);
        }

        return text;
    }

    /// <summary>Označí text na preklad bez prekladu (preloží sa neskôr cez <see cref="T"/>) – kvôli zoznamu textov na preklad.</summary>
    public static string N(string text) => text;

    /// <summary>Je text v slovníku aktuálneho jazyka (alebo je jazyk slovenčina)?</summary>
    public static bool MaPreklad(string text) => _slovnik is null || _slovnik.ContainsKey(text);

    /// <summary>Preklad bez hlásenia chýbajúceho textu (texty, ktoré môžu, ale nemusia byť v slovníku – napr. názvy sviatkov).</summary>
    public static string TAkJe(string text) =>
        _slovnik is not null && !string.IsNullOrEmpty(text) && _slovnik.TryGetValue(text, out var preklad) ? preklad : text;

    /// <summary>Preloží text s vloženými hodnotami: F($"Pridané {pocet} položky") – kľúčom je „Pridané {0} položky“.</summary>
    public static string F(FormattableString text)
    {
        var vzor = T(text.Format);
        try
        {
            return string.Format(Kultura, vzor, text.GetArguments());
        }
        catch (FormatException)
        {
            // chybný preklad nesmie zhodiť program – použije sa slovenský vzor
            return string.Format(Kultura, text.Format, text.GetArguments());
        }
    }

    /// <summary>
    /// Počet s tvarom slova podľa jazyka: Pocet(5, "{0} položka", "{0} položky", "{0} položiek").
    /// Preklad obsahuje tvary oddelené znakom | podľa pravidiel jazyka (angličtina 2, čeština, poľština a ruština 3, maďarčina a čínština 1).
    /// </summary>
    public static string Pocet(long pocet, string jeden, string dvaAzStyri, string viac)
    {
        var kluc = $"{jeden}|{dvaAzStyri}|{viac}";
        string[] tvary;
        if (_slovnik is null)
        {
            tvary = new[] { jeden, dvaAzStyri, viac };
        }
        else
        {
            tvary = T(kluc).Split('|');
        }

        var index = Math.Min(IndexTvaru(pocet), tvary.Length - 1);
        return string.Format(Kultura, tvary[index], pocet);
    }

    private static int IndexTvaru(long n)
    {
        var a = Math.Abs(n);
        switch (Aktualny.Kod)
        {
            case "en":
                return a == 1 ? 0 : 1;
            case "sk":
            case "cs":
                return a == 1 ? 0 : a is >= 2 and <= 4 ? 1 : 2;
            case "pl":
                if (a == 1)
                {
                    return 0;
                }

                return a % 10 is >= 2 and <= 4 && a % 100 is < 12 or > 14 ? 1 : 2;
            case "ru":
                if (a % 10 == 1 && a % 100 != 11)
                {
                    return 0;
                }

                return a % 10 is >= 2 and <= 4 && a % 100 is < 12 or > 14 ? 1 : 2;
            default:
                return 0;
        }
    }

    /// <summary>Text, ktorý by mal mať preklad (obsahuje písmená – nie len čísla, znaky alebo medzery).</summary>
    private static bool Potrebuje(string text) => text.Any(char.IsLetter);

    /// <summary>Jazyk uložený v databáze (ak databáza ešte neexistuje alebo jazyk nie je nastavený – angličtina).</summary>
    public static string JazykZDatabazy()
    {
        try
        {
            if (!File.Exists(Data.Databaza.Cesta))
            {
                return Predvoleny;
            }

            using var spojenie = Data.Databaza.OtvorSpojenie();
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = "SELECT \"Hodnota\" FROM \"Nastavenia\" WHERE \"Kluc\" = $kluc;";
            prikaz.Parameters.AddWithValue("$kluc", KlucJazyk);
            return prikaz.ExecuteScalar() as string is { Length: > 0 } kod ? Najdi(kod).Kod : Predvoleny;
        }
        catch (Exception)
        {
            return Predvoleny;
        }
    }

    public static void UlozJazyk(string kod)
    {
        using var db = Data.Databaza.Otvor();
        Data.Databaza.Zapis(db, KlucJazyk, Najdi(kod).Kod);
        db.SaveChanges();
    }
}
