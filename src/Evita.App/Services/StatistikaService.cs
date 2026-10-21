using Evita.Data;
using Evita.Models;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

/// <summary>Položka evidencie s údajmi pre štatistiku.</summary>
public sealed record PolozkaStatistiky(
    int Id,
    int Cislo,
    string Nazov,
    int KategoriaId,
    string Kategoria,
    string? KategoriaIkona,
    string? KategoriaFarba,
    string? Miestnost,
    string? Osoba,
    StavPolozky Stav,
    DateTime? DatumNakupu,
    decimal? Cena,
    DateTime? ZarukaDo,
    DateTime? DalsiaRevizia,
    string? Inventarne,
    string? Seriove)
{
    /// <summary>Vek v rokoch (z dátumu nákupu) alebo null.</summary>
    public double? Vek(DateTime dnes) => DatumNakupu is { } d && d <= dnes ? (dnes - d).TotalDays / 365.25 : DatumNakupu is null ? null : 0;

    public string VekText(DateTime dnes) => Vek(dnes) is { } v ? Preklad.F($"{v:0.0} r.") : "";
}

/// <summary>Riadok bloku štatistiky (kategória, miestnosť, veková skupina …) s položkami pre podokno.</summary>
public sealed class RiadokStatistiky
{
    public required string Nazov { get; init; }
    public string? Doplnok { get; init; }
    public FarebnaIkona? Ikona { get; init; }
    public bool MaIkonu => Ikona is not null;
    public required IReadOnlyList<PolozkaStatistiky> Polozky { get; init; }

    /// <summary>Upozornenie (napr. záruka po termíne) – zvýrazní sa.</summary>
    public bool Upozornit { get; init; }

    public int Pocet => Polozky.Count;
    public decimal Suma => Polozky.Sum(p => p.Cena ?? 0);
    public int BezCeny => Polozky.Count(p => p.Cena is null);

    /// <summary>Pomer k najväčšiemu riadku bloku (0 – 1) – dĺžka pruhu.</summary>
    public double Podiel { get; set; }

    public double SirkaPruhu => 4 + Podiel * 110;
    public string PocetText => Preklad.Pocet(Pocet, "{0} ks", "{0} ks", "{0} ks");
    public string SumaText => Sk.Suma(Suma);
}

/// <summary>
/// Štatistika evidencie: súhrn, kategórie, vek a obnova, plán obnovy po rokoch, miestnosti, stav, záruky a revízie,
/// nákupy po rokoch, osoby a úplnosť údajov. Hodnota = súčet nákupných cien (položky bez ceny sa rátajú len do kusov).
/// </summary>
public sealed class StatistikaEvidencie
{
    public const string KlucHranica = "Statistika.HranicaObnovy";

    public required DateTime Dnes { get; init; }
    public required int Hranica { get; init; }
    public required IReadOnlyList<PolozkaStatistiky> Polozky { get; init; }

    public int Spolu => Polozky.Count;
    public decimal Hodnota => Polozky.Sum(p => p.Cena ?? 0);
    public double? PriemernyVek
    {
        get
        {
            var veky = Polozky.Select(p => p.Vek(Dnes)).OfType<double>().ToList();
            return veky.Count == 0 ? null : veky.Average();
        }
    }
    public IReadOnlyList<PolozkaStatistiky> Stare => Polozky.Where(p => p.Vek(Dnes) >= Hranica).ToList();

    public List<RiadokStatistiky> Kategorie { get; } = new();
    public List<RiadokStatistiky> VekoveSkupiny { get; } = new();
    public List<RiadokStatistiky> StareKategorie { get; } = new();
    public List<RiadokStatistiky> PlanObnovy { get; } = new();
    public List<RiadokStatistiky> Miestnosti { get; } = new();
    public List<RiadokStatistiky> Stavy { get; } = new();
    public List<RiadokStatistiky> ZarukyRevizie { get; } = new();
    public List<RiadokStatistiky> Nakupy { get; } = new();
    public List<RiadokStatistiky> Osoby { get; } = new();
    public List<RiadokStatistiky> Kvalita { get; } = new();

    public static int NacitajHranicu()
    {
        try
        {
            using var db = Databaza.Otvor();
            return int.TryParse(Databaza.Citaj(db, KlucHranica), out var h) ? Math.Clamp(h, 2, 15) : 5;
        }
        catch (Exception)
        {
            return 5;
        }
    }

    public static void UlozHranicu(int hranica)
    {
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucHranica, Math.Clamp(hranica, 2, 15).ToString(System.Globalization.CultureInfo.InvariantCulture));
        db.SaveChanges();
    }

    /// <summary>Položky z databázy (vyradené len na požiadanie).</summary>
    public static List<PolozkaStatistiky> NacitajPolozky(bool aVyradene)
    {
        using var db = Databaza.Otvor();
        return db.Polozky.AsNoTracking()
            .Where(p => aVyradene || p.Stav != StavPolozky.Vyradene)
            .OrderBy(p => p.PrirastkoveCislo)
            .Select(p => new PolozkaStatistiky(p.Id, p.PrirastkoveCislo, p.Nazov, p.KategoriaId,
                p.Kategoria != null ? p.Kategoria.Nazov : "", p.Kategoria != null ? p.Kategoria.Ikona : null,
                p.Kategoria != null ? p.Kategoria.Farba : null,
                p.Miestnost != null ? p.Miestnost.Nazov : null, p.Osoba != null ? p.Osoba.Meno + " " + p.Osoba.Priezvisko : null, p.Stav, p.DatumNakupu, p.Cena,
                p.ZarukaDo, p.DalsiaRevizia, p.InventarneCislo, p.SerioveCislo))
            .ToList();
    }

    public static StatistikaEvidencie Vypocitaj(IReadOnlyList<PolozkaStatistiky> polozky, DateTime dnes, int hranica)
    {
        dnes = dnes.Date;
        var s = new StatistikaEvidencie { Dnes = dnes, Hranica = hranica, Polozky = polozky };
        var porovnanie = Sk.Porovnanie;

        // kategórie – kusy a hodnota (v podokne ceny jednotlivých položiek)
        s.Kategorie.AddRange(polozky.GroupBy(p => p.KategoriaId)
            .Select(g => new RiadokStatistiky
            {
                Nazov = g.First().Kategoria,
                Ikona = IkonyKategorii.Pre(g.First().KategoriaIkona, g.First().KategoriaFarba, g.First().Kategoria),
                Polozky = g.ToList(),
                Doplnok = Preklad.F($"priemerne {Sk.Suma(Priemer(g))}"),
            })
            .OrderByDescending(r => r.Suma).ThenByDescending(r => r.Pocet).ThenBy(r => r.Nazov, porovnanie));
        Pomer(s.Kategorie, r => (double)r.Suma);

        // vek podľa dátumu nákupu
        var skupiny = new (string Nazov, double Od, double Do)[]
        {
            (Preklad.T("do 1 roka"), 0, 1),
            (Preklad.T("1 – 2 roky"), 1, 3),
            (Preklad.T("3 – 4 roky"), 3, 5),
            (Preklad.T("5 – 6 rokov"), 5, 7),
            (Preklad.T("7 – 9 rokov"), 7, 10),
            (Preklad.T("10 a viac rokov"), 10, double.MaxValue),
        };
        foreach (var (nazov, od, po) in skupiny)
        {
            s.VekoveSkupiny.Add(new RiadokStatistiky
            {
                Nazov = nazov,
                Polozky = polozky.Where(p => p.Vek(dnes) is { } v && v >= od && v < po).ToList(),
                Upozornit = od >= hranica,
            });
        }

        s.VekoveSkupiny.Add(new RiadokStatistiky { Nazov = Preklad.T("bez dátumu nákupu"), Polozky = polozky.Where(p => p.DatumNakupu is null).ToList() });
        Pomer(s.VekoveSkupiny, r => r.Pocet);

        // staré položky podľa kategórií (kandidáti na obnovu)
        var stare = s.Stare;
        s.StareKategorie.AddRange(stare.GroupBy(p => p.KategoriaId)
            .Select(g => new RiadokStatistiky
            {
                Nazov = g.First().Kategoria,
                Ikona = IkonyKategorii.Pre(g.First().KategoriaIkona, g.First().KategoriaFarba, g.First().Kategoria),
                Polozky = g.OrderBy(p => p.DatumNakupu).ToList(),
                Doplnok = Preklad.F($"{100.0 * g.Count() / Math.Max(1, polozky.Count(p => p.KategoriaId == g.Key)):0} % kategórie"),
            })
            .OrderByDescending(r => r.Pocet).ThenBy(r => r.Nazov, porovnanie));
        Pomer(s.StareKategorie, r => r.Pocet);

        // plán obnovy: kedy položky dosiahnu hranicu – odhad rozpočtu podľa pôvodných cien
        s.PlanObnovy.Add(new RiadokStatistiky
        {
            Nazov = Preklad.F($"už teraz ({hranica} a viac rokov)"),
            Polozky = stare.OrderBy(p => p.DatumNakupu).ToList(),
            Upozornit = true,
        });
        for (var rok = dnes.Year; rok < dnes.Year + 5; rok++)
        {
            var r = rok;
            s.PlanObnovy.Add(new RiadokStatistiky
            {
                Nazov = r.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Polozky = polozky.Where(p => p.DatumNakupu is { } d && d.AddYears(hranica) > dnes && d.AddYears(hranica).Year == r)
                    .OrderBy(p => p.DatumNakupu).ToList(),
            });
        }

        Pomer(s.PlanObnovy, r => (double)r.Suma);

        // miestnosti – kusy, hodnota, staré a v oprave
        s.Miestnosti.AddRange(polozky.GroupBy(p => p.Miestnost)
            .Select(g => new RiadokStatistiky
            {
                Nazov = g.Key ?? Preklad.T("bez miestnosti"),
                Polozky = g.ToList(),
                Upozornit = g.Key is null,
                Doplnok = string.Join(" · ", new[]
                {
                    g.Count(p => p.Vek(dnes) >= hranica) is var st and > 0 ? Preklad.F($"{st} × {hranica}+ r.") : null,
                    g.Count(p => p.Stav == StavPolozky.VOprave) is var op and > 0 ? Preklad.F($"{op} v oprave") : null,
                    g.GroupBy(p => p.Kategoria).OrderByDescending(k => k.Count()).First() is var k ? $"{k.Key} ({k.Count()})" : null,
                }.Where(t => t is not null)),
            })
            .OrderBy(r => r.Upozornit).ThenByDescending(r => r.Pocet).ThenBy(r => r.Nazov, porovnanie));
        Pomer(s.Miestnosti, r => r.Pocet);

        // stav
        foreach (var stav in Enum.GetValues<StavPolozky>())
        {
            var vStave = polozky.Where(p => p.Stav == stav).ToList();
            if (vStave.Count > 0 || stav != StavPolozky.Vyradene)
            {
                s.Stavy.Add(new RiadokStatistiky { Nazov = StavyPoloziek.Nazov(stav), Polozky = vStave, Upozornit = stav == StavPolozky.VOprave && vStave.Count > 0 });
            }
        }

        Pomer(s.Stavy, r => r.Pocet);

        // záruky a revízie
        var aktivne = polozky.Where(p => p.Stav != StavPolozky.Vyradene).ToList();
        s.ZarukyRevizie.Add(new RiadokStatistiky
        {
            Nazov = Preklad.T("záruka končí do 90 dní"),
            Polozky = aktivne.Where(p => p.ZarukaDo is { } z && z >= dnes && z <= dnes.AddDays(90)).OrderBy(p => p.ZarukaDo).ToList(),
            Upozornit = true,
        });
        s.ZarukyRevizie.Add(new RiadokStatistiky
        {
            Nazov = Preklad.T("v záruke"),
            Polozky = aktivne.Where(p => p.ZarukaDo is { } z && z > dnes.AddDays(90)).OrderBy(p => p.ZarukaDo).ToList(),
        });
        s.ZarukyRevizie.Add(new RiadokStatistiky
        {
            Nazov = Preklad.T("po záruke"),
            Polozky = aktivne.Where(p => p.ZarukaDo is { } z && z < dnes).OrderBy(p => p.ZarukaDo).ToList(),
        });
        s.ZarukyRevizie.Add(new RiadokStatistiky
        {
            Nazov = Preklad.T("revízia po termíne"),
            Polozky = aktivne.Where(p => p.DalsiaRevizia is { } r && r < dnes).OrderBy(p => p.DalsiaRevizia).ToList(),
            Upozornit = true,
        });
        s.ZarukyRevizie.Add(new RiadokStatistiky
        {
            Nazov = Preklad.T("revízia do 30 dní"),
            Polozky = aktivne.Where(p => p.DalsiaRevizia is { } r && r >= dnes && r <= dnes.AddDays(30)).OrderBy(p => p.DalsiaRevizia).ToList(),
        });
        Pomer(s.ZarukyRevizie, r => r.Pocet);

        // nákupy po rokoch (posledných 10 rokov)
        for (var rok = dnes.Year; rok > dnes.Year - 10; rok--)
        {
            var r = rok;
            s.Nakupy.Add(new RiadokStatistiky
            {
                Nazov = r.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Polozky = polozky.Where(p => p.DatumNakupu?.Year == r).ToList(),
            });
        }

        var skor = polozky.Where(p => p.DatumNakupu is { } d && d.Year <= dnes.Year - 10).ToList();
        if (skor.Count > 0)
        {
            s.Nakupy.Add(new RiadokStatistiky { Nazov = Preklad.F($"{dnes.Year - 10} a skôr"), Polozky = skor });
        }

        Pomer(s.Nakupy, r => (double)r.Suma);

        // osoby (pridelená technika)
        s.Osoby.AddRange(polozky.Where(p => p.Osoba is not null).GroupBy(p => p.Osoba!)
            .Select(g => new RiadokStatistiky { Nazov = g.Key, Polozky = g.ToList() })
            .OrderByDescending(r => r.Suma).ThenByDescending(r => r.Pocet).ThenBy(r => r.Nazov, porovnanie).Take(15));
        Pomer(s.Osoby, r => (double)r.Suma);

        // úplnosť údajov
        var kvalita = new (string Nazov, Func<PolozkaStatistiky, bool> Chyba)[]
        {
            (Preklad.T("bez dátumu nákupu"), p => p.DatumNakupu is null),
            (Preklad.T("bez ceny"), p => p.Cena is null),
            (Preklad.T("bez miestnosti"), p => p.Miestnost is null),
            (Preklad.T("bez inventárneho čísla"), p => string.IsNullOrWhiteSpace(p.Inventarne)),
            (Preklad.T("bez sériového čísla"), p => string.IsNullOrWhiteSpace(p.Seriove)),
        };
        foreach (var (nazov, chyba) in kvalita)
        {
            var chybne = polozky.Where(chyba).ToList();
            s.Kvalita.Add(new RiadokStatistiky
            {
                Nazov = nazov,
                Polozky = chybne,
                Upozornit = chybne.Count > 0,
                Doplnok = polozky.Count > 0 ? Preklad.F($"{100.0 * chybne.Count / polozky.Count:0} %") : null,
            });
        }

        Pomer(s.Kvalita, r => r.Pocet);
        return s;
    }

    private static decimal Priemer(IEnumerable<PolozkaStatistiky> polozky)
    {
        var ceny = polozky.Where(p => p.Cena is not null).Select(p => p.Cena!.Value).ToList();
        return ceny.Count == 0 ? 0 : Math.Round(ceny.Average(), 2);
    }

    private static void Pomer(List<RiadokStatistiky> riadky, Func<RiadokStatistiky, double> hodnota)
    {
        var najviac = riadky.Count == 0 ? 0 : riadky.Max(hodnota);
        foreach (var r in riadky)
        {
            r.Podiel = najviac > 0 ? hodnota(r) / najviac : 0;
        }
    }
}
