using System.IO;
using ClosedXML.Excel;
using Evita.Data;
using Evita.Models;

namespace Evita.Services;

/// <summary>Vzorové súbory pre import (Dokumenty\EviTA\Vzory) – vždy v aktuálnom formáte programu.</summary>
public static class VzoryService
{
    public const string Excel = "vzor_import.xlsx";
    public const string Csv = "vzor_import.csv";
    public const string Json = "vzor_import.json";
    public const string Xml = "vzor_import.xml";

    private static readonly string[] Subory = { Excel, Csv, Json, Xml, XsdEviTA.NazovSuboru };

    /// <summary>Dokumenty\EviTA\Vzory (pri teste v testovacom priečinku).</summary>
    public static string Priecinok =>
        Path.Combine(Path.GetDirectoryName(Databaza.PriecinokZaloh.TrimEnd(Path.DirectorySeparatorChar)) ?? Databaza.Priecinok, "Vzory");

    public static bool Existuju => Subory.All(s => File.Exists(Path.Combine(Priecinok, s)));

    /// <summary>Vytvorí (prepíše) všetky vzorové súbory. Vráti priečinok so vzormi.</summary>
    public static string Vytvor()
    {
        var priecinok = Priecinok;
        Directory.CreateDirectory(priecinok);
        var polozky = Ukazky();
        var stlpce = StlpcePrenosu.Vsetky
            .Where(s => s.Kluc is not (StlpcePrenosu.DatumZmeny or StlpcePrenosu.Identifikator))
            .ToList();
        var riadky = ExportService.Riadky(polozky, stlpce);

        // Excel s výberovými zoznamami a návodom
        using (var zosit = new XLWorkbook())
        {
            var harok = ExportService.PridajHarok(zosit, "Položky", stlpce, riadky);

            var zoznamy = zosit.Worksheets.Add("Zoznamy");
            var kategorie = NazvyKategorii();
            for (var i = 0; i < kategorie.Count; i++)
            {
                zoznamy.Cell(i + 1, 1).Value = kategorie[i];
            }

            var stavy = StavyPoloziek.Vsetky.Select(s => s.Nazov).ToList();
            for (var i = 0; i < stavy.Count; i++)
            {
                zoznamy.Cell(i + 1, 2).Value = stavy[i];
            }

            zoznamy.Visibility = XLWorksheetVisibility.Hidden;

            var stlpecKategorie = stlpce.FindIndex(s => s.Kluc == StlpcePrenosu.Kategoria) + 1;
            var vyberKategorie = harok.Range(2, stlpecKategorie, 2000, stlpecKategorie).CreateDataValidation();
            vyberKategorie.List(zoznamy.Range(1, 1, kategorie.Count, 1), true);
            vyberKategorie.ErrorStyle = XLErrorStyle.Information;
            vyberKategorie.ErrorTitle = "Nová kategória";
            vyberKategorie.ErrorMessage = "Kategória nie je v zozname. Pri importe sa vytvorí nová.";

            var stlpecStavu = stlpce.FindIndex(s => s.Kluc == StlpcePrenosu.Stav) + 1;
            var vyberStavu = harok.Range(2, stlpecStavu, 2000, stlpecStavu).CreateDataValidation();
            vyberStavu.List(zoznamy.Range(1, 2, stavy.Count, 2), true);
            vyberStavu.ErrorStyle = XLErrorStyle.Stop;
            vyberStavu.ErrorTitle = "Neznámy stav";
            vyberStavu.ErrorMessage = "Vyberte stav zo zoznamu: v prevádzke, v sklade, v oprave, vyradené.";

            var navod = zosit.Worksheets.Add("Návod");
            navod.Cell(1, 1).Value = "Ako vyplniť súbor na import do EviTA";
            navod.Cell(1, 1).Style.Font.Bold = true;
            navod.Cell(1, 1).Style.Font.FontSize = 14;
            navod.Cell(2, 1).Value = "Na hárku Položky je jedna položka v každom riadku. Povinný je len názov. Ukážkové riadky prepíšte vlastnými údajmi. " +
                                     "Stĺpce, ktoré nepotrebujete, môžete vymazať; názvy stĺpcov nemeňte. Dátumy píšte ako 20.10.2026.";
            navod.Range(2, 1, 2, 4).Merge().Style.Alignment.WrapText = true;
            navod.Row(2).Height = 45;
            string[] hlavicka = { "Stĺpec", "Povinný", "Popis", "Príklad" };
            for (var c = 0; c < hlavicka.Length; c++)
            {
                navod.Cell(4, c + 1).Value = hlavicka[c];
            }

            var riadok = 5;
            foreach (var s in StlpcePrenosu.Vsetky)
            {
                navod.Cell(riadok, 1).Value = s.Nazov;
                navod.Cell(riadok, 2).Value = s.Povinny ? "áno" : "nie";
                navod.Cell(riadok, 3).Value = s.Popis;
                navod.Cell(riadok, 4).Value = s.Priklad;
                riadok++;
            }

            var hl = navod.Range(4, 1, 4, 4);
            hl.Style.Font.Bold = true;
            hl.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
            navod.Column(1).Width = 22;
            navod.Column(2).Width = 9;
            navod.Column(3).Width = 90;
            navod.Column(4).Width = 26;
            navod.Range(5, 3, riadok, 3).Style.Alignment.WrapText = true;
            zosit.Worksheet("Položky").SetTabActive();
            zosit.SaveAs(Path.Combine(priecinok, Excel));
        }

        ExportService.ZapisCsv(Path.Combine(priecinok, Csv), stlpce, riadky);

        var balik = new BalikEviTAPolozky
        {
            Program = $"EviTA {AktualizaciaService.AktualnaVerzia}",
            Vytvorene = Prevod.TextIso(DateTime.Now, true),
            Kategorie = polozky.Select(p => p.Kategoria!.Nazov).Distinct().Select(n => new KategoriaPrenos { Nazov = n }).ToList(),
            Miestnosti = polozky.Where(p => p.Miestnost is not null).Select(p => p.Miestnost!).DistinctBy(m => m.Nazov)
                .Select(m => new MiestnostPrenos { Nazov = m.Nazov, Budova = m.Budova }).ToList(),
            Osoby = polozky.Where(p => p.Osoba is not null).Select(p => p.Osoba!).DistinctBy(o => o.CeleMeno)
                .Select(o => new OsobaPrenos { Meno = o.Meno, Priezvisko = o.Priezvisko, Funkcia = o.Funkcia }).ToList(),
            Polozky = polozky.Select(p =>
            {
                var prenos = PolozkaPrenos.Z(p);
                prenos.Guid = null;
                prenos.DatumZmeny = null;
                return prenos;
            }).ToList(),
        };
        ExportService.ZapisJson(Path.Combine(priecinok, Json), balik);
        ExportService.ZapisXml(Path.Combine(priecinok, Xml), balik);
        return priecinok;
    }

    /// <summary>Vytvorí vzory, ak niektorý chýba. Chyba sa ignoruje (vzory nie sú nevyhnutné).</summary>
    public static void VytvorAkChybaju()
    {
        try
        {
            if (!Existuju)
            {
                Vytvor();
            }
        }
        catch (Exception)
        {
            // vzory sa dajú vytvoriť aj neskôr tlačidlom
        }
    }

    private static List<string> NazvyKategorii()
    {
        try
        {
            using var db = Databaza.Otvor();
            var nazvy = db.Kategorie.Where(k => k.Aktivna).Select(k => k.Nazov).ToList();
            if (nazvy.Count > 0)
            {
                return nazvy.OrderBy(n => n, Sk.Porovnanie).ToList();
            }
        }
        catch (Exception)
        {
            // bez databázy použijeme počiatočný zoznam
        }

        return Databaza.PociatocneKategorie.OrderBy(n => n, Sk.Porovnanie).ToList();
    }

    private static List<Polozka> Ukazky()
    {
        var kancelaria = new Miestnost { Nazov = "Kancelária 101", Budova = "A" };
        var sekretariat = new Miestnost { Nazov = "Sekretariát", Budova = "A" };
        var ucebna = new Miestnost { Nazov = "Učebňa 12", Budova = "B" };
        var sklad = new Miestnost { Nazov = "Sklad", Budova = "B" };
        var novak = new Osoba { Meno = "Ján", Priezvisko = "Novák", Funkcia = "účtovník" };
        var kovacova = new Osoba { Meno = "Mária", Priezvisko = "Kováčová", Funkcia = "učiteľka" };

        return new List<Polozka>
        {
            new()
            {
                PrirastkoveCislo = 1, Nazov = "Notebook Dell Latitude 5540", Kategoria = new Kategoria { Nazov = "notebook" },
                Miestnost = kancelaria, Osoba = novak, Vyrobca = "Dell", Model = "Latitude 5540", SerioveCislo = "5CD1234XYZ",
                InventarneCislo = "INV-2026-001", Stav = StavPolozky.VPrevadzke, DatumNakupu = new DateTime(2026, 3, 15), Cena = 1089.00m,
                ZarukaDo = new DateTime(2029, 3, 15), DatumPridania = new DateTime(2026, 3, 16), Poznamka = "nabíjačka v taške",
            },
            new()
            {
                PrirastkoveCislo = 2, Nazov = "Tlačiareň HP LaserJet Pro M404dn", Kategoria = new Kategoria { Nazov = "tlačiareň" },
                Miestnost = sekretariat, Vyrobca = "HP", Model = "LaserJet Pro M404dn", SerioveCislo = "PHBQK12345",
                InventarneCislo = "INV-2025-014", Stav = StavPolozky.VPrevadzke, DatumNakupu = new DateTime(2025, 11, 4), Cena = 289.90m,
                ZarukaDo = new DateTime(2027, 11, 4), DalsiaRevizia = new DateTime(2027, 5, 1), DatumPridania = new DateTime(2025, 11, 5),
            },
            new()
            {
                PrirastkoveCislo = 3, Nazov = "Projektor Epson EB-W51", Kategoria = new Kategoria { Nazov = "projektor" },
                Miestnost = ucebna, Osoba = kovacova, Vyrobca = "Epson", Model = "EB-W51", SerioveCislo = "X5LK0123456",
                Stav = StavPolozky.VOprave, DatumNakupu = new DateTime(2024, 9, 2), Cena = 459.00m, ZarukaDo = new DateTime(2026, 9, 2),
                DatumPridania = new DateTime(2024, 9, 3), Poznamka = "nesvieti lampa, v servise od 10. 10. 2026",
            },
            new()
            {
                PrirastkoveCislo = 4, Nazov = "Monitor Dell P2422H", Kategoria = new Kategoria { Nazov = "príslušenstvo" },
                Miestnost = sklad, Vyrobca = "Dell", Model = "P2422H", SerioveCislo = "CN-0ABC12-34567", Stav = StavPolozky.VSklade,
                DatumNakupu = new DateTime(2026, 1, 20), Cena = 179.00m, ZarukaDo = new DateTime(2029, 1, 20), DatumPridania = new DateTime(2026, 1, 21),
            },
            new()
            {
                PrirastkoveCislo = 5, Nazov = "PC Lenovo ThinkCentre M70q", Kategoria = new Kategoria { Nazov = "PC" },
                Miestnost = ucebna, Vyrobca = "Lenovo", Model = "ThinkCentre M70q Gen 2", SerioveCislo = "MJ0ABCDE",
                Stav = StavPolozky.Vyradene, DatumNakupu = new DateTime(2019, 8, 26), Cena = 649.00m, ZarukaDo = new DateTime(2022, 8, 26),
                DatumPridania = new DateTime(2019, 8, 27), DatumVyradenia = new DateTime(2026, 6, 30), DovodVyradenia = "poškodená základná doska",
            },
        };
    }
}
