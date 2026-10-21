using System.IO;
using System.Text;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Evita;

/// <summary>Samotest ukladania do PDF: strany na list, brožúra, sivá, zosvetlenie, šifrovanie, heslo a zákazy.</summary>
internal static partial class Samotest
{
    private static void SamotestPdf()
    {
        var povodne = VolbyPdf.Nacitaj();
        var priecinok = Path.Combine(Databaza.Priecinok, "Export");
        Directory.CreateDirectory(priecinok);
        MigraDoc.DocumentObjectModel.Document Zostava() => new ZostavyService().Vytvor(new MoznostiZostavy
        {
            Druh = DruhZostavy.SuhrnnyZoznam,
            Stlpce = new[] { StlpcePrenosu.Cislo, StlpcePrenosu.Nazov, StlpcePrenosu.Kategoria, StlpcePrenosu.Miestnost, StlpcePrenosu.Stav },
            Zoskupenie = Zoskupenie.Miestnost,
        });
        var stran = NahladDokumentu.PocetStran(NahladDokumentu.Priprav(Zostava()));
        string Text(string cesta) => Encoding.Latin1.GetString(File.ReadAllBytes(cesta));
        int Stran(string cesta, string? heslo = null)
        {
            using var dokument = heslo is null
                ? PdfReader.Open(cesta, PdfDocumentOpenMode.Import)
                : PdfReader.Open(cesta, heslo, PdfDocumentOpenMode.Import);
            return dokument.PageCount;
        }

        string Uloz(string nazov, VolbyPdf volby)
        {
            var cesta = Path.Combine(priecinok, $"samotest_pdf_{nazov}.pdf");
            PdfVystup.Uloz(Zostava(), cesta, volby);
            return cesta;
        }

        try
        {
            VolbyPdf.Heslo = null;
            VolbyPdf.HesloVlastnika = null;

            // bez volieb: priame PDF, písma vložené, text sa dá kopírovať (ToUnicode), bez šifrovania
            var obycajne = Uloz("obycajne", new VolbyPdf());
            var text = Text(obycajne);
            Over(Stran(obycajne) == stran && !text.Contains("/Encrypt") && text.Contains("/FontFile") && text.Contains("/ToUnicode"),
                $"PDF: {stran} strán, písma vložené, text sa dá kopírovať, nešifrované");

            // strany na list, brožúra, sivá, zosvetlenie (vektorovo – písma a text zostávajú)
            var pripady = new (string Nazov, VolbyTlace Volby, int Listov)[]
            {
                ("2_strany", new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Dve, Ramcek = true }, (stran + 1) / 2),
                ("4_strany_siva", new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Styri, OdtieneSivej = true }, (stran + 3) / 4),
                ("8_stran", new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Osem }, (stran + 7) / 8),
                ("brozura", new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Brozura, Ramcek = true }, (stran + 3) / 4 * 2),
                ("zosvetlit_dierovac", new VolbyTlace { Zosvetlit = true, Dierovac = true, DierovacMm = 20 }, stran),
                ("bez_pozadia", new VolbyTlace { BezPozadia = true }, stran),
            };
            foreach (var (nazov, volby, listov) in pripady)
            {
                var cesta = Uloz(nazov, new VolbyPdf { Rozvrh = volby });
                var obsah = Text(cesta);
                Over(Stran(cesta) == listov && obsah.Contains("/FontFile") && obsah.Contains("/ToUnicode")
                     && (!volby.OdtieneSivej || obsah.Contains("/Saturation")),
                    $"PDF: {nazov.Replace('_', ' ')} – {stran} strán zostavy → {listov} strán PDF, text zostáva textom");
            }

            // šifrovanie bez hesla, zákazy, heslo na otvorenie
            var sifrovane = Uloz("sifrovane_zakazy", new VolbyPdf { ZakazTlace = true, ZakazKopirovania = true });
            Over(Text(sifrovane).Contains("/Encrypt") && Stran(sifrovane) == stran, "PDF: zákazy tlače a kopírovania → zašifrované, otvorí sa bez hesla");

            var chyba = "";
            try
            {
                Uloz("bez_hesla", new VolbyPdf { PouzitHeslo = true });
            }
            catch (EvitaException ex)
            {
                chyba = ex.Message;
            }

            Over(chyba.Length > 0, "PDF: heslo zvolené, ale nezadané → upozornenie, PDF sa neuloží");

            VolbyPdf.Heslo = "EviTA-test";
            var sHeslom = Uloz("heslo", new VolbyPdf { PouzitHeslo = true, Rozvrh = new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Dve } });
            var bezHeslaOtvorene = true;
            try
            {
                Stran(sHeslom);
            }
            catch (Exception)
            {
                bezHeslaOtvorene = false;
            }

            Over(Text(sHeslom).Contains("/Encrypt") && !bezHeslaOtvorene && Stran(sHeslom, "EviTA-test") == (stran + 1) / 2,
                "PDF: heslo na otvorenie – bez hesla sa neotvorí, s heslom áno (2 strany na list)");

            // okno Uložiť ako PDF s náhľadom a okno Nastavenia PDF (Tlač a zostavy, Import a export)
            var okno = new TlacWindow(NahladDokumentu.Priprav(Zostava()), false, "samotest", RezimOknaTlace.Pdf, Zostava, "samotest.pdf")
            {
                CestaBezDialogu = Path.Combine(priecinok, "samotest_pdf_z_okna.pdf"),
            };
            okno.Show();
            Pumpuj();
            okno.NastavPdf(new VolbyPdf
            {
                PouzitHeslo = true,
                ZakazUprav = true,
                Rozvrh = new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Dve, OdtieneSivej = true, Ramcek = true },
            }, "okno-heslo");
            Pumpuj();
            Over(okno.PocetListov == (stran + 1) / 2, $"okno PDF: 2 strany na list → {okno.PocetListov} strán PDF");
            Snimka(okno, "okno_ulozit_pdf");
            UlozObrazok(okno.ObrazokListu(0), "pdf_nahlad_2_strany_siva");
            var zOkna = okno.UlozitPdf();
            okno.Close();
            Pumpuj();
            Over(zOkna is { Length: > 0 } && File.Exists(zOkna) && Text(zOkna).Contains("/Encrypt") && Stran(zOkna, "okno-heslo") == (stran + 1) / 2,
                "okno PDF: uložené zašifrované PDF s heslom");
            var ulozene = VolbyPdf.Nacitaj();
            string json;
            using (var db = Databaza.Otvor())
            {
                json = Databaza.Citaj(db, VolbyPdf.KlucNastavenia) ?? "";
            }

            Over(ulozene is { PouzitHeslo: true, ZakazUprav: true, Rozvrh.Rozmiestnenie: Rozmiestnenie.Dve, Rozvrh.OdtieneSivej: true }
                 && !json.Contains("okno-heslo"), "okno PDF: voľby sa zapamätajú, heslo sa do databázy neukladá");

            // záložky (miestnosti) v bežnom PDF
            Over(Text(obycajne).Contains("/Outlines") && Text(obycajne).Contains("/UseOutlines"), "PDF: záložky miestností (panel Záložky sa otvorí)");

            // PDF/A-2b, PDF/A-3b s dátami v Exceli, PDF/X-4 (súbory overí v CI aj veraPDF)
            var a2 = Uloz("pdfa2b", new VolbyPdf { Format = FormatVystupu.PdfA2b });
            var a2Text = Text(a2);
            Over(Stran(a2) == stran && a2Text.Contains("/GTS_PDFA1") && a2Text.Contains("<pdfaid:part>2</pdfaid:part>")
                 && a2Text.Contains("/Outlines") && !a2Text.Contains("/Encrypt") && !a2Text.Contains("/Interpolate true"),
                "PDF/A-2b: farebný profil sRGB, metadáta s označením normy, záložky, bez šifrovania");
            var a2Siva = Uloz("pdfa2b_2_strany_siva", new VolbyPdf
            {
                Format = FormatVystupu.PdfA2b,
                Rozvrh = new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Dve, OdtieneSivej = true, Zosvetlit = true, Ramcek = true },
            });
            Over(Stran(a2Siva) == (stran + 1) / 2 && Text(a2Siva).Contains("<pdfaid:part>2</pdfaid:part>"), "PDF/A-2b: aj 2 strany na list v odtieňoch sivej");

            var a3 = Uloz("pdfa3b", new VolbyPdf { Format = FormatVystupu.PdfA3b, PouzitHeslo = true, ZakazTlace = true });
            var a3Text = Text(a3);
            var tabulka = "";
            try
            {
                using var citany = PdfReader.Open(a3, PdfDocumentOpenMode.Import);
                var spec = citany.Internals.Catalog.Elements.GetArray("/AF")!.Elements.GetDictionary(0)!;
                var vlozeny = spec.Elements.GetDictionary("/EF")!.Elements.GetDictionary("/F")!;
                using var kniha = new ClosedXML.Excel.XLWorkbook(new MemoryStream(vlozeny.Stream.UnfilteredValue));
                tabulka = string.Join(" | ", kniha.Worksheet(1).RangeUsed()!.Rows().Take(10).Select(r => string.Join(";", r.Cells().Select(c => c.GetString()))));
            }
            catch (Exception ex)
            {
                tabulka = "chyba: " + ex.GetBaseException().Message;
            }

            Krok($"PDF/A-3b: vložený Excel – {tabulka}");
            Over(a3Text.Contains("<pdfaid:part>3</pdfaid:part>") && a3Text.Contains("/AFRelationship") && !a3Text.Contains("/Encrypt")
                 && tabulka.Contains("Miestnos"), "PDF/A-3b: vložená tabuľka v Exceli, heslo a zákazy sa pri norme nepoužijú");

            try
            {
                var x4 = Uloz("pdfx4", new VolbyPdf { Format = FormatVystupu.PdfX4 });
                var x4Text = Text(x4);
                Over(Stran(x4) == stran && x4Text.Contains("/GTS_PDFX") && x4Text.Contains("PDF/X-4") && x4Text.Contains("/TrimBox")
                     && x4Text.Contains("/DefaultRGB") && x4Text.Contains("/Trapped"), "PDF/X-4: výstupný zámer CMYK, orezová veľkosť, farby označené profilom");
            }
            catch (EvitaException ex)
            {
                Over(false, "PDF/X-4: " + ex.Message);
            }

            // obrázky PNG a TIFF
            int Sirka(string cesta) => System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri(cesta), System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0].PixelWidth;
            var png = PdfVystup.Uloz(Zostava(), Path.Combine(priecinok, "samotest_png.png"), new VolbyPdf { Format = FormatVystupu.Png });
            Over(png.Count == stran && png.All(File.Exists) && Math.Abs(Sirka(png[0]) - 2480) <= 2,
                $"PNG: {png.Count} obrázkov 2480 bodov na šírku (A4, 300 dpi)");
            var png2 = PdfVystup.Uloz(Zostava(), Path.Combine(priecinok, "samotest_png_2_strany.png"),
                new VolbyPdf { Format = FormatVystupu.Png, Rozvrh = new VolbyTlace { Rozmiestnenie = Rozmiestnenie.Dve, Ramcek = true } });
            Over(png2.Count == (stran + 1) / 2 && Math.Abs(Sirka(png2[0]) - 3508) <= 2, $"PNG: 2 strany na list → {png2.Count} obrázkov na šírku");
            var tif = PdfVystup.Uloz(Zostava(), Path.Combine(priecinok, "samotest_tiff.tif"),
                new VolbyPdf { Format = FormatVystupu.Tiff, Rozvrh = new VolbyTlace { OdtieneSivej = true } });
            var tifStran = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri(tif[0]), System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames.Count;
            Over(tif.Count == 1 && tifStran == stran, $"TIFF: jeden súbor s {tifStran} stranami (odtiene sivej)");
            var pdfZPng = PdfVystup.Uloz(Zostava(), Path.Combine(priecinok, "samotest_pdf_pripona.pdf"), new VolbyPdf { Format = FormatVystupu.Png });
            Over(pdfZPng.Count == 1 && Text(pdfZPng[0]).StartsWith("%PDF", StringComparison.Ordinal), "cesta s príponou .pdf vždy dá PDF (export)");

            // okno: formát PDF/A-3b – zabezpečenie sa vypne
            var oknoA3 = new TlacWindow(NahladDokumentu.Priprav(Zostava()), false, "samotest", RezimOknaTlace.Pdf, Zostava, "samotest.pdf");
            oknoA3.Show();
            Pumpuj();
            oknoA3.NastavPdf(new VolbyPdf { Format = FormatVystupu.PdfA3b, PouzitHeslo = true }, null);
            Pumpuj();
            Snimka(oknoA3, "okno_ulozit_pdf_a3b");
            var zOknaA3 = oknoA3.VytvorSubory();
            oknoA3.NastavPdf(new VolbyPdf { Format = FormatVystupu.Png }, null);
            Pumpuj();
            Snimka(oknoA3, "okno_ulozit_png");
            oknoA3.Close();
            Pumpuj();
            Over(zOknaA3 is [{ Mime: "application/pdf" } a3Okno] && Encoding.Latin1.GetString(a3Okno.Data).Contains("<pdfaid:part>3</pdfaid:part>"),
                "okno PDF: PDF/A-3b bez hesla (heslo pri norme nie je potrebné)");

            var nastavenia = new TlacWindow(null, false, "PDF", RezimOknaTlace.NastaveniaPdf, null, null);
            nastavenia.Show();
            Pumpuj();
            Snimka(nastavenia, "okno_nastavenia_pdf");
            nastavenia.Close();
            Pumpuj();
        }
        finally
        {
            VolbyPdf.Heslo = null;
            VolbyPdf.HesloVlastnika = null;
            povodne.Uloz();
        }

        Krok("ukladanie do PDF v poriadku");
    }
}
