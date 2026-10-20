using Evita.Data;
using Evita.Models;
using MigraDoc.DocumentObjectModel;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

public enum DruhZostavy
{
    SuhrnnyZoznam,
    Vyfiltrovane,
    PoMiestnostiach,
    PoKategoriach,
    PodlaPridelenia,
    Prirastkovy,
    PodlaDatumuPridania,
    Vyradene,
    Historia,
    Kalendar,
}

public enum Zoskupenie
{
    Kategoria,
    Miestnost,
    Pridelenie,
}

/// <summary>Popis zostavy v zozname: čo obsahuje a ktoré voľby má.</summary>
public sealed record PopisZostavy(
    DruhZostavy Druh,
    string Nazov,
    string Popis,
    string[] PredvoleneStlpce,
    bool MaObdobie = false,
    bool MaVyradene = true,
    bool PredvoleneVyradene = false,
    bool MaPodpis = false,
    bool MaNovuStranu = false,
    bool MaVolbuStlpcov = true,
    bool Dostupna = true,
    string? Poznamka = null);

/// <summary>Voľby konkrétnej zostavy.</summary>
public sealed class MoznostiZostavy
{
    public DruhZostavy Druh { get; init; }
    public IReadOnlyList<string> Stlpce { get; init; } = Array.Empty<string>();
    public bool NaSirku { get; init; }
    public bool VrataneVyradenych { get; init; }
    public DateTime? Od { get; init; }
    public DateTime? Do { get; init; }
    public Zoskupenie Zoskupenie { get; init; } = Zoskupenie.Kategoria;
    public bool Podpis { get; init; }
    public bool KazdaNaNovejStrane { get; init; }

    /// <summary>Len tieto položky (vyfiltrované v evidencii).</summary>
    public IReadOnlyCollection<int>? IdPoloziek { get; init; }

    public string? PopisFiltra { get; init; }

    /// <summary>Len jedna kategória, miestnosť alebo osoba (0 alebo null = všetky).</summary>
    public int? KategoriaId { get; init; }

    public int? MiestnostId { get; init; }
    public int? OsobaId { get; init; }

    /// <summary>História zmien len pre položku s týmto prírastkovým číslom.</summary>
    public int? PrirastkoveCislo { get; init; }
}

/// <summary>Tlačové zostavy. Každá sa vytvorí ako dokument, ktorý sa zobrazí v náhľade, vytlačí alebo uloží do PDF.</summary>
public sealed class ZostavyService
{
    private const string Cislo = StlpcePrenosu.Cislo;
    private const string Nazov = StlpcePrenosu.Nazov;
    private const string Kategoria = StlpcePrenosu.Kategoria;
    private const string Miestnost = StlpcePrenosu.Miestnost;
    private const string Pridelene = StlpcePrenosu.Pridelene;
    private const string Seriove = StlpcePrenosu.Seriove;
    private const string Inventarne = StlpcePrenosu.Inventarne;
    private const string Stav = StlpcePrenosu.Stav;
    private const string Cena = StlpcePrenosu.Cena;
    private const string Vyrobca = StlpcePrenosu.Vyrobca;
    private const string Model = StlpcePrenosu.Model;
    private const string DatumPridania = StlpcePrenosu.DatumPridania;

    public static IReadOnlyList<PopisZostavy> Zoznam { get; } = new PopisZostavy[]
    {
        new(DruhZostavy.SuhrnnyZoznam, "Súhrnný zoznam",
            "Všetky položky zoskupené podľa kategórie, miestnosti alebo pridelenia s počtami za skupinu a celkovým súhrnom.",
            new[] { Cislo, Nazov, Miestnost, Pridelene, Seriove, Stav, Cena }),
        new(DruhZostavy.Vyfiltrovane, "Vyfiltrované položky",
            "Presne to, čo je práve zobrazené v evidencii, s popisom použitého filtra.",
            new[] { Cislo, Nazov, Kategoria, Miestnost, Pridelene, Vyrobca, Model, Seriove, Stav }, MaVyradene: false),
        new(DruhZostavy.PoMiestnostiach, "Po miestnostiach",
            "Každá miestnosť na novej strane, voliteľne s riadkami na podpis – inventúrny zoznam miestnosti.",
            new[] { Cislo, Nazov, Kategoria, Pridelene, Seriove, Inventarne, Stav }, MaPodpis: true, MaNovuStranu: true),
        new(DruhZostavy.PoKategoriach, "Po kategóriách",
            "Každá kategória ako samostatná skupina s počtom kusov.",
            new[] { Cislo, Nazov, Miestnost, Pridelene, Vyrobca, Model, Seriove, Stav }, MaNovuStranu: true),
        new(DruhZostavy.PodlaPridelenia, "Podľa pridelenia",
            "Každá osoba na novej strane, voliteľne ako zoznam zverenej techniky s podpisom.",
            new[] { Cislo, Nazov, Kategoria, Miestnost, Seriove, Inventarne }, MaPodpis: true, MaNovuStranu: true),
        new(DruhZostavy.Prirastkovy, "Prírastkový zoznam",
            "Položky pridané v zvolenom období, zoradené podľa prírastkového čísla.",
            new[] { Cislo, DatumPridania, Nazov, Kategoria, Miestnost, Seriove, Inventarne, Cena }, MaObdobie: true, PredvoleneVyradene: true),
        new(DruhZostavy.PodlaDatumuPridania, "Podľa dátumu pridania",
            "Položky pridané od – do, zoradené podľa dátumu pridania.",
            new[] { DatumPridania, Cislo, Nazov, Kategoria, Miestnost, Pridelene, Cena }, MaObdobie: true, PredvoleneVyradene: true),
        new(DruhZostavy.Vyradene, "Vyradené položky",
            "Položky vyradené v zvolenom období s dátumom a dôvodom vyradenia.",
            new[] { Cislo, Nazov, Kategoria, Seriove, Inventarne, StlpcePrenosu.DatumVyradenia, StlpcePrenosu.DovodVyradenia, Cena },
            MaObdobie: true, MaVyradene: false),
        new(DruhZostavy.Historia, "História zmien",
            "Zmeny za obdobie, voliteľne len pre jednu položku, miestnosť alebo osobu.",
            Array.Empty<string>(), MaObdobie: true, MaVyradene: false, MaVolbuStlpcov: false),
        new(DruhZostavy.Kalendar, "Kalendár",
            "Mesačný kalendár alebo zoznam úloh za obdobie.",
            Array.Empty<string>(), MaVyradene: false, MaVolbuStlpcov: false, Dostupna: false, Poznamka: "Pripravuje sa v etape 6 spolu s kalendárom."),
    };

    public static PopisZostavy Popis(DruhZostavy druh) => Zoznam.First(z => z.Druh == druh);

    /// <summary>Navrhovaný názov súboru PDF, napr. „EviTA_po_miestnostiach_2026-10-20.pdf“.</summary>
    public static string NazovSuboru(DruhZostavy druh)
    {
        var slova = Sk.BezDiakritiky(Popis(druh).Nazov).Replace(' ', '_');
        return $"EviTA_{slova}_{DateTime.Now:yyyy-MM-dd}.pdf";
    }

    /// <summary>Odporúčaná orientácia: na šírku pri viac ako 6 stĺpcoch a pri histórii zmien.</summary>
    public static bool OdporucNaSirku(DruhZostavy druh, int pocetStlpcov) => druh == DruhZostavy.Historia || pocetStlpcov > 6;

    // ------------------------------------------------------------------ vytvorenie

    public Document Vytvor(MoznostiZostavy m)
    {
        if (!Popis(m.Druh).Dostupna)
        {
            throw new EvitaException(Popis(m.Druh).Poznamka ?? "Táto zostava zatiaľ nie je dostupná.");
        }

        if (m.Druh == DruhZostavy.Historia)
        {
            return Historia(m);
        }

        var stlpce = m.Stlpce.Select(StlpcePrenosu.Podla).ToList();
        if (stlpce.Count == 0)
        {
            throw new EvitaException("Vyberte aspoň jeden stĺpec.");
        }

        using var db = Databaza.Otvor();
        var polozky = ExportService.NacitajPolozky(db, m.IdPoloziek);

        // výber podľa druhu zostavy
        var od = m.Od?.Date;
        var po = m.Do?.Date.AddDays(1);
        polozky = m.Druh switch
        {
            DruhZostavy.Vyfiltrovane => polozky,
            DruhZostavy.Vyradene => polozky
                .Where(p => p.Stav == StavPolozky.Vyradene)
                .Where(p => od is null || (p.DatumVyradenia ?? p.DatumZmeny) >= od)
                .Where(p => po is null || (p.DatumVyradenia ?? p.DatumZmeny) < po)
                .ToList(),
            DruhZostavy.Prirastkovy or DruhZostavy.PodlaDatumuPridania => polozky
                .Where(p => m.VrataneVyradenych || p.Stav != StavPolozky.Vyradene)
                .Where(p => od is null || p.DatumPridania >= od)
                .Where(p => po is null || p.DatumPridania < po)
                .ToList(),
            _ => polozky.Where(p => m.VrataneVyradenych || p.Stav != StavPolozky.Vyradene).ToList(),
        };

        if (m.KategoriaId is > 0)
        {
            polozky = polozky.Where(p => p.KategoriaId == m.KategoriaId).ToList();
        }

        if (m.MiestnostId is > 0)
        {
            polozky = polozky.Where(p => p.MiestnostId == m.MiestnostId).ToList();
        }

        if (m.OsobaId is > 0)
        {
            polozky = polozky.Where(p => p.OsobaId == m.OsobaId).ToList();
        }

        var popis = Popis(m.Druh);
        var zostava = new PdfZostava(popis.Nazov, Podtitulok(m, polozky.Count), m.NaSirku, stlpce.Count > 10 ? 7 : 8);
        var hlavicky = stlpce.Select(PdfExport.Hlavicka).ToList();
        var vpravo = stlpce.Select(PdfExport.VPravo).ToList();
        string[] Riadok(Polozka p) => stlpce.Select(s => PdfZostava.TextBunky(StlpcePrenosu.Hodnota(p, s.Kluc), s.Typ)).ToArray();

        // šírky stĺpcov rovnaké pre všetky skupiny
        var vsetkyRiadky = polozky.Select(Riadok).ToList();
        var sirky = zostava.Sirky(hlavicky, vsetkyRiadky);

        void Tabulka(IReadOnlyList<Polozka> zoznam) =>
            zostava.Tabulka(hlavicky, vpravo, zoznam.Select(Riadok).ToList(), sirky, zoznam.Select(p => p.Stav == StavPolozky.Vyradene).ToList());

        if (polozky.Count == 0)
        {
            zostava.Text("Zostave nezodpovedá žiadna položka.", sedy: true);
            return zostava.Dokument;
        }

        switch (m.Druh)
        {
            case DruhZostavy.SuhrnnyZoznam:
            case DruhZostavy.PoMiestnostiach:
            case DruhZostavy.PoKategoriach:
            case DruhZostavy.PodlaPridelenia:
            {
                var zoskupenie = m.Druh switch
                {
                    DruhZostavy.PoMiestnostiach => Zoskupenie.Miestnost,
                    DruhZostavy.PoKategoriach => Zoskupenie.Kategoria,
                    DruhZostavy.PodlaPridelenia => Zoskupenie.Pridelenie,
                    _ => m.Zoskupenie,
                };
                var skupiny = Skupiny(polozky, zoskupenie);
                var suhrn = new List<(string Skupina, int Pocet, decimal Suma)>();
                var prva = true;
                foreach (var (nazov, clenovia) in skupiny)
                {
                    if (!prva && m.KazdaNaNovejStrane)
                    {
                        zostava.NovaStrana();
                    }

                    prva = false;
                    var suma = clenovia.Sum(p => p.Cena ?? 0);
                    zostava.NadpisSkupiny($"{NazovSkupiny(zoskupenie)}: {nazov}", PocetASuma(clenovia.Count, suma));
                    Tabulka(clenovia);
                    suhrn.Add((nazov, clenovia.Count, suma));

                    if (m.Podpis && zoskupenie == Zoskupenie.Miestnost)
                    {
                        zostava.Text($"Inventúra miestnosti {nazov}. Skutočný stav súhlasí so zoznamom (rozdiely sú vyznačené).", sedy: true)
                            .Format.SpaceBefore = Unit.FromPoint(10);
                        zostava.Podpis("Inventúru vykonal(a) – meno a podpis", "Zodpovedná osoba – meno a podpis", "Dátum");
                    }
                    else if (m.Podpis && zoskupenie == Zoskupenie.Pridelenie && clenovia[0].Osoba is { } osoba)
                    {
                        zostava.Text($"Potvrdzujem, že som prevzal(a) uvedenú techniku do užívania a zodpovedám za ňu. ({osoba.CeleMeno})", sedy: true)
                            .Format.SpaceBefore = Unit.FromPoint(10);
                        zostava.Podpis($"Prevzal(a): {osoba.CeleMeno}", "Odovzdal(a) – meno a podpis", "Dátum");
                    }
                }

                // súhrn za všetky skupiny
                if (m.KazdaNaNovejStrane && suhrn.Count > 1)
                {
                    zostava.NovaStrana();
                }

                zostava.NadpisSkupiny("Súhrn", PocetASuma(polozky.Count, polozky.Sum(p => p.Cena ?? 0)));
                var hlavickySuhrnu = new[] { NazovSkupiny(zoskupenie), "Počet", "Cena spolu" };
                var riadkySuhrnu = suhrn.Select(s => new[] { s.Skupina, s.Pocet.ToString(Sk.Kultura), PdfZostava.Suma(s.Suma) }).ToList();
                riadkySuhrnu.Add(new[] { "Spolu", polozky.Count.ToString(Sk.Kultura), PdfZostava.Suma(polozky.Sum(p => p.Cena ?? 0)) });
                var sirkaSuhrnu = Math.Min(zostava.SirkaTabulky, 14);
                zostava.Tabulka(hlavickySuhrnu, new[] { false, true, true }, riadkySuhrnu,
                    new[] { sirkaSuhrnu - 6, 2.5, 3.5 });
                break;
            }

            case DruhZostavy.PodlaDatumuPridania:
                Tabulka(polozky.OrderBy(p => p.DatumPridania).ThenBy(p => p.PrirastkoveCislo).ToList());
                zostava.Spolu("Spolu: " + PocetASuma(polozky.Count, polozky.Sum(p => p.Cena ?? 0)));
                break;

            case DruhZostavy.Vyradene:
                Tabulka(polozky.OrderBy(p => p.DatumVyradenia).ThenBy(p => p.PrirastkoveCislo).ToList());
                zostava.Spolu("Spolu: " + PocetASuma(polozky.Count, polozky.Sum(p => p.Cena ?? 0)));
                break;

            default:
                Tabulka(polozky);
                zostava.Spolu("Spolu: " + PocetASuma(polozky.Count, polozky.Sum(p => p.Cena ?? 0)));
                break;
        }

        return zostava.Dokument;
    }

    public void UlozPdf(MoznostiZostavy m, string cesta) => PdfZostava.UlozPdf(Vytvor(m), cesta);

    // ------------------------------------------------------------------ história zmien

    private Document Historia(MoznostiZostavy m)
    {
        using var db = Databaza.Otvor();
        var dotaz = db.Historia.AsNoTracking().AsQueryable();
        if (m.Od is { } od)
        {
            var zaciatok = od.Date;
            dotaz = dotaz.Where(h => h.Cas >= zaciatok);
        }

        if (m.Do is { } po)
        {
            var koniec = po.Date.AddDays(1);
            dotaz = dotaz.Where(h => h.Cas < koniec);
        }

        var obmedzenia = new List<string>();
        if (m.PrirastkoveCislo is { } cislo)
        {
            dotaz = dotaz.Where(h => h.PrirastkoveCislo == cislo);
            obmedzenia.Add($"položka č. {cislo}");
        }

        if (m.MiestnostId is > 0)
        {
            var nazov = db.Miestnosti.Where(x => x.Id == m.MiestnostId).Select(x => x.Nazov).FirstOrDefault() ?? "";
            var idPoloziek = db.Polozky.Where(p => p.MiestnostId == m.MiestnostId).Select(p => p.Id).ToList();
            dotaz = dotaz.Where(h => idPoloziek.Contains(h.PolozkaId)
                                     || (h.Pole == "Miestnosť" && (h.StaraHodnota == nazov || h.NovaHodnota == nazov)));
            obmedzenia.Add($"miestnosť {nazov}");
        }

        if (m.OsobaId is > 0)
        {
            var osoba = db.Osoby.Where(x => x.Id == m.OsobaId).Select(x => x.Meno + " " + x.Priezvisko).FirstOrDefault()?.Trim() ?? "";
            var idPoloziek = db.Polozky.Where(p => p.OsobaId == m.OsobaId).Select(p => p.Id).ToList();
            dotaz = dotaz.Where(h => idPoloziek.Contains(h.PolozkaId)
                                     || (h.Pole == "Pridelené" && (h.StaraHodnota == osoba || h.NovaHodnota == osoba)));
            obmedzenia.Add($"osoba {osoba}");
        }

        var zmeny = dotaz.OrderBy(h => h.Cas).ThenBy(h => h.Id).ToList();
        var podtitulok = Obdobie(m) + (obmedzenia.Count > 0 ? " · " + string.Join(", ", obmedzenia) : "")
                         + $" · {zmeny.Count} záznamov · vytlačené {Sk.DatumCas(DateTime.Now)}";
        var zostava = new PdfZostava(Popis(DruhZostavy.Historia).Nazov, podtitulok, m.NaSirku);
        if (zmeny.Count == 0)
        {
            zostava.Text("V zvolenom období nie sú žiadne zmeny.", sedy: true);
            return zostava.Dokument;
        }

        var hlavicky = new[] { "Čas", "Č.", "Položka", "Akcia", "Pole", "Pôvodná hodnota", "Nová hodnota", "Používateľ" };
        var riadky = zmeny.Select(h => new[]
        {
            h.Cas.ToString("d.M.yyyy H:mm", Sk.Kultura), h.PrirastkoveCislo.ToString(Sk.Kultura), h.NazovPolozky, h.Akcia,
            h.Pole ?? "", Bez(h.StaraHodnota), Bez(h.NovaHodnota), h.Pouzivatel,
        }).ToList();
        zostava.Tabulka(hlavicky, new[] { false, true, false, false, false, false, false, false }, riadky, zostava.Sirky(hlavicky, riadky));
        return zostava.Dokument;
    }

    private static string Bez(string? text) => (text ?? "").Replace("\r\n", " / ").Replace('\n', ' ');

    // ------------------------------------------------------------------ pomocné

    private static List<(string Nazov, List<Polozka> Clenovia)> Skupiny(List<Polozka> polozky, Zoskupenie zoskupenie)
    {
        string? Kluc(Polozka p) => zoskupenie switch
        {
            Zoskupenie.Kategoria => p.Kategoria?.Nazov,
            Zoskupenie.Miestnost => p.Miestnost?.Nazov,
            _ => p.Osoba?.CeleMeno,
        };

        var prazdna = zoskupenie switch
        {
            Zoskupenie.Kategoria => "(bez kategórie)",
            Zoskupenie.Miestnost => "(bez miestnosti)",
            _ => "(nepridelené)",
        };

        // osoby podľa priezviska, ostatné podľa názvu; prázdna skupina na koniec
        return polozky
            .GroupBy(p => Kluc(p) ?? "")
            .OrderBy(g => g.Key.Length == 0 ? 1 : 0)
            .ThenBy(g => zoskupenie == Zoskupenie.Pridelenie ? g.First().Osoba?.Priezvisko ?? g.Key : g.Key, Sk.Porovnanie)
            .ThenBy(g => g.Key, Sk.Porovnanie)
            .Select(g => (g.Key.Length == 0 ? prazdna : g.Key, g.OrderBy(p => p.PrirastkoveCislo).ToList()))
            .ToList();
    }

    private static string NazovSkupiny(Zoskupenie zoskupenie) => zoskupenie switch
    {
        Zoskupenie.Kategoria => "Kategória",
        Zoskupenie.Miestnost => "Miestnosť",
        _ => "Pridelené",
    };

    private static string PocetASuma(int pocet, decimal suma) =>
        suma > 0 ? $"{ExportService.PocetPoloziek(pocet)} · {PdfZostava.Suma(suma)}" : ExportService.PocetPoloziek(pocet);

    private static string Obdobie(MoznostiZostavy m) => (m.Od, m.Do) switch
    {
        ({ } od, { } po) => $"obdobie {Sk.Datum(od)} – {Sk.Datum(po)}",
        ({ } od, null) => $"od {Sk.Datum(od)}",
        (null, { } po) => $"do {Sk.Datum(po)}",
        _ => "celé obdobie",
    };

    private static string Podtitulok(MoznostiZostavy m, int pocet)
    {
        var casti = new List<string>();
        var popis = Popis(m.Druh);
        if (popis.MaObdobie)
        {
            casti.Add(Obdobie(m));
        }

        if (m.Druh == DruhZostavy.Vyfiltrovane)
        {
            casti.Add(string.IsNullOrWhiteSpace(m.PopisFiltra) ? "bez filtra" : "filter: " + m.PopisFiltra);
        }

        if (m.Druh == DruhZostavy.SuhrnnyZoznam)
        {
            casti.Add("zoskupené podľa: " + NazovSkupiny(m.Zoskupenie).ToLowerInvariant());
        }

        if (popis.MaVyradene)
        {
            casti.Add(m.VrataneVyradenych ? "vrátane vyradených" : "bez vyradených");
        }

        casti.Add(ExportService.PocetPoloziek(pocet));
        casti.Add("vytlačené " + Sk.DatumCas(DateTime.Now));
        return string.Join(" · ", casti);
    }
}
