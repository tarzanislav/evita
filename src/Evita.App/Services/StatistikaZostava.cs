using MigraDoc.DocumentObjectModel;

namespace Evita.Services;

/// <summary>Štatistika ako tlačová zostava (náhľad, tlač, PDF) – súhrn a všetky bloky; zoznam položiek z podokna.</summary>
public static class StatistikaZostava
{
    /// <summary>Kľúč súhrnu (ostatné časti majú kľúče blokov obrazovky Štatistika).</summary>
    public const string KlucSuhrn = "suhrn";

    /// <summary>Časti zostavy v poradí tlače: súhrn a bloky.</summary>
    public static IReadOnlyList<(string Kluc, string Nazov)> Casti(int hranica) => new[]
    {
        (KlucSuhrn, Preklad.T("Súhrn")),
        ("kategorie", Preklad.T("Kategórie")),
        ("vek", Preklad.T("Obnova – vek techniky")),
        ("stare", Preklad.F($"Obnova – {hranica}+ ročné podľa kategórií")),
        ("plan", Preklad.T("Plán obnovy na 5 rokov")),
        ("miestnosti", Preklad.T("Miestnosti")),
        ("stav", Preklad.T("Stav")),
        ("zaruky", Preklad.T("Záruky a revízie")),
        ("nakupy", Preklad.T("Nákupy po rokoch")),
        ("osoby", Preklad.T("Pridelené osobám")),
        ("kvalita", Preklad.T("Úplnosť údajov")),
    };

    /// <summary>Zostava štatistiky; <paramref name="casti"/> = kľúče častí na tlač (null = všetky).</summary>
    public static Document Vytvor(StatistikaEvidencie s, bool aVyradene, IReadOnlyCollection<string>? casti = null)
    {
        var zoznam = Casti(s.Hranica);
        bool Ma(string kluc) => casti is null || casti.Contains(kluc);
        var podtitulok = Preklad.F($"stav k {Sk.Datum(s.Dnes)}") + " · "
                         + (aVyradene ? Preklad.T("vrátane vyradených") : Preklad.T("bez vyradených")) + " · "
                         + Preklad.F($"hranica obnovy {s.Hranica} rokov");
        var titulok = casti is { Count: 1 } && zoznam.FirstOrDefault(c => casti.Contains(c.Kluc)) is { Nazov: { } jedna }
            ? Preklad.T("Štatistika evidencie") + " – " + jedna
            : Preklad.T("Štatistika evidencie");
        var z = new PdfZostava(titulok, podtitulok, false);
        var stare = s.Stare;
        if (Ma(KlucSuhrn))
        {
            z.NadpisSkupiny(Preklad.T("Súhrn"));
            var suhrn = new List<string[]>
            {
                new[] { Preklad.T("Položky"), s.Spolu.ToString("N0", Sk.Kultura) },
                new[] { Preklad.T("Hodnota (súčet nákupných cien)"), Sk.Suma(s.Hodnota) },
                new[] { Preklad.T("Priemerný vek"), s.PriemernyVek is { } v ? Preklad.F($"{v:0.0} roka") : "–" },
                new[] { Preklad.F($"Na obnovu ({s.Hranica}+ rokov)"), $"{stare.Count} · {Sk.Suma(stare.Sum(p => p.Cena ?? 0))}" },
                new[] { Preklad.T("Bez dátumu nákupu"), s.Polozky.Count(p => p.DatumNakupu is null).ToString(Sk.Kultura) },
                new[] { Preklad.T("Bez ceny"), s.Polozky.Count(p => p.Cena is null).ToString(Sk.Kultura) },
            };
            z.Tabulka(new[] { Preklad.T("Ukazovateľ"), Preklad.T("Hodnota") }, new[] { false, true }, suhrn,
                new[] { z.SirkaTabulky * 0.6, z.SirkaTabulky * 0.4 });
        }

        var bloky = new (string Kluc, IReadOnlyList<RiadokStatistiky> Riadky, bool Spolu)[]
        {
            ("kategorie", s.Kategorie, true),
            ("vek", s.VekoveSkupiny, true),
            ("stare", s.StareKategorie, true),
            ("plan", s.PlanObnovy, true),
            ("miestnosti", s.Miestnosti, false),
            ("stav", s.Stavy, false),
            ("zaruky", s.ZarukyRevizie, false),
            ("nakupy", s.Nakupy, true),
            ("osoby", s.Osoby, false),
            ("kvalita", s.Kvalita, false),
        };
        foreach (var (kluc, riadky, spolu) in bloky.Where(b => Ma(b.Kluc)))
        {
            var nadpis = zoznam.First(c => c.Kluc == kluc).Nazov;
            if (riadky.Count == 0)
            {
                // samostatne vytlačený prázdny blok – aspoň nadpis a informácia
                if (casti is not null)
                {
                    z.NadpisSkupiny(nadpis);
                    z.Text(Preklad.T("Žiadne údaje."), sedy: true);
                }

                continue;
            }

            Blok(z, nadpis, riadky, spolu);
        }

        return z.Dokument;
    }

    private static void Blok(PdfZostava z, string nadpis, IReadOnlyList<RiadokStatistiky> riadky, bool spolu)
    {
        if (riadky.Count == 0)
        {
            return;
        }

        z.NadpisSkupiny(nadpis);
        var data = riadky.Select(r => new[] { r.Nazov, r.Pocet.ToString(Sk.Kultura), Sk.Suma(r.Suma), r.Doplnok ?? "" }).ToList();
        var w = z.SirkaTabulky;
        z.Tabulka(new[] { "", Preklad.T("Kusov"), Preklad.T("Hodnota"), Preklad.T("Poznámka") }, new[] { false, true, true, false }, data,
            new[] { w * 0.34, w * 0.1, w * 0.18, w * 0.38 });
        if (spolu)
        {
            z.Spolu(Preklad.F($"Spolu {riadky.Sum(r => r.Pocet)} ks · {Sk.Suma(riadky.Sum(r => r.Suma))}"));
        }
    }

    /// <summary>Položky jedného riadku štatistiky s cenami.</summary>
    public static Document Zoznam(string nadpis, RiadokStatistiky riadok, DateTime dnes)
    {
        var z = new PdfZostava(nadpis, Preklad.F($"stav k {Sk.Datum(dnes)}") + " · " + Preklad.Pocet(riadok.Pocet, "{0} položka", "{0} položky", "{0} položiek"), false);
        var hlavicky = new[] { Preklad.T("Č."), Preklad.T("Názov"), Preklad.T("Kategória"), Preklad.T("Miestnosť"), Preklad.T("Nákup"), Preklad.T("Vek"), Preklad.T("Cena") };
        var data = riadok.Polozky.Select(p => new[]
        {
            p.Cislo.ToString(Sk.Kultura), p.Nazov, p.Kategoria, p.Miestnost ?? "", Sk.Datum(p.DatumNakupu), p.VekText(dnes), Sk.Suma(p.Cena),
        }).ToList();
        z.Tabulka(hlavicky, new[] { true, false, false, false, false, true, true }, data, z.Sirky(hlavicky, data));
        z.Spolu(Preklad.F($"Spolu {riadok.Pocet} ks · {Sk.Suma(riadok.Suma)}"));
        return z.Dokument;
    }
}
