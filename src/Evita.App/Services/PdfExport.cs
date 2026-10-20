using Evita.Models;

namespace Evita.Services;

/// <summary>Export tabuľky položiek do PDF – rovnaký vzhľad ako tlačové zostavy.</summary>
internal static class PdfExport
{
    public static void Zapis(string cesta, string titulok, string popis, IReadOnlyList<StlpecPrenosu> stlpce, IReadOnlyList<Polozka> polozky)
    {
        var hlavicky = stlpce.Select(Hlavicka).ToList();
        var riadky = polozky.Select(p => stlpce.Select(s => PdfZostava.TextBunky(StlpcePrenosu.Hodnota(p, s.Kluc), s.Typ)).ToArray()).ToList();
        var naSirku = PdfZostava.OdporucNaSirku(hlavicky, riadky);
        var zostava = new PdfZostava(titulok, popis, naSirku, stlpce.Count > 12 ? 7 : 8);
        zostava.Tabulka(hlavicky, stlpce.Select(VPravo).ToList(), riadky, zostava.Sirky(hlavicky, riadky),
            polozky.Select(p => p.Stav == StavPolozky.Vyradene).ToList());
        PdfZostava.UlozPdf(zostava.Dokument, cesta);
    }

    public static string Hlavicka(StlpecPrenosu stlpec) => stlpec.Kluc == StlpcePrenosu.Cislo ? Preklad.T("Č.") : stlpec.Nazov;

    public static bool VPravo(StlpecPrenosu stlpec) => stlpec.Typ is TypStlpca.CeleCislo or TypStlpca.Suma;
}
