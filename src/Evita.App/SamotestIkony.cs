using System.IO;
using Evita.Data;
using Evita.Models;
using Evita.Services;

namespace Evita;

/// <summary>Samotest ikon kategórií: sada, priradenie podľa názvu, vlastná ikona a farba, stĺpec v evidencii a v zostave.</summary>
internal static partial class Samotest
{
    private static void SamotestIkonKategorii(string exportPriecinok)
    {
        var sada = Services.IkonyKategorii.Vsetky;
        Over(sada.Count >= 30 && sada.Select(i => i.Kluc).Distinct().Count() == sada.Count
             && sada.All(i => !i.Geometria.Bounds.IsEmpty && i.Geometria.Bounds.Width <= i.Mierka + 0.5 && i.Geometria.Bounds.Height <= i.Mierka + 0.5)
             && sada.Select(i => i.PredvolenaFarba).Distinct().Count() == sada.Count,
            $"sada ikon kategórií: {sada.Count} ikon, každá s vlastnou farbou a platnou kresbou");

        // počiatočné kategórie majú vlastnú ikonu (nie „Iné“) a navzájom rôzne
        var pociatocne = Databaza.PociatocneKategorie.Select(k => (Kategoria: Preklad.T(k), Ikona: Services.IkonyKategorii.PodlaNazvu(Preklad.T(k)))).ToList();
        Over(pociatocne.All(p => p.Ikona.Kluc != Services.IkonyKategorii.Predvolena) && pociatocne.Select(p => p.Ikona.Kluc).Distinct().Count() == pociatocne.Count,
            "počiatočné kategórie: " + string.Join(", ", pociatocne.Select(p => $"{p.Kategoria} → {p.Ikona.Kluc}")));

        (string Nazov, string Ikona)[] nazvy =
        {
            ("Multifunkčná tlačiareň", "multifunkcia"), ("Notebooky", "notebook"), ("Záložný zdroj UPS", "ups"), ("Monitory", "monitor"),
            ("Myš a klávesnica", "prislusenstvo"), ("NAS", "disk"), ("Wi-Fi access point", "wifi"), ("IP telefón", "ip_telefon"),
            ("Mobilné telefóny", "mobil"), ("Dokovacia stanica", "dok"), ("Kancelárska stolička", "ine"),
        };
        foreach (var (nazov, ikona) in nazvy)
        {
            var najdena = Services.IkonyKategorii.PodlaNazvu(nazov).Kluc;
            Over(najdena == ikona, $"ikona podľa názvu kategórie „{nazov}“ → {ikona} (je {najdena})");
        }

        // obrázok na tlač – aj z vlákna na pozadí
        var png = Services.IkonyKategorii.Png(Services.IkonyKategorii.Pre("router", null, null));
        var pngPozadie = Task.Run(() => Services.IkonyKategorii.Png(Services.IkonyKategorii.Pre("notebook", "#16A34A", null))).Result;
        Over(png.Length > 200 && pngPozadie.Length > 200 && png[1] == (byte)'P' && pngPozadie[1] == (byte)'P', "ikona ako obrázok PNG (aj na pozadí)");
        File.WriteAllBytes(Path.Combine(exportPriecinok, "ikona_router.png"), png);

        // vlastná ikona a farba kategórie
        var ciselniky = new CiselnikService();
        var kategorie = ciselniky.Nacitaj(TypCiselnika.Kategorie);
        Over(kategorie.All(k => k.Ikona is not null), "číselník kategórií: každá kategória má ikonu");
        var prva = kategorie.First(k => k.Pocet > 0);
        ciselniky.NastavIkonu(prva.Id, "server", "#111827");
        var upravena = ciselniky.Nacitaj(TypCiselnika.Kategorie).First(k => k.Id == prva.Id);
        var riadok = new PolozkaService().NacitajRiadky().First(r => r.KategoriaId == prva.Id);
        Over(upravena.Ikona!.Ikona.Kluc == "server" && upravena.Ikona.Farba == "#111827" && riadok.IkonaKategorie?.Ikona.Kluc == "server",
            $"vlastná ikona a farba kategórie „{prva.Nazov}“ sa uloží a ukáže v evidencii");
        ciselniky.NastavIkonu(prva.Id, null, "neplatná");
        Over(ciselniky.Nacitaj(TypCiselnika.Kategorie).First(k => k.Id == prva.Id) is { IkonaKluc: null, IkonaFarba: null } spat
             && spat.Ikona!.Ikona.Kluc == Services.IkonyKategorii.PodlaNazvu(prva.Nazov).Kluc,
            "návrat na automatickú ikonu a predvolenú farbu (neplatná farba sa neuloží)");
        Over(new PolozkaService().NacitajRiadky().All(r => r.IkonaKategorie is not null), "evidencia: každá položka má ikonu kategórie");

        // tlačová zostava s ikonou a bez nej
        var zostavy = new ZostavyService();
        var sIkonou = Path.Combine(exportPriecinok, "zostava_ikony_kategorii.pdf");
        var bezIkony = Path.Combine(exportPriecinok, "zostava_bez_ikon.pdf");
        var stlpce = new[] { StlpcePrenosu.Cislo, StlpcePrenosu.Nazov, StlpcePrenosu.Kategoria, StlpcePrenosu.Miestnost, StlpcePrenosu.Stav };
        zostavy.UlozPdf(new MoznostiZostavy { Druh = DruhZostavy.SuhrnnyZoznam, Stlpce = stlpce, Zoskupenie = Zoskupenie.Miestnost }, sIkonou);
        zostavy.UlozPdf(new MoznostiZostavy { Druh = DruhZostavy.SuhrnnyZoznam, Stlpce = stlpce, Zoskupenie = Zoskupenie.Miestnost, IkonaKategorie = false }, bezIkony);
        var pdfSIkonou = File.ReadAllText(sIkonou, System.Text.Encoding.Latin1);
        var pdfBezIkony = File.ReadAllText(bezIkony, System.Text.Encoding.Latin1);
        static int Obrazky(string pdf) => System.Text.RegularExpressions.Regex.Matches(pdf, @"/Subtype\s*/Image").Count;
        Over(Obrazky(pdfSIkonou) > Obrazky(pdfBezIkony),
            $"zostava s ikonami kategórií ({new FileInfo(sIkonou).Length / 1024} kB) a bez nich ({new FileInfo(bezIkony).Length / 1024} kB)");
        var uz = new UlozenaZostava();
        Over(uz.IkonaKategorie && new MoznostiZostavy().IkonaKategorie, "ikona kategórie je v zostavách predvolene zapnutá");
        Krok("ikony kategórií v poriadku");
    }
}
