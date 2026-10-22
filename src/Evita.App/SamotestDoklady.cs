using System.IO;
using System.Text;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Evita;

/// <summary>
/// Samotest nových položiek z dokladu: text faktúry, webová objednávka, diktovanie, EAN, elektronické faktúry
/// (UBL, ISDOC, CII), e-faktúra vložená v PDF, OCR vygenerovanej faktúry a vytvorenie položiek v okne.
/// </summary>
internal static partial class Samotest
{
    private static string PopisDokladu(Doklad d) =>
        string.Join(" | ", d.Riadky.Select(r => $"{r.Nazov} ×{r.Pocet} {r.CenaKus?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "-"}"
                                                 + (r.Ean is null ? "" : " EAN " + r.Ean)));

    private static void SamotestDokladov()
    {
        var datum = DateTime.Today.AddDays(-8);
        var faktura = $"""
            Faktúra č. 2026104
            Dodávateľ: ABC Computers s.r.o.
            Dátum vystavenia: {datum:d.M.yyyy}
            Názov	Množstvo	Cena/ks	Spolu
            Notebook HP ProBook 450 G10	3 ks	780,00 €	2 340,00 €
            Monitor Dell P2422H	2 ks	159,90 €	319,80 €
            Myš Logitech M185	5 ks	12,50 €	62,50 €
            Spolu s DPH	2 722,30 €
            """;
        var doklad = DokladService.ZTextu(faktura, "test");
        Krok("doklad – text faktúry: " + PopisDokladu(doklad));
        Over(doklad.Riadky.Count == 3
             && doklad.Riadky[0] is { Nazov: "Notebook HP ProBook 450 G10", Pocet: 3, CenaKus: 780m }
             && doklad.Riadky[1] is { Nazov: "Monitor Dell P2422H", Pocet: 2, CenaKus: 159.9m }
             && doklad.Riadky[2] is { Pocet: 5, CenaKus: 12.5m },
            "doklad: riadky tabuľky faktúry (názov, počet, cena za kus, bez súčtov)");
        Over(doklad.Cislo == "2026104" && doklad.Datum == datum && doklad.Dodavatel == "ABC Computers s.r.o.",
            $"doklad: číslo, dátum a dodávateľ z hlavičky ({doklad.Cislo}, {doklad.Datum:d}, {doklad.Dodavatel})");

        var objednavka = """
            Váš košík
            Lenovo ThinkPad E14 Gen 5
            Počet: 2
            1 299,00 €
            Logitech MX Keys klávesnica
            1 ks
            109,90 €
            Doprava zdarma
            Celkom 2 707,90 €
            """;
        doklad = DokladService.ZTextu(objednavka, "test");
        Krok("doklad – webová objednávka: " + PopisDokladu(doklad));
        Over(doklad.Riadky.Count == 2
             && doklad.Riadky[0] is { Nazov: "Lenovo ThinkPad E14 Gen 5", Pocet: 2, CenaKus: 1299m }
             && doklad.Riadky[1] is { Nazov: "Logitech MX Keys klávesnica", Pocet: 1, CenaKus: 109.9m },
            "doklad: webová objednávka (názov, počet a cena na samostatných riadkoch)");

        doklad = DokladService.ZTextu("Tri notebooky HP ProBook po 780 eur, dva monitory Dell po 150 eur a jeden projektor Epson.", "test");
        Krok("doklad – diktovanie: " + PopisDokladu(doklad));
        Over(doklad.Riadky.Count == 3
             && doklad.Riadky[0] is { Pocet: 3, CenaKus: 780m } && doklad.Riadky[0].Nazov.StartsWith("Notebooky HP ProBook", StringComparison.Ordinal)
             && doklad.Riadky[1] is { Pocet: 2, CenaKus: 150m } && doklad.Riadky[1].Nazov.StartsWith("Monitory Dell", StringComparison.Ordinal)
             && doklad.Riadky[2] is { Pocet: 1, CenaKus: null } && doklad.Riadky[2].Nazov.StartsWith("Projektor Epson", StringComparison.Ordinal),
            "doklad: nadiktované položky (počet slovom, cena „po … eur“, položka bez ceny)");

        doklad = DokladService.ZTextu("2 tlačiarne Canon za 400 eur.\n5 slúchadiel Jabra po 35 €.", "test");
        Krok("doklad – diktovanie 2: " + PopisDokladu(doklad));
        Over(doklad.Riadky.Count == 2 && doklad.Riadky[0] is { Pocet: 2, CenaKus: 200m } && doklad.Riadky[1] is { Pocet: 5, CenaKus: 35m },
            "doklad: diktovanie s číslicami („za 400 eur“ spolu = 200 € za kus)");

        doklad = DokladService.ZTextu("8591234567890\n4006381333931\n", "test");
        Over(doklad.Riadky.Count == 2 && doklad.Riadky.All(r => r.Ean is { Length: 13 }), "doklad: naskenované EAN (každý na riadku)");

        // elektronické faktúry
        var ubl = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                     xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                     xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:ID>FV2026-0153</cbc:ID>
              <cbc:IssueDate>{datum:yyyy-MM-dd}</cbc:IssueDate>
              <cac:AccountingSupplierParty><cac:Party><cac:PartyName><cbc:Name>Datacomp s.r.o.</cbc:Name></cac:PartyName></cac:Party></cac:AccountingSupplierParty>
              <cac:InvoiceLine>
                <cbc:ID>1</cbc:ID>
                <cbc:InvoicedQuantity unitCode="H87">4</cbc:InvoicedQuantity>
                <cbc:LineExtensionAmount currencyID="EUR">2400.00</cbc:LineExtensionAmount>
                <cac:Item>
                  <cbc:Name>Notebook Lenovo ThinkPad L14 Gen 4</cbc:Name>
                  <cac:StandardItemIdentification><cbc:ID schemeID="0160">0196802345678</cbc:ID></cac:StandardItemIdentification>
                  <cac:ClassifiedTaxCategory><cbc:ID>S</cbc:ID><cbc:Percent>23</cbc:Percent></cac:ClassifiedTaxCategory>
                </cac:Item>
                <cac:Price><cbc:PriceAmount currencyID="EUR">600.00</cbc:PriceAmount></cac:Price>
              </cac:InvoiceLine>
              <cac:InvoiceLine>
                <cbc:ID>2</cbc:ID>
                <cbc:InvoicedQuantity unitCode="H87">1</cbc:InvoicedQuantity>
                <cbc:LineExtensionAmount currencyID="EUR">89.00</cbc:LineExtensionAmount>
                <cac:Item>
                  <cbc:Name>Dokovacia stanica Lenovo USB-C</cbc:Name>
                  <cac:ClassifiedTaxCategory><cbc:ID>S</cbc:ID><cbc:Percent>23</cbc:Percent></cac:ClassifiedTaxCategory>
                </cac:Item>
                <cac:Price><cbc:PriceAmount currencyID="EUR">89.00</cbc:PriceAmount></cac:Price>
              </cac:InvoiceLine>
            </Invoice>
            """;
        doklad = DokladService.ZXml(ubl, "ubl.xml")!;
        Krok("doklad – UBL: " + PopisDokladu(doklad));
        Over(doklad is { Cislo: "FV2026-0153", Dodavatel: "Datacomp s.r.o.", CenyBezDph: true, Riadky.Count: 2 } && doklad.Datum == datum
             && doklad.Riadky[0] is { Pocet: 4, CenaKus: 600m, Dph: 23m, Ean: "0196802345678" },
            "doklad: elektronická faktúra UBL (slovenská e-faktúra / Peppol)");

        var isdoc = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Invoice xmlns="http://isdoc.cz/namespace/2013" version="6.0.2">
              <ID>2026-77</ID>
              <IssueDate>{datum:yyyy-MM-dd}</IssueDate>
              <AccountingSupplierParty><Party><PartyName><Name>Alza.cz a.s.</Name></PartyName></Party></AccountingSupplierParty>
              <InvoiceLines>
                <InvoiceLine>
                  <ID>1</ID>
                  <InvoicedQuantity unitCode="ks">2</InvoicedQuantity>
                  <UnitPrice>100.00</UnitPrice>
                  <UnitPriceTaxInclusive>123.00</UnitPriceTaxInclusive>
                  <ClassifiedTaxCategory><Percent>23</Percent></ClassifiedTaxCategory>
                  <Item><Description>Webkamera Logitech C920</Description></Item>
                </InvoiceLine>
              </InvoiceLines>
            </Invoice>
            """;
        doklad = DokladService.ZXml(isdoc, "faktura.isdoc")!;
        Over(doklad is { Cislo: "2026-77", CenyBezDph: false, Riadky.Count: 1 } && doklad.Riadky[0] is { Nazov: "Webkamera Logitech C920", Pocet: 2, CenaKus: 123m },
            "doklad: ISDOC (cena s DPH) – " + PopisDokladu(doklad));

        var cii = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <rsm:CrossIndustryInvoice xmlns:rsm="urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100"
                xmlns:ram="urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100"
                xmlns:udt="urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100">
              <rsm:ExchangedDocument>
                <ram:ID>RE-2026-5</ram:ID>
                <ram:IssueDateTime><udt:DateTimeString format="102">{datum:yyyyMMdd}</udt:DateTimeString></ram:IssueDateTime>
              </rsm:ExchangedDocument>
              <rsm:SupplyChainTradeTransaction>
                <ram:IncludedSupplyChainTradeLineItem>
                  <ram:SpecifiedTradeProduct><ram:GlobalID schemeID="0160">4006381333931</ram:GlobalID><ram:Name>Projektor Epson EB-W51</ram:Name></ram:SpecifiedTradeProduct>
                  <ram:SpecifiedLineTradeAgreement><ram:NetPriceProductTradePrice><ram:ChargeAmount>450.00</ram:ChargeAmount></ram:NetPriceProductTradePrice></ram:SpecifiedLineTradeAgreement>
                  <ram:SpecifiedLineTradeDelivery><ram:BilledQuantity unitCode="H87">1</ram:BilledQuantity></ram:SpecifiedLineTradeDelivery>
                  <ram:SpecifiedLineTradeSettlement><ram:ApplicableTradeTax><ram:RateApplicablePercent>20</ram:RateApplicablePercent></ram:ApplicableTradeTax></ram:SpecifiedLineTradeSettlement>
                </ram:IncludedSupplyChainTradeLineItem>
                <ram:ApplicableHeaderTradeAgreement><ram:SellerTradeParty><ram:Name>Projektory GmbH</ram:Name></ram:SellerTradeParty></ram:ApplicableHeaderTradeAgreement>
              </rsm:SupplyChainTradeTransaction>
            </rsm:CrossIndustryInvoice>
            """;
        doklad = DokladService.ZXml(cii, "factur-x.xml")!;
        Over(doklad is { Cislo: "RE-2026-5", Dodavatel: "Projektory GmbH", CenyBezDph: true, Riadky.Count: 1 } && doklad.Datum == datum
             && doklad.Riadky[0] is { Nazov: "Projektor Epson EB-W51", CenaKus: 450m, Dph: 20m, Ean: "4006381333931" },
            "doklad: Factur-X / ZUGFeRD (CII) – " + PopisDokladu(doklad));
        Over(DokladService.ZXml("<html><body>nie</body></html>", "x.xml") is null && DokladService.ZXml("nie je xml", "x.xml") is null,
            "doklad: iné XML alebo text nie je e-faktúra");

        // PDF faktúry: vložená e-faktúra a text na rozpoznanie (OCR)
        PismaPdf.Zaregistruj();
        var priecinok = Path.Combine(Path.GetTempPath(), "EvitaSamotestDoklady");
        Directory.CreateDirectory(priecinok);
        var pdfSXml = Path.Combine(priecinok, "faktura_s_ubl.pdf");
        var pdfText = Path.Combine(priecinok, "faktura_sken.pdf");
        foreach (var (cesta, sXml) in new[] { (pdfSXml, true), (pdfText, false) })
        {
            using var pdf = new PdfDocument();
            var strana = pdf.AddPage();
            using (var g = XGraphics.FromPdfPage(strana))
            {
                var pismo = new XFont("Arial", 12);
                var tucne = new XFont("Arial", 16, XFontStyleEx.Bold);
                g.DrawString("Faktúra č. 2026104", tucne, XBrushes.Black, 50, 70);
                g.DrawString("Dodávateľ: ABC Computers s.r.o.", pismo, XBrushes.Black, 50, 100);
                g.DrawString($"Dátum vystavenia: {datum:d.M.yyyy}", pismo, XBrushes.Black, 50, 120);
                var y = 170;
                foreach (var (nazov, ks, cena, spolu) in new[]
                         {
                             ("Názov", "Množstvo", "Cena/ks", "Spolu"),
                             ("Notebook HP ProBook 450 G10", "3 ks", "780,00 €", "2 340,00 €"),
                             ("Monitor Dell P2422H", "2 ks", "159,90 €", "319,80 €"),
                             ("Projektor Epson EB-W51", "1 ks", "520,00 €", "520,00 €"),
                         })
                {
                    g.DrawString(nazov, pismo, XBrushes.Black, 50, y);
                    g.DrawString(ks, pismo, XBrushes.Black, 300, y);
                    g.DrawString(cena, pismo, XBrushes.Black, 390, y);
                    g.DrawString(spolu, pismo, XBrushes.Black, 480, y);
                    y += 26;
                }

                g.DrawString("Spolu s DPH: 3 179,80 €", tucne, XBrushes.Black, 300, y + 20);
            }

            if (sXml)
            {
                PdfNorma.Priloha(pdf, "faktura.xml", Encoding.UTF8.GetBytes(ubl), DateTime.Now, "/text/xml", "e-faktúra", "/Alternative");
            }

            pdf.Save(cesta);
        }

        var vlozene = DokladService.XmlZPdf(pdfSXml);
        Over(vlozene is not null && DokladService.ZXml(vlozene, "pdf") is { Riadky.Count: 2 }, "doklad: e-faktúra vložená v PDF sa nájde a prečíta");
        Over(DokladService.XmlZPdf(pdfText) is null, "doklad: PDF bez prílohy nemá e-faktúru (ide na OCR)");

        // OCR beží v pomocnom procese EviTA (--ocr)
        try
        {
            var (text, jazyk) = Cakaj(OcrDokladu.PrecitajAsync(pdfText));
            var ocr = DokladService.ZTextu(text, "ocr");
            Krok($"doklad – OCR ({jazyk}): „{text.Replace("\r", "").Replace("\n", " ⏎ ")}“ → {PopisDokladu(ocr)}");
            Over(ocr.Riadky.Count(r => r.CenaKus is 780m or 159.9m or 520m) >= 2,
                $"doklad: OCR faktúry v PDF v pomocnom procese ({jazyk}) – rozpoznané riadky s cenou");
        }
        catch (EvitaException ex)
        {
            Krok("doklad – OCR: preskočené – " + ex.Message);
        }

        // okno: rozpoznanie, kategórie, vytvorenie položiek
        var okno = new NovePolozkyWindow();
        List<int> nove;
        try
        {
            okno.Show();
            Pumpuj();
            okno.NastavText(faktura, "test");
            Pumpuj();
            var notebook = okno.Polozky.FirstOrDefault(r => r.Nazov.StartsWith("Notebook", StringComparison.Ordinal));
            var kategoriaNotebooku = okno.Kategorie.FirstOrDefault(k => k.Id == notebook?.KategoriaId)?.Nazov;
            Over(okno.Polozky.Count == 3 && notebook is { Vyrobca: "HP" } && kategoriaNotebooku is not null
                 && Sk.BezDiakritiky(kategoriaNotebooku).Contains("notebook"),
                $"okno nové položky: rozpoznané riadky, výrobca a kategória ({notebook?.Vyrobca} {notebook?.Model}, {kategoriaNotebooku}) – {okno.StavText}");
            Over(okno.UhadniKategoriu("Lenovo ThinkPad E14") == notebook?.KategoriaId && okno.UhadniKategoriu("Xyz 123") is null,
                "okno nové položky: kategória podľa typového radu (ThinkPad = notebook)");
            Snimka(okno, "okno_nove_polozky");

            // úprava v tabuľke zostane po opätovnom rozpoznaní; nevybraný riadok sa nevytvorí
            okno.Polozky[2].Pridat = false;
            okno.Polozky[1].Model = "P2422H";
            okno.RozpoznajText();
            Over(!okno.Polozky[2].Pridat && okno.Polozky[1].Model == "P2422H", "okno nové položky: úpravy v tabuľke zostanú po novom rozpoznaní textu");
            nove = okno.VytvoritPolozky();
            Over(nove.Count == 5, $"okno nové položky: vytvorených {nove.Count} položiek (3 notebooky + 2 monitory)");

            // e-faktúra: ceny bez DPH – pripočíta sa DPH
            okno.NastavText("", "test");
            var xml = Path.Combine(priecinok, "faktura.xml");
            File.WriteAllText(xml, ubl);
            CakajNa(okno.OtvorSuborAsync(xml));
            Pumpuj();
            Over(okno.Polozky.Count == 2 && okno.PripocitatDph.IsChecked == true && okno.Poznamka.Text.Contains("FV2026-0153"),
                "okno nové položky: e-faktúra zo súboru (DPH sa pripočíta, poznámka s číslom dokladu) – " + okno.StavText);
            Snimka(okno, "okno_nove_polozky_efaktura");
        }
        finally
        {
            okno.Close();
            Pumpuj();
        }

        using (var db = Databaza.Otvor())
        {
            var polozky = db.Polozky.Where(p => nove.Contains(p.Id)).ToList();
            var notebooky = polozky.Where(p => p.Nazov == "Notebook HP ProBook 450 G10").ToList();
            Over(notebooky.Count == 3 && notebooky.All(p => p.Cena == 780m && p.DatumNakupu == datum && p.ZarukaDo == datum.AddYears(2)
                                                          && p.Vyrobca == "HP" && p.Poznamka != null && p.Poznamka.Contains("2026104"))
                 && polozky.Count(p => p.Model == "P2422H" && p.Cena == 159.9m) == 2,
                "nové položky z dokladu: cena, dátum nákupu, záruka, výrobca a poznámka sú v evidencii");
        }

        new PolozkaService().Vymaz(nove);
        Directory.Delete(priecinok, true);
        Krok("nové položky z dokladu v poriadku");
    }
}
