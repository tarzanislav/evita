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
        MigraDoc.DocumentObjectModel.Document Tvorba() => new ZostavyService().Vytvor(new MoznostiZostavy
        {
            Druh = DruhZostavy.SuhrnnyZoznam,
            Stlpce = new[] { StlpcePrenosu.Cislo, StlpcePrenosu.Nazov, StlpcePrenosu.Kategoria, StlpcePrenosu.Miestnost, StlpcePrenosu.Stav },
            Zoskupenie = Zoskupenie.Miestnost,
        });
        var dokument = Tvorba();

        // bez pozadia: podklad hlavičky tabuľky (svetlomodrý) zmizne, text zostane
        int Podklad(MigraDoc.DocumentObjectModel.Document d)
        {
            var strana = NahladDokumentu.Strana(NahladDokumentu.Priprav(d), 1, false, out var velkost);
            var obraz = new RenderTargetBitmap((int)velkost.Width, (int)velkost.Height, 96, 96, PixelFormats.Pbgra32);
            obraz.Render(strana);
            var body = new byte[(int)velkost.Width * (int)velkost.Height * 4];
            obraz.CopyPixels(body, (int)velkost.Width * 4, 0);
            var pocet = 0;
            for (var i = 0; i < body.Length; i += 4)
            {
                if (Math.Abs(body[i] - 241) < 4 && Math.Abs(body[i + 1] - 230) < 4 && Math.Abs(body[i + 2] - 220) < 4)
                {
                    pocet++;
                }
            }

            return pocet;
        }

        var sPozadim = Podklad(Tvorba());
        var bezPozadia = Podklad(PozadieDokumentu.Odstran(Tvorba()));
        Over(sPozadim > 1000 && bezPozadia < sPozadim / 20, $"tlač bez pozadia: podklad hlavičky tabuľky {sPozadim} → {bezPozadia} bodov");

        var vykreslenie = NahladDokumentu.Priprav(dokument);
        var stran = NahladDokumentu.PocetStran(vykreslenie);
        var prva = NahladDokumentu.Logicka(vykreslenie, 1, false);
        Over(!prva.Obsah.IsEmpty && prva.Obsah.Width < prva.Velkost.Width * 0.97 && prva.Obsah.Width > prva.Velkost.Width * 0.5,
            $"tlač: obsah strany bez prázdnych okrajov ({prva.Obsah.Width / 96 * 25.4:0} × {prva.Obsah.Height / 96 * 25.4:0} mm zo 210 × 297 mm) – zväčšenie neoreže text");
        var okno = new TlacWindow(vykreslenie, false, "samotest", RezimOknaTlace.Tlac, Tvorba, null);
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
            ("bez_pozadia_siva", new VolbyTlace { BezPozadia = true, OdtieneSivej = true }, stran),
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

        // obojstranná tlač: párny počet strán, otočenie zadných strán, ručné otáčanie v dvoch úlohách
        var rozvrh = new NahladDokumentu.StrankovacRozvrhu(vykreslenie, false, new VolbyTlace { Obojstranne = true });
        var auto = TlacWindow.Ulohy(rozvrh, new VolbyTlace { Obojstranne = true, Kopie = 2 }, new ProfilObojstrannej { OtocitNaVysku = true });
        var rucne = TlacWindow.Ulohy(rozvrh, new VolbyTlace { Obojstranne = true, Kopie = 2 },
            new ProfilObojstrannej { Rezim = RezimObojstrannej.Rucne, ObratitPoradie = true });
        var jednostranne = TlacWindow.Ulohy(rozvrh, new VolbyTlace(), null);
        var parny = (stran + 1) / 2 * 2;
        Over(auto.Count == 1 && auto[0].Strany.Count == parny && auto[0].Duplex && auto[0].Kopie == 2
             && rucne.Count == 2 && rucne.All(u => !u.Duplex && u.Kopie == 1 && u.Strany.Count == parny)
             && jednostranne.Count == 1 && jednostranne[0].Strany.Count == stran && !jednostranne[0].Duplex,
            $"obojstranná tlač: automaticky 1 úloha ({parny} strán), ručne 2 úlohy po {parny} stranách (2 kópie), jednostranne {stran}");
        ProfilObojstrannej.Uloz("Testovacia tlačiareň", new ProfilObojstrannej { Rezim = RezimObojstrannej.Rucne, OtocitNaSirku = true, ObratitPoradie = true });
        Over(ProfilObojstrannej.Pre("testovacia TLAČIAREŇ") is { Rezim: RezimObojstrannej.Rucne, OtocitNaSirku: true, OtocitNaVysku: false, ObratitPoradie: true }
             && ProfilObojstrannej.Pre("Iná tlačiareň") is null, "obojstranná tlač: nastavenie sa pamätá pre každú tlačiareň zvlášť");
        var velkostA4 = RozvrhTlace.VelkostListu(false);
        UlozObrazok(new DrawingImage(ObojstrannaKresba.SkusobnaStrana(1, velkostA4, false).Drawing), "obojstranna_skusobny_list_1");
        UlozObrazok(new DrawingImage(ObojstrannaKresba.SkusobnaStrana(2, velkostA4, true).Drawing), "obojstranna_skusobny_list_2");
        UlozObrazok(new DrawingImage(ObojstrannaKresba.Otocena(ObojstrannaKresba.SkusobnaStrana(2, velkostA4, true), velkostA4).Drawing), "obojstranna_skusobny_list_2_otoceny");

        // sprievodca: úvod, otázky po automatickej a po ručnej tlači (bez skutočnej tlače)
        var sprievodca = new ObojstrannaWindow("Testovacia tlačiareň 2");
        sprievodca.Show();
        Pumpuj();
        Snimka(sprievodca, "okno_obojstranna_uvod");
        sprievodca.UkazOtazky(rucne: false);
        Pumpuj();
        Snimka(sprievodca, "okno_obojstranna_otazky");
        sprievodca.Odpovedz(0, 1);
        Over(sprievodca.Profil is { Rezim: RezimObojstrannej.Automaticky, OtocitNaVysku: false, OtocitNaSirku: true }
             && ProfilObojstrannej.Pre("Testovacia tlačiareň 2") is { OtocitNaSirku: true },
            "sprievodca: list na výšku rovno, na šírku hore nohami → otáčať len listy na šírku");
        sprievodca.UkazOtazky(rucne: true);
        Pumpuj();
        Snimka(sprievodca, "okno_obojstranna_rucne");
        sprievodca.Odpovedz(3);
        Over(sprievodca.Profil is { Rezim: RezimObojstrannej.Rucne, OtocitNaVysku: true, OtocitNaSirku: true, ObratitPoradie: true },
            "sprievodca: ručné otáčanie – číslo 4 hore nohami → obrátené poradie a otočenie");
        sprievodca.Close();
        Pumpuj();

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
