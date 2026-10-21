using MigraDoc.DocumentObjectModel;

namespace Evita.Services;

/// <summary>Štatistika ako tlačová zostava (náhľad, tlač, PDF) – súhrn a všetky bloky; zoznam položiek z podokna.</summary>
public static class StatistikaZostava
{
    public static Document Vytvor(StatistikaEvidencie s, bool aVyradene)
    {
        var podtitulok = Preklad.F($"stav k {Sk.Datum(s.Dnes)}") + " · "
                         + (aVyradene ? Preklad.T("vrátane vyradených") : Preklad.T("bez vyradených")) + " · "
                         + Preklad.F($"hranica obnovy {s.Hranica} rokov");
        var z = new PdfZostava(Preklad.T("Štatistika evidencie"), podtitulok, false);
        var stare = s.Stare;
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

        Blok(z, Preklad.T("Kategórie"), s.Kategorie, true);
        Blok(z, Preklad.T("Obnova – vek techniky"), s.VekoveSkupiny, true);
        Blok(z, Preklad.F($"Obnova – {s.Hranica}+ ročné podľa kategórií"), s.StareKategorie, true);
        Blok(z, Preklad.T("Plán obnovy na 5 rokov"), s.PlanObnovy, true);
        Blok(z, Preklad.T("Miestnosti"), s.Miestnosti, false);
        Blok(z, Preklad.T("Stav"), s.Stavy, false);
        Blok(z, Preklad.T("Záruky a revízie"), s.ZarukyRevizie, false);
        Blok(z, Preklad.T("Nákupy po rokoch"), s.Nakupy, true);
        Blok(z, Preklad.T("Pridelené osobám"), s.Osoby, false);
        Blok(z, Preklad.T("Úplnosť údajov"), s.Kvalita, false);
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
