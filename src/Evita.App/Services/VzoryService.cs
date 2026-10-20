using System.IO;
using System.Runtime.InteropServices;
using ClosedXML.Excel;
using Evita.Data;
using Evita.Models;

namespace Evita.Services;

/// <summary>Jeden vzorový súbor tak, ako sa ponúka v programe na stiahnutie.</summary>
public sealed record VzorovySubor(string Nazov, string Popis, string Subor);

/// <summary>
/// Vzorové súbory pre import – vytvára ich priamo program (vždy v aktuálnom formáte) a ponúka ich na stiahnutie.
/// Pracovná kópia je v Dokumenty\EviTA\Vzory.
/// </summary>
public static class VzoryService
{
    public const string Excel = "EviTA_vzor_Excel.xlsx";
    public const string Csv = "EviTA_vzor_CSV.csv";
    public const string Json = "EviTA_vzor_JSON.json";
    public const string Xml = "EviTA_vzor_XML.xml";

    private static readonly string[] Subory = { Excel, Csv, Json, Xml, XsdEviTA.NazovSuboru };

    /// <summary>Názvy zo starších verzií – pri vytvorení vzorov sa odstránia.</summary>
    private static readonly string[] StareSubory = { "vzor_import.xlsx", "vzor_import.csv", "vzor_import.json", "vzor_import.xml" };

    /// <summary>Vzory v poradí a s názvami formátov ako pri exporte.</summary>
    public static IReadOnlyList<VzorovySubor> Zoznam => new VzorovySubor[]
    {
        new("Excel (.xlsx)", Preklad.T("s návodom a výberovými zoznamami"), Excel),
        new("CSV (.csv)", Preklad.T("UTF-8, oddeľovač bodkočiarka"), Csv),
        new("JSON (.json)", Preklad.T("úplná štruktúra s číselníkmi"), Json),
        new("XML (.xml)", Preklad.T("spolu s popisom formátu evita.xsd"), Xml),
    };

    /// <summary>Priečinok Stiahnuté (pri teste v testovacom priečinku).</summary>
    public static string PriecinokStiahnutych
    {
        get
        {
            if (Environment.GetEnvironmentVariable("EVITA_DATA_DIR") is { Length: > 0 } test)
            {
                return Path.Combine(test, "Stiahnute");
            }

            try
            {
                var id = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
                if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var cesta) == 0 && !string.IsNullOrEmpty(cesta))
                {
                    return cesta;
                }
            }
            catch (Exception)
            {
                // náhradná cesta nižšie
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint priznaky, IntPtr token, out string cesta);

    /// <summary>
    /// Uloží vzor do priečinka Stiahnuté (ak tam súbor s rovnakým názvom už je, pridá číslo „(2)“).
    /// Pri XML uloží vedľa aj popis formátu evita.xsd. Vráti cestu k uloženému súboru.
    /// </summary>
    public static string Stiahni(VzorovySubor vzor)
    {
        var zdroj = Vytvor();
        var ciel = PriecinokStiahnutych;
        Directory.CreateDirectory(ciel);

        var zaklad = Path.GetFileNameWithoutExtension(vzor.Subor);
        var pripona = Path.GetExtension(vzor.Subor);
        var cesta = Path.Combine(ciel, vzor.Subor);
        for (var i = 2; File.Exists(cesta); i++)
        {
            cesta = Path.Combine(ciel, $"{zaklad} ({i}){pripona}");
        }

        File.Copy(Path.Combine(zdroj, vzor.Subor), cesta);
        if (vzor.Subor == Xml)
        {
            File.Copy(Path.Combine(zdroj, XsdEviTA.NazovSuboru), Path.Combine(ciel, XsdEviTA.NazovSuboru), overwrite: true);
        }

        return cesta;
    }

    /// <summary>Dokumenty\EviTA\Vzory (pri teste v testovacom priečinku).</summary>
    public static string Priecinok =>
        Path.Combine(Path.GetDirectoryName(Databaza.PriecinokZaloh.TrimEnd(Path.DirectorySeparatorChar)) ?? Databaza.Priecinok, "Vzory");

    public static bool Existuju => Subory.All(s => File.Exists(Path.Combine(Priecinok, s)));

    /// <summary>Vytvorí (prepíše) všetky vzorové súbory. Vráti priečinok so vzormi.</summary>
    public static string Vytvor()
    {
        var priecinok = Priecinok;
        Directory.CreateDirectory(priecinok);
        foreach (var stary in StareSubory)
        {
            try
            {
                File.Delete(Path.Combine(priecinok, stary));
            }
            catch (Exception)
            {
                // starý vzor môže byť otvorený – nevadí
            }
        }

        var polozky = Ukazky();
        var stlpce = StlpcePrenosu.Vsetky
            .Where(s => s.Kluc is not (StlpcePrenosu.DatumZmeny or StlpcePrenosu.Identifikator))
            .ToList();
        var riadky = ExportService.Riadky(polozky, stlpce);

        // Excel s výberovými zoznamami a návodom
        using (var zosit = new XLWorkbook())
        {
            var harok = ExportService.PridajHarok(zosit, Preklad.T("Položky"), stlpce, riadky);

            var zoznamy = zosit.Worksheets.Add(Preklad.T("Zoznamy"));
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
            vyberKategorie.ErrorTitle = Preklad.T("Nová kategória");
            vyberKategorie.ErrorMessage = Preklad.T("Kategória nie je v zozname. Pri importe sa vytvorí nová.");

            var stlpecStavu = stlpce.FindIndex(s => s.Kluc == StlpcePrenosu.Stav) + 1;
            var vyberStavu = harok.Range(2, stlpecStavu, 2000, stlpecStavu).CreateDataValidation();
            vyberStavu.List(zoznamy.Range(1, 2, stavy.Count, 2), true);
            vyberStavu.ErrorStyle = XLErrorStyle.Stop;
            vyberStavu.ErrorTitle = Preklad.T("Neznámy stav");
            vyberStavu.ErrorMessage = Preklad.T("Vyberte stav zo zoznamu: v prevádzke, v sklade, v oprave, vyradené.");

            var navod = zosit.Worksheets.Add(Preklad.T("Návod"));
            navod.Cell(1, 1).Value = Preklad.T("Ako vyplniť súbor na import do EviTA");
            navod.Cell(1, 1).Style.Font.Bold = true;
            navod.Cell(1, 1).Style.Font.FontSize = 14;
            navod.Cell(2, 1).Value = Preklad.T("Na hárku Položky je jedna položka v každom riadku. Povinný je len názov.");
            navod.Cell(3, 1).Value = Preklad.T("Ukážkové riadky prepíšte vlastnými údajmi. Stĺpce, ktoré nepotrebujete, môžete vymazať; názvy stĺpcov nemeňte.");
            navod.Cell(4, 1).Value = Preklad.T("Dátumy píšte ako 20.10.2026, ceny ako 1089,00. Kategóriu a stav vyberiete zo zoznamu v bunke.");
            string[] hlavicka = { Preklad.T("Stĺpec"), Preklad.T("Povinný"), Preklad.T("Popis"), Preklad.T("Príklad") };
            for (var c = 0; c < hlavicka.Length; c++)
            {
                navod.Cell(6, c + 1).Value = hlavicka[c];
            }

            var riadok = 7;
            foreach (var s in StlpcePrenosu.Vsetky)
            {
                navod.Cell(riadok, 1).Value = s.Nazov;
                navod.Cell(riadok, 2).Value = s.Povinny ? Preklad.T("áno") : Preklad.T("nie");
                navod.Cell(riadok, 3).Value = s.Popis;
                navod.Cell(riadok, 4).Value = s.Priklad;
                riadok++;
            }

            var hl = navod.Range(6, 1, 6, 4);
            hl.Style.Font.Bold = true;
            hl.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
            navod.Column(1).Width = 22;
            navod.Column(2).Width = 9;
            navod.Column(3).Width = 90;
            navod.Column(4).Width = 26;
            navod.Range(7, 3, riadok, 3).Style.Alignment.WrapText = true;
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
        var kancelaria = new Miestnost { Nazov = Preklad.T("Kancelária 101"), Budova = "A" };
        var sekretariat = new Miestnost { Nazov = Preklad.T("Sekretariát"), Budova = "A" };
        var ucebna = new Miestnost { Nazov = Preklad.T("Učebňa 12"), Budova = "B" };
        var sklad = new Miestnost { Nazov = Preklad.T("Sklad"), Budova = "B" };
        var novak = new Osoba { Meno = "Ján", Priezvisko = "Novák", Funkcia = Preklad.T("účtovník") };
        var kovacova = new Osoba { Meno = "Mária", Priezvisko = "Kováčová", Funkcia = Preklad.T("učiteľka") };

        return new List<Polozka>
        {
            new()
            {
                PrirastkoveCislo = 1, Nazov = Preklad.T("Notebook Dell Latitude 5540"), Kategoria = new Kategoria { Nazov = Preklad.T("notebook") },
                Miestnost = kancelaria, Osoba = novak, Vyrobca = "Dell", Model = "Latitude 5540", SerioveCislo = "5CD1234XYZ",
                InventarneCislo = "INV-2026-001", Stav = StavPolozky.VPrevadzke, DatumNakupu = new DateTime(2026, 3, 15), Cena = 1089.00m,
                ZarukaDo = new DateTime(2029, 3, 15), DatumPridania = new DateTime(2026, 3, 16), Poznamka = Preklad.T("nabíjačka v taške"),
            },
            new()
            {
                PrirastkoveCislo = 2, Nazov = Preklad.T("Tlačiareň HP LaserJet Pro M404dn"), Kategoria = new Kategoria { Nazov = Preklad.T("tlačiareň") },
                Miestnost = sekretariat, Vyrobca = "HP", Model = "LaserJet Pro M404dn", SerioveCislo = "PHBQK12345",
                InventarneCislo = "INV-2025-014", Stav = StavPolozky.VPrevadzke, DatumNakupu = new DateTime(2025, 11, 4), Cena = 289.90m,
                ZarukaDo = new DateTime(2027, 11, 4), DalsiaRevizia = new DateTime(2027, 5, 1), DatumPridania = new DateTime(2025, 11, 5),
            },
            new()
            {
                PrirastkoveCislo = 3, Nazov = Preklad.T("Projektor Epson EB-W51"), Kategoria = new Kategoria { Nazov = Preklad.T("projektor") },
                Miestnost = ucebna, Osoba = kovacova, Vyrobca = "Epson", Model = "EB-W51", SerioveCislo = "X5LK0123456",
                Stav = StavPolozky.VOprave, DatumNakupu = new DateTime(2024, 9, 2), Cena = 459.00m, ZarukaDo = new DateTime(2026, 9, 2),
                DatumPridania = new DateTime(2024, 9, 3), Poznamka = Preklad.T("nesvieti lampa, v servise od 10. 10. 2026"),
            },
            new()
            {
                PrirastkoveCislo = 4, Nazov = Preklad.T("Monitor Dell P2422H"), Kategoria = new Kategoria { Nazov = Preklad.T("príslušenstvo") },
                Miestnost = sklad, Vyrobca = "Dell", Model = "P2422H", SerioveCislo = "CN-0ABC12-34567", Stav = StavPolozky.VSklade,
                DatumNakupu = new DateTime(2026, 1, 20), Cena = 179.00m, ZarukaDo = new DateTime(2029, 1, 20), DatumPridania = new DateTime(2026, 1, 21),
            },
            new()
            {
                PrirastkoveCislo = 5, Nazov = Preklad.T("PC Lenovo ThinkCentre M70q"), Kategoria = new Kategoria { Nazov = "PC" },
                Miestnost = ucebna, Vyrobca = "Lenovo", Model = "ThinkCentre M70q Gen 2", SerioveCislo = "MJ0ABCDE",
                Stav = StavPolozky.Vyradene, DatumNakupu = new DateTime(2019, 8, 26), Cena = 649.00m, ZarukaDo = new DateTime(2022, 8, 26),
                DatumPridania = new DateTime(2019, 8, 27), DatumVyradenia = new DateTime(2026, 6, 30), DovodVyradenia = Preklad.T("poškodená základná doska"),
            },
        };
    }
}
