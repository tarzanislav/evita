using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Evita.Services;

/// <summary>Riadok dokladu (faktúra, objednávka, diktovanie) – kandidát na novú položku evidencie.</summary>
public sealed record RiadokDokladu(string Nazov, int Pocet, decimal? CenaKus, decimal? Dph = null, string? Ean = null);

/// <summary>Rozpoznaný doklad: riadky a údaje z hlavičky (číslo, dátum, dodávateľ).</summary>
public sealed record Doklad(IReadOnlyList<RiadokDokladu> Riadky, string? Cislo, DateTime? Datum, string? Dodavatel, string Zdroj, bool CenyBezDph = false);

/// <summary>
/// Rozpoznanie položiek z dokladov: elektronická faktúra (UBL – slovenská e-faktúra/Peppol, ISDOC, Factur-X/ZUGFeRD CII),
/// aj vložená v PDF; inak text (skopírovaná webová objednávka, text z PDF/OCR, diktovanie) podľa vzorov riadkov s cenou
/// a počtom kusov.
/// </summary>
public static partial class DokladService
{
    // suma: desatinná časť alebo mena (inak by to mohlo byť číslo modelu – napr. „ProBook 450“)
    [GeneratedRegex(@"(?<![\w,.])(?<cela>\d{1,3}(?:[   .]\d{3})+|\d+)(?:(?<des>[,.]\d{1,2})(?!\d))?\s*(?<mena>€|eur(?:a|á|o)?\b|EUR\b|Eur\b)?",
        RegexOptions.IgnoreCase)]
    private static partial Regex SumaRegex();

    [GeneratedRegex(@"(?<![\w,.])(?<n>\d{1,4})\s*(?:ks|kus|kusy|kusov|pc|pcs|x|×)(?![\p{L}\d])", RegexOptions.IgnoreCase)]
    private static partial Regex PocetRegex();

    [GeneratedRegex(@"^\s*(?:(?:počet|pocet|množstvo|mnozstvo|qty|quantity|kusov|ks)\s*[:.]?\s*)?(?<n>\d{1,3})\s*(?:ks|kus|kusy|kusov|pcs|x)?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex PocetRiadokRegex();

    [GeneratedRegex(@"(?<![\w,.])\d{1,2}(?:[,.]\d+)?\s*%")]
    private static partial Regex PercentoRegex();

    [GeneratedRegex(@"^\s*(?:\d{1,3}[.)]?|[-•*·])\s+(?=\p{L})")]
    private static partial Regex PoradieRegex();

    [GeneratedRegex(@"^\s*(\d{8}|\d{12,14})\s*$")]
    private static partial Regex EanRegex();

    [GeneratedRegex(@"(\d{1,2})\.\s?(\d{1,2})\.\s?(\d{4})")]
    private static partial Regex DatumRegex();

    [GeneratedRegex(@"(?:faktúr\w*|faktur\w*|invoice|doklad\w*|objednávk\w*|objednavk\w*|order)\s*(?:č\.|číslo|cislo|no\.?|nr\.?|#)?\s*[:\-]?\s*(?<c>[A-Z0-9][A-Z0-9/\-]{2,})",
        RegexOptions.IgnoreCase)]
    private static partial Regex CisloRegex();

    /// <summary>Riadky, ktoré nie sú tovar (súčty, DPH, doprava, platba).</summary>
    private static readonly string[] Suctove =
    {
        "spolu", "celkom", "sucet", "suma", "zaklad", "dph", "zaokruhl", "k uhrade", "uhrada", "zaloha", "doprava", "dopravne",
        "postovne", "balne", "zlava", "total", "subtotal", "shipping", "vat", "tax", "platba", "preprava", "dobierka", "celkem",
        "uhradit", "medzisucet", "iban", "swift", "variabiln", "konstantn", "splatnost", "datum", "ico", "dic", "ic dph", "cislo uctu", "strana",
    };

    /// <summary>Riadky hlavičky tabuľky.</summary>
    private static readonly string[] Hlavicky = { "nazov", "popis", "mnozstvo", "jednotk", "cena za", "polozka", "kod", "mj" };

    private static readonly Dictionary<string, int> SlovaCisla = new()
    {
        ["jeden"] = 1, ["jedna"] = 1, ["jedno"] = 1, ["dva"] = 2, ["dve"] = 2, ["tri"] = 3, ["styri"] = 4, ["pat"] = 5, ["sest"] = 6,
        ["sedem"] = 7, ["osem"] = 8, ["devat"] = 9, ["desat"] = 10, ["jedenast"] = 11, ["dvanast"] = 12, ["patnast"] = 15,
        ["dvadsat"] = 20,
    };

    // ------------------------------------------------------------------ text

    /// <summary>Položky z textu (webová objednávka, text faktúry, OCR, diktovanie).</summary>
    public static Doklad ZTextu(string text, string zdroj)
    {
        var riadky = Rozdel(text);
        var cakajuciZVety = false;
        var vysledok = new List<RiadokDokladu>();
        string? cakajuciNazov = null;
        var pocetPreNazov = 1;
        var odNazvu = 0;
        foreach (var (povodny, zVety) in riadky)
        {
            // predchádzajúca veta bez ceny („jeden projektor Epson“) je položka
            if (cakajuciNazov is not null && cakajuciZVety && !zVety)
            {
                vysledok.Add(new RiadokDokladu(cakajuciNazov, pocetPreNazov, null));
                cakajuciNazov = null;
            }

            var riadok = povodny.Replace('\t', ' ');
            var bez = Sk.BezDiakritiky(riadok);
            if (EanRegex().Match(riadok) is { Success: true } ean)
            {
                vysledok.Add(new RiadokDokladu("EAN " + ean.Groups[1].Value, 1, null, null, ean.Groups[1].Value));
                cakajuciNazov = null;
                continue;
            }

            // súčty, DPH, doprava … – kľúčové slovo medzi prvými tromi slovami (tovar „… s DPH“ ostane)
            var zaciatok = string.Join(" ", bez.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Take(3));
            if (Suctove.Any(s => Regex.IsMatch(zaciatok, $@"(^|\W){Regex.Escape(s)}")) || Hlavicky.Count(h => bez.Contains(h)) >= 2)
            {
                cakajuciNazov = null;
                continue;
            }

            // „Počet: 2“, „Množstvo 2 ks“, samotné „2“ pod názvom (webová objednávka)
            if (PocetRiadokRegex().Match(riadok) is { Success: true } pr)
            {
                if (cakajuciNazov is not null)
                {
                    pocetPreNazov = Math.Clamp(int.Parse(pr.Groups["n"].Value, CultureInfo.InvariantCulture), 1, 500);
                }

                odNazvu++;
                continue;
            }

            var sumy = Sumy(riadok);
            var nazov = Nazov(riadok);
            var pocet = Pocet(riadok);
            if (pocet is null && zVety && Regex.Match(riadok, @"^\s*(\d{1,3})\s+\p{L}{3,}") is { Success: true } kusy)
            {
                // diktovanie „3 notebooky HP …“ (pri faktúre by to bolo poradové číslo riadku)
                pocet = Math.Clamp(int.Parse(kusy.Groups[1].Value, CultureInfo.InvariantCulture), 1, 500);
            }

            if (sumy.Count > 0 && nazov.Count(char.IsLetter) >= 3)
            {
                vysledok.Add(new RiadokDokladu(nazov, pocet ?? 1, CenaKus(sumy, pocet ?? 1, riadok)));
                cakajuciNazov = null;
            }
            else if (sumy.Count > 0 && cakajuciNazov is not null && odNazvu <= 4)
            {
                // webová objednávka: názov na jednom riadku, počet a cena na ďalších
                vysledok.Add(new RiadokDokladu(cakajuciNazov, pocet ?? pocetPreNazov, CenaKus(sumy, pocet ?? pocetPreNazov, riadok)));
                cakajuciNazov = null;
            }
            else if (sumy.Count == 0 && nazov.Count(char.IsLetter) >= 3)
            {
                // diktovanie bez ceny („dva projektory Epson“) – položka sa pridá, ak nepríde cena
                if (cakajuciNazov is not null && cakajuciZVety)
                {
                    vysledok.Add(new RiadokDokladu(cakajuciNazov, pocetPreNazov, null));
                }

                cakajuciNazov = nazov;
                cakajuciZVety = zVety;
                pocetPreNazov = pocet ?? 1;
                odNazvu = 0;
            }
            else if (cakajuciNazov is not null && pocet is { } p)
            {
                pocetPreNazov = p;
            }

            odNazvu++;
        }

        if (cakajuciNazov is not null && cakajuciZVety)
        {
            vysledok.Add(new RiadokDokladu(cakajuciNazov, pocetPreNazov, null));
        }

        return new Doklad(vysledok, CisloDokladu(text), DatumDokladu(text), Dodavatel(text), zdroj);
    }

    /// <summary>
    /// Riadky textu. Text bez tabuliek sa delí aj na vety a na časti začínajúce počtom kusov (diktovanie:
    /// „tri notebooky … po 780 eur, dva monitory … a jeden projektor“). Veta = položka sa pridá aj bez ceny.
    /// </summary>
    private static List<(string Text, bool Veta)> Rozdel(string text)
    {
        var riadky = text.Replace("\r", "").Split('\n', ';').Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
        if (text.Contains('\t') || text.Contains('|'))
        {
            return riadky.Select(r => (r, false)).ToList();
        }

        var kratky = riadky.Count <= 3;
        var casti = new List<(string, bool)>();
        foreach (var riadok in riadky)
        {
            var diktovany = kratky || riadok.EndsWith('.') || riadok.EndsWith('!')
                            || SlovaCisla.ContainsKey(Sk.BezDiakritiky(riadok.Split(' ')[0]));
            if (!diktovany)
            {
                casti.Add((riadok, false));
                continue;
            }

            foreach (var veta in Regex.Split(riadok, @"(?<=\p{L}|€)\.\s+|(?<=\d)\.\s+(?=\p{Lu})"))
            {
                // „…, dva monitory Dell po 150 eur“ – nová položka po čiarke alebo „a“, ak začína počtom
                foreach (var cast in Regex.Split(veta, @",\s+(?=(?:\d+|jeden|jedna|dva|dve|tri|štyri|styri|päť|pat|šesť|sest|sedem|osem|deväť|devat|desať|desat)\s+(?!(?:gb|tb|mb|ghz|mhz|hz|mm|cm|mah|wh|ks|kusy|kusov|rok|roky|rokov)\b)\p{L}{3,})|\s+a\s+(?=(?:\d+|jeden|jedna|jedno|dva|dve|tri|štyri|styri|päť|pat)\s+\p{L}{3,})",
                             RegexOptions.IgnoreCase))
                {
                    var t = cast.Trim(' ', '.');
                    if (t.Length > 0)
                    {
                        casti.Add((t, diktovany));
                    }
                }
            }
        }

        return casti;
    }

    private static List<decimal> Sumy(string riadok)
    {
        var bezPercent = PercentoRegex().Replace(riadok, " ");
        var sumy = new List<decimal>();
        foreach (Match m in SumaRegex().Matches(bezPercent))
        {
            var maDesatiny = m.Groups["des"].Success;
            var maMenu = m.Groups["mena"].Success;
            if (!maDesatiny && !maMenu)
            {
                continue;
            }

            // počet kusov s desatinnou čiarkou („2,00 ks“) nie je cena
            var za = bezPercent[(m.Index + m.Length)..].TrimStart();
            if (!maMenu && Regex.IsMatch(za, @"^(ks|kus|pcs|x|×)\b", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var cislo = Regex.Replace(m.Groups["cela"].Value, @"[   .]", "") + (maDesatiny ? "." + m.Groups["des"].Value[1..] : "");
            if (decimal.TryParse(cislo, NumberStyles.Number, CultureInfo.InvariantCulture, out var suma) && suma > 0 && suma < 10_000_000)
            {
                sumy.Add(suma);
            }
        }

        return sumy;
    }

    private static int? Pocet(string riadok)
    {
        if (PocetRegex().Match(riadok) is { Success: true } m && int.TryParse(m.Groups["n"].Value, out var n) && n is > 0 and <= 500)
        {
            return n;
        }

        // „2,00 ks“ / „2.000 ks“
        if (Regex.Match(riadok, @"(?<![\w,.])(\d{1,3})[,.]0{1,3}\s*(ks|kus|pcs)\b", RegexOptions.IgnoreCase) is { Success: true } d)
        {
            return int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        // diktovanie: „tri notebooky …“
        var prve = Sk.BezDiakritiky(riadok.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "");
        return SlovaCisla.TryGetValue(prve, out var slovom) ? slovom : null;
    }

    /// <summary>Cena za kus: ak niektorá suma = počet × iná suma, tá menšia je za kus; inak prvá suma.</summary>
    private static decimal CenaKus(List<decimal> sumy, int pocet, string riadok)
    {
        if (pocet > 1)
        {
            foreach (var kus in sumy)
            {
                if (sumy.Any(s => Math.Abs(s - kus * pocet) <= 0.05m * pocet))
                {
                    return kus;
                }
            }

            // diktovanie „… za 1500 eur“ pri viacerých kusoch = cena spolu
            if (sumy.Count == 1 && Regex.IsMatch(Sk.BezDiakritiky(riadok), @"\bza\s+\d"))
            {
                return Math.Round(sumy[0] / pocet, 2);
            }
        }

        return sumy[0];
    }

    /// <summary>Názov tovaru: text bez súm, počtu, percent, poradového čísla a jednotiek.</summary>
    private static string Nazov(string riadok)
    {
        var t = PercentoRegex().Replace(riadok, " ");
        t = SumaRegex().Replace(t, m => m.Groups["des"].Success || m.Groups["mena"].Success ? " " : m.Value);
        t = PocetRegex().Replace(t, " ");
        t = Regex.Replace(t, @"(?<![\w,.])\d{1,3}[,.]0{1,3}\s*(ks|kus|pcs)\b", " ", RegexOptions.IgnoreCase);
        t = PoradieRegex().Replace(t, "");
        t = Regex.Replace(t, @"\b(po|za|á|à|a\s+cena|cena|s\s+dph|bez\s+dph|eur|eura|eurá|ks|kus|kusy|kusov)\b", " ", RegexOptions.IgnoreCase);
        var slova = t.Split(new[] { ' ', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (slova.Count > 0 && SlovaCisla.ContainsKey(Sk.BezDiakritiky(slova[0])))
        {
            slova.RemoveAt(0);
        }

        return Sk.VelkePrve(string.Join(" ", slova).Trim(' ', '-', '–', ',', ';', ':', '·', '*', '.', '/'));
    }

    private static string? CisloDokladu(string text) =>
        CisloRegex().Match(text) is { Success: true } m && m.Groups["c"].Value.Any(char.IsDigit) ? m.Groups["c"].Value : null;

    private static DateTime? DatumDokladu(string text)
    {
        var bez = Sk.BezDiakritiky(text);
        foreach (var kluc in new[] { "datum dodania", "datum vystavenia", "datum objednavky", "datum nakupu", "vystavene", "objednane", "datum" })
        {
            var i = bez.IndexOf(kluc, StringComparison.Ordinal);
            if (i >= 0 && DatumRegex().Match(text, i) is { Success: true } m && m.Index - i < 60 && Datum(m) is { } d)
            {
                return d;
            }
        }

        return DatumRegex().Match(text) is { Success: true } prvy ? Datum(prvy) : null;
    }

    private static DateTime? Datum(Match m) =>
        DateTime.TryParseExact($"{m.Groups[1].Value}.{m.Groups[2].Value}.{m.Groups[3].Value}", new[] { "d.M.yyyy", "dd.MM.yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year > 2000 && d <= DateTime.Today.AddDays(31)
            ? d
            : null;

    private static string? Dodavatel(string text)
    {
        var riadky = text.Replace("\r", "").Split('\n').Select(r => r.Trim()).ToList();
        for (var i = 0; i < riadky.Count; i++)
        {
            var bez = Sk.BezDiakritiky(riadky[i]);
            if (bez.StartsWith("dodavatel") || bez.StartsWith("predavajuci") || bez.StartsWith("supplier") || bez.StartsWith("seller"))
            {
                var zvysok = riadky[i][(riadky[i].IndexOf(':') + 1)..].Trim();
                if (riadky[i].Contains(':') && zvysok.Length >= 3)
                {
                    return zvysok;
                }

                return riadky.Skip(i + 1).FirstOrDefault(r => r.Count(char.IsLetter) >= 3);
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ elektronická faktúra (XML)

    /// <summary>Elektronická faktúra: UBL (Peppol, slovenská e-faktúra), ISDOC, alebo CII (Factur-X/ZUGFeRD). Inak null.</summary>
    public static Doklad? ZXml(string xml, string zdroj)
    {
        XDocument dok;
        try
        {
            dok = XDocument.Parse(xml);
        }
        catch (Exception)
        {
            return null;
        }

        var koren = dok.Root!;
        IEnumerable<XElement> Vsetky(XElement e, string meno) => e.Descendants().Where(x => x.Name.LocalName == meno);
        XElement? Prvy(XElement? e, params string[] cesta)
        {
            foreach (var meno in cesta)
            {
                e = e?.Elements().FirstOrDefault(x => x.Name.LocalName == meno);
            }

            return e;
        }

        decimal? Cislo(XElement? e) =>
            e is not null && decimal.TryParse(e.Value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        DateTime? Den(XElement? e) =>
            e is null ? null
            : DateTime.TryParseExact(e.Value.Trim(), new[] { "yyyy-MM-dd", "yyyyMMdd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        int Kusy(decimal? n) => n is { } q && q >= 1 && q <= 500 && q == Math.Floor(q) ? (int)q : 1;

        var riadky = new List<RiadokDokladu>();
        if (koren.Name.LocalName == "CrossIndustryInvoice")
        {
            foreach (var l in Vsetky(koren, "IncludedSupplyChainTradeLineItem"))
            {
                var nazov = Prvy(l, "SpecifiedTradeProduct", "Name")?.Value.Trim() ?? "";
                var pocet = Cislo(Prvy(l, "SpecifiedLineTradeDelivery", "BilledQuantity"));
                var cena = Cislo(Prvy(l, "SpecifiedLineTradeAgreement", "NetPriceProductTradePrice", "ChargeAmount"));
                var dph = Cislo(Prvy(l, "SpecifiedLineTradeSettlement", "ApplicableTradeTax", "RateApplicablePercent"));
                var ean = Prvy(l, "SpecifiedTradeProduct", "GlobalID")?.Value.Trim();
                if (nazov.Length > 0)
                {
                    riadky.Add(new RiadokDokladu(nazov, Kusy(pocet), cena, dph, ean));
                }
            }

            var hlavicka = Prvy(koren, "ExchangedDocument");
            return new Doklad(riadky, Prvy(hlavicka, "ID")?.Value.Trim(), Den(Prvy(hlavicka, "IssueDateTime", "DateTimeString")),
                Vsetky(koren, "SellerTradeParty").FirstOrDefault() is { } predajca ? Prvy(predajca, "Name")?.Value.Trim() : null, zdroj, true);
        }

        var isdoc = koren.Name.NamespaceName.Contains("isdoc", StringComparison.OrdinalIgnoreCase);
        if (koren.Name.LocalName is "Invoice" or "CreditNote" || isdoc)
        {
            foreach (var l in Vsetky(koren, isdoc ? "InvoiceLine" : koren.Name.LocalName == "CreditNote" ? "CreditNoteLine" : "InvoiceLine"))
            {
                var polozka = Prvy(l, "Item");
                var nazov = (isdoc ? Prvy(polozka, "Description") : Prvy(polozka, "Name"))?.Value.Trim() ?? "";
                var pocet = Cislo(Prvy(l, "InvoicedQuantity") ?? Prvy(l, "CreditedQuantity"));
                var cena = isdoc
                    ? Cislo(Prvy(l, "UnitPriceTaxInclusive")) ?? Cislo(Prvy(l, "UnitPrice"))
                    : Cislo(Prvy(l, "Price", "PriceAmount"));
                var dph = isdoc ? Cislo(Prvy(l, "ClassifiedTaxCategory", "Percent")) : Cislo(Prvy(polozka, "ClassifiedTaxCategory", "Percent"));
                var ean = Prvy(polozka, "StandardItemIdentification", "ID")?.Value.Trim();
                if (nazov.Length > 0)
                {
                    riadky.Add(new RiadokDokladu(nazov, Kusy(pocet), cena, dph, ean));
                }
            }

            var dodavatel = Prvy(koren, "AccountingSupplierParty", "Party");
            var meno = Prvy(dodavatel, "PartyName", "Name")?.Value ?? Prvy(dodavatel, "PartyLegalEntity", "RegistrationName")?.Value;
            var bezDph = !isdoc || Prvy(Vsetky(koren, "InvoiceLine").FirstOrDefault(), "UnitPriceTaxInclusive") is null;
            return new Doklad(riadky, Prvy(koren, "ID")?.Value.Trim(), Den(Prvy(koren, "IssueDate")), meno?.Trim(), zdroj, bezDph);
        }

        return null;
    }

    /// <summary>XML elektronickej faktúry vložené v PDF (Factur-X/ZUGFeRD, PDF/A-3 s UBL alebo ISDOC). Inak null.</summary>
    public static string? XmlZPdf(string cesta)
    {
        try
        {
            using var pdf = PdfReader.Open(cesta, PdfDocumentOpenMode.Import);
            var subory = new List<PdfDictionary>();
            void Strom(PdfDictionary? uzol)
            {
                if (uzol is null)
                {
                    return;
                }

                if (uzol.Elements.GetArray("/Names") is { } mena)
                {
                    for (var i = 1; i < mena.Elements.Count; i += 2)
                    {
                        if (mena.Elements.GetDictionary(i) is { } spec)
                        {
                            subory.Add(spec);
                        }
                    }
                }

                if (uzol.Elements.GetArray("/Kids") is { } deti)
                {
                    for (var i = 0; i < deti.Elements.Count; i++)
                    {
                        Strom(deti.Elements.GetDictionary(i));
                    }
                }
            }

            Strom(pdf.Internals.Catalog.Elements.GetDictionary("/Names")?.Elements.GetDictionary("/EmbeddedFiles"));
            if (pdf.Internals.Catalog.Elements.GetArray("/AF") is { } af)
            {
                for (var i = 0; i < af.Elements.Count; i++)
                {
                    if (af.Elements.GetDictionary(i) is { } spec)
                    {
                        subory.Add(spec);
                    }
                }
            }

            foreach (var spec in subory)
            {
                var meno = spec.Elements.GetString("/UF") is { Length: > 0 } uf ? uf : spec.Elements.GetString("/F");
                if (!meno.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !meno.EndsWith(".isdoc", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (spec.Elements.GetDictionary("/EF")?.Elements.GetDictionary("/F") is { Stream: { } prud })
                {
                    var bajty = prud.UnfilteredValue;
                    using var citac = new StreamReader(new MemoryStream(bajty), detectEncodingFromByteOrderMarks: true);
                    return citac.ReadToEnd();
                }
            }
        }
        catch (Exception)
        {
            // PDF sa nedá prečítať (zašifrované, poškodené) – skúsi sa OCR
        }

        return null;
    }
}
