using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;

namespace Evita;

/// <summary>Samotest okna tlače: rozmiestnenie strán na list, opakovanie, brožúra, zväčšenie, rámik, sivá, dierovač.</summary>
internal static partial class Samotest
{
    private static void SamotestOknaTlace()
    {
        // rozdelenie strán zostavy na listy
        int?[] L(params int?[] s) => s;
        bool Rovnake(List<int?[]> a, params int?[][] b) => a.Count == b.Length && a.Zip(b).All(p => p.First.SequenceEqual(p.Second));
        Over(Rovnake(RozvrhTlace.Rozdel(5, new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Dve }), L(1, 2), L(3, 4), L(5, null)),
            "tlač: 2 strany na list");
        Over(RozvrhTlace.Rozdel(5, new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Styri }).Count == 2
             && RozvrhTlace.Rozdel(9, new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Osem }).Count == 2, "tlač: 4 a 8 strán na list");
        Over(Rovnake(RozvrhTlace.Rozdel(2, new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Styri, OpakovatStranky = true }.Opravene()), L(1, 1, 1, 1), L(2, 2, 2, 2)),
            "tlač: opakovanie strán (1 strana 4× na liste)");
        Over(Rovnake(RozvrhTlace.Rozdel(5, new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Brozura }), L(null, 1), L(2, null), L(null, 3), L(4, 5)),
            "tlač: brožúra (poradie strán na preloženie, prázdne strany doplnené)");
        Over(new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Brozura, Dierovac = true, OpakovatStranky = true }.Opravene() is { Dierovac: false, OpakovatStranky: false, TlacitObojstranne: true }
             && new VolbyTlace { Zvacsenie = 5, DierovacMm = -3, Kopie = 0 }.Opravene() is { Zvacsenie: 1.6, DierovacMm: 0, Kopie: 1 },
            "tlač: neplatné voľby sa opravia (brožúra obojstranne, bez dierovača)");
        Over(RozvrhTlace.Mriezka(Rozmiestnenie.Dve, false) == (2, 1, true) && RozvrhTlace.Mriezka(Rozmiestnenie.Osem, false) == (4, 2, true)
             && RozvrhTlace.Mriezka(Rozmiestnenie.Styri, true) == (2, 2, true), "tlač: orientácia listu podľa počtu strán na list");

        // okno tlače so zostavou – náhľady listov sa uložia ako obrázky (výstupy samotestu)
        var dokument = new ZostavyService().Vytvor(new MoznostiZostavy
        {
            Druh = DruhZostavy.SuhrnnyZoznam,
            Stlpce = new[] { StlpcePrenosu.Cislo, StlpcePrenosu.Nazov, StlpcePrenosu.Kategoria, StlpcePrenosu.Miestnost, StlpcePrenosu.Stav },
            Zoskupenie = Zoskupenie.Miestnost,
        });
        var vykreslenie = NahladDokumentu.Priprav(dokument);
        var stran = NahladDokumentu.PocetStran(vykreslenie);
        var okno = new TlacWindow(vykreslenie, false, "samotest");
        okno.Show();
        Pumpuj();
        var pripady = new (string Nazov, VolbyTlace Volby, int Listov)[]
        {
            ("1_strana", new VolbyTlace(), stran),
            ("2_strany_ramcek", new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Dve, Ramcek = true }, (stran + 1) / 2),
            ("2_strany_zvacsenie", new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Dve, Zvacsenie = 1.25, Ramcek = true }, (stran + 1) / 2),
            ("4_strany_opakovat", new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Styri, OpakovatStranky = true, Ramcek = true }, stran),
            ("8_stran", new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Osem, Ramcek = true }, (stran + 7) / 8),
            ("brozura", new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Brozura, Ramcek = true }, (stran + 3) / 4 * 2),
            ("siva_dierovac", new VolbyTlace { OdtieneSivej = true, Dierovac = true, DierovacMm = 20, Obojstranne = true }, stran),
        };
        foreach (var (nazov, volby, listov) in pripady)
        {
            okno.NastavVolby(volby);
            Pumpuj();
            Over(okno.PocetListov == listov && okno.Volby.Rozmiestnenie == volby.Rozmiestnenie,
                $"okno tlače: {nazov.Replace('_', ' ')} – {stran} strán zostavy → {okno.PocetListov} strán listov");
            UlozObrazok(okno.ObrazokListu(0), $"tlac_{nazov}");
        }

        Snimka(okno, "okno_tlace");
        okno.UlozVolby();
        Over(VolbyTlace.Nacitaj() is { OdtieneSivej: true, Dierovac: true, DierovacMm: 20 }, "okno tlače: voľby sa zapamätajú");
        okno.NastavVolby(new VolbyTlace());
        okno.UlozVolby();
        okno.Close();
        Pumpuj();

        // tlačový lístok pre tlačiarne nainštalované na počítači (len záznam – zostavovací počítač nemusí mať žiadnu)
        try
        {
            using var server = new LocalPrintServer();
            foreach (var fronta in server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections }).Take(4))
            {
                using (fronta)
                {
                    var tiket = NahladDokumentu.Tiket(fronta, new VolbyTlace { Obojstranne = true, Kopie = 2, OdtieneSivej = true }, false);
                    Krok($"tlačiareň {fronta.FullName}: lístok A4 {tiket.PageOrientation}, {tiket.Duplexing}, kópie {tiket.CopyCount}, {tiket.OutputColor}");
                }
            }
        }
        catch (Exception ex)
        {
            Krok($"tlačiarne sa nedajú zistiť: {ex.GetBaseException().Message}");
        }

        Krok("okno tlače v poriadku");
    }

    private static void UlozObrazok(ImageSource? obrazok, string nazov)
    {
        if (obrazok is null)
        {
            return;
        }

        var mierka = 1100 / Math.Max(obrazok.Width, obrazok.Height);
        var sirka = (int)(obrazok.Width * mierka);
        var vyska = (int)(obrazok.Height * mierka);
        var vizual = new DrawingVisual();
        using (var dc = vizual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, sirka, vyska));
            dc.DrawImage(obrazok, new Rect(0, 0, sirka, vyska));
        }

        var bitmapa = new RenderTargetBitmap(sirka, vyska, 96, 96, PixelFormats.Pbgra32);
        bitmapa.Render(vizual);
        var kodovac = new PngBitmapEncoder();
        kodovac.Frames.Add(BitmapFrame.Create(bitmapa));
        var priecinok = Path.Combine(Databaza.Priecinok, "Export");
        Directory.CreateDirectory(priecinok);
        using var subor = File.Create(Path.Combine(priecinok, nazov + ".png"));
        kodovac.Save(subor);
    }
}
