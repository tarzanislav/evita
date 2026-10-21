using System.IO;
using System.Text;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;
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
