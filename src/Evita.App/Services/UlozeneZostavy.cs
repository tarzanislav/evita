using System.Text.Json;
using System.Text.Json.Serialization;
using Evita.Data;

namespace Evita.Services;

/// <summary>Obdobie uloženej zostavy: pevné dátumy alebo obdobie, ktoré sa počíta podľa dňa tlače.</summary>
public enum ObdobieZostavy
{
    PevneDatumy,
    TentoMesiac,
    MinulyMesiac,
    TentoRok,
    MinulyRok,
    Poslednych12Mesiacov,
}

public sealed record VolbaObdobia(ObdobieZostavy Hodnota, string Nazov);

/// <summary>Zostava uložená pod vlastným názvom so všetkými voľbami (stĺpce, poradie, obdobie, filtre, orientácia).</summary>
public sealed class UlozenaZostava
{
    public string Nazov { get; set; } = "";
    public string Druh { get; set; } = "";
    public List<string> Stlpce { get; set; } = new();
    public bool NaSirku { get; set; }
    public bool VrataneVyradenych { get; set; }
    public bool Podpis { get; set; } = true;
    public bool KazdaNaNovejStrane { get; set; } = true;
    public bool IkonaKategorie { get; set; } = true;
    public string? Zoskupenie { get; set; }
    public string Obdobie { get; set; } = nameof(ObdobieZostavy.PevneDatumy);
    public DateTime? Od { get; set; }
    public DateTime? Do { get; set; }
    public string? Kategoria { get; set; }
    public string? Miestnost { get; set; }
    public string? Osoba { get; set; }
    public string? PrirastkoveCislo { get; set; }
    public bool KalendarZoznam { get; set; }
    public bool AutomatickeTerminy { get; set; } = true;
    public DateTime Ulozene { get; set; }

    [JsonIgnore]
    public ObdobieZostavy DruhObdobia => Enum.TryParse<ObdobieZostavy>(Obdobie, out var o) ? o : ObdobieZostavy.PevneDatumy;

    /// <summary>Druh zostavy a obdobie – druhý riadok v zozname uložených zostáv.</summary>
    [JsonIgnore]
    public string Popis
    {
        get
        {
            var druh = ZostavyService.Zoznam.FirstOrDefault(z => z.Druh.ToString() == Druh)?.Nazov ?? Druh;
            var obdobie = DruhObdobia == ObdobieZostavy.PevneDatumy
                ? Od is { } od && Do is { } po ? $"{Sk.Datum(od)} – {Sk.Datum(po)}" : null
                : UlozeneZostavy.NazovObdobia(DruhObdobia);
            return obdobie is null ? druh : $"{druh} · {obdobie}";
        }
    }
}

/// <summary>Uložené zostavy v databáze (kľúč Tlac.… – prenášajú sa aj s konfiguráciou programu).</summary>
public static class UlozeneZostavy
{
    public const string Kluc = "Tlac.UlozeneZostavy";

    private static readonly JsonSerializerOptions Moznosti = new() { WriteIndented = false };

    public static IReadOnlyList<VolbaObdobia> Obdobia => new VolbaObdobia[]
    {
        new(ObdobieZostavy.PevneDatumy, NazovObdobia(ObdobieZostavy.PevneDatumy)),
        new(ObdobieZostavy.TentoMesiac, NazovObdobia(ObdobieZostavy.TentoMesiac)),
        new(ObdobieZostavy.MinulyMesiac, NazovObdobia(ObdobieZostavy.MinulyMesiac)),
        new(ObdobieZostavy.TentoRok, NazovObdobia(ObdobieZostavy.TentoRok)),
        new(ObdobieZostavy.MinulyRok, NazovObdobia(ObdobieZostavy.MinulyRok)),
        new(ObdobieZostavy.Poslednych12Mesiacov, NazovObdobia(ObdobieZostavy.Poslednych12Mesiacov)),
    };

    public static string NazovObdobia(ObdobieZostavy obdobie) => obdobie switch
    {
        ObdobieZostavy.TentoMesiac => Preklad.T("tento mesiac"),
        ObdobieZostavy.MinulyMesiac => Preklad.T("minulý mesiac"),
        ObdobieZostavy.TentoRok => Preklad.T("tento rok"),
        ObdobieZostavy.MinulyRok => Preklad.T("minulý rok"),
        ObdobieZostavy.Poslednych12Mesiacov => Preklad.T("posledných 12 mesiacov"),
        _ => Preklad.T("pevné dátumy (od – do)"),
    };

    /// <summary>Dátumy obdobia ku dňu <paramref name="dnes"/> (pri pevných dátumoch null).</summary>
    public static (DateTime Od, DateTime Do)? Rozsah(ObdobieZostavy obdobie, DateTime dnes)
    {
        var mesiac = new DateTime(dnes.Year, dnes.Month, 1);
        return obdobie switch
        {
            ObdobieZostavy.TentoMesiac => (mesiac, mesiac.AddMonths(1).AddDays(-1)),
            ObdobieZostavy.MinulyMesiac => (mesiac.AddMonths(-1), mesiac.AddDays(-1)),
            ObdobieZostavy.TentoRok => (new DateTime(dnes.Year, 1, 1), new DateTime(dnes.Year, 12, 31)),
            ObdobieZostavy.MinulyRok => (new DateTime(dnes.Year - 1, 1, 1), new DateTime(dnes.Year - 1, 12, 31)),
            ObdobieZostavy.Poslednych12Mesiacov => (dnes.Date.AddMonths(-12).AddDays(1), dnes.Date),
            _ => null,
        };
    }

    public static List<UlozenaZostava> Nacitaj()
    {
        try
        {
            using var db = Databaza.Otvor();
            var json = Databaza.Citaj(db, Kluc);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<UlozenaZostava>();
            }

            return (JsonSerializer.Deserialize<List<UlozenaZostava>>(json, Moznosti) ?? new List<UlozenaZostava>())
                .Where(z => !string.IsNullOrWhiteSpace(z.Nazov))
                .OrderBy(z => z.Nazov, Sk.Porovnanie)
                .ToList();
        }
        catch (Exception)
        {
            // poškodený zápis – akoby nebola uložená žiadna zostava
            return new List<UlozenaZostava>();
        }
    }

    public static void Uloz(IEnumerable<UlozenaZostava> zostavy)
    {
        using var db = Databaza.Otvor();
        var zoznam = zostavy.OrderBy(z => z.Nazov, Sk.Porovnanie).ToList();
        Databaza.Zapis(db, Kluc, zoznam.Count == 0 ? null : JsonSerializer.Serialize(zoznam, Moznosti));
        db.SaveChanges();
    }

    /// <summary>Pridá zostavu alebo nahradí uloženú s rovnakým názvom.</summary>
    public static void Pridaj(UlozenaZostava zostava)
    {
        var zoznam = Nacitaj();
        zoznam.RemoveAll(z => Sk.Porovnanie.Equals(z.Nazov.Trim(), zostava.Nazov.Trim()));
        zoznam.Add(zostava);
        Uloz(zoznam);
    }

    public static void Vymaz(string nazov)
    {
        var zoznam = Nacitaj();
        zoznam.RemoveAll(z => Sk.Porovnanie.Equals(z.Nazov, nazov));
        Uloz(zoznam);
    }
}
