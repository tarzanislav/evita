using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Evita.Pomocne;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace Evita.Services;

/// <summary>
/// Archívne PDF/A-2b, PDF/A-3b (s vloženými dátami v Exceli) a PDF/X-4 pre tlačiarne: farebný profil (výstupný zámer),
/// metadáta XMP s označením normy, obrázky bez interpolácie, bez šifrovania. Farebné profily sa berú zo systému Windows
/// (sRGB a RSWOP pre ofsetovú tlač), program ich so sebou nenesie.
/// </summary>
public static class PdfNorma
{
    private static readonly string PriecinokProfilov =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "drivers", "color");

    /// <summary>Upraví PDF podľa normy, uloží ho a vráti obsah súboru.</summary>
    public static byte[] Uloz(PdfDocument pdf, FormatVystupu format, string nazov, byte[]? data)
    {
        var archiv = format is FormatVystupu.PdfA2b or FormatVystupu.PdfA3b;
        pdf.Version = archiv ? 17 : 16;

        // dátumy na celé sekundy – v slovníku Info aj v XMP musia byť rovnaké
        var teraz = DateTime.Now;
        teraz = new DateTime(teraz.Ticks - teraz.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Local);
        pdf.Info.CreationDate = teraz;
        pdf.Info.ModificationDate = teraz;
        if (string.IsNullOrWhiteSpace(pdf.Info.Title))
        {
            pdf.Info.Title = nazov;
        }

        foreach (var kluc in new[] { "/Author", "/Subject", "/Keywords" })
        {
            if (pdf.Info.Elements.GetString(kluc).Length == 0)
            {
                pdf.Info.Elements.Remove(kluc);
            }
        }

        // norma nepovoľuje interpoláciu obrázkov
        foreach (var objekt in pdf.Internals.GetAllObjects())
        {
            if (objekt is PdfDictionary slovnik && slovnik.Elements.GetName("/Subtype") == "/Image")
            {
                slovnik.Elements.Remove("/Interpolate");
            }
        }

        var srgb = Profil(pdf, "sRGB Color Space Profile.icm", 3);
        if (archiv)
        {
            Zamer(pdf, "/GTS_PDFA1", srgb, "sRGB IEC61966-2.1", "sRGB IEC61966-2.1");
        }
        else
        {
            var cmyk = Profil(pdf, "RSWOP.icm", 4);
            Zamer(pdf, "/GTS_PDFX", cmyk, "CGATS TR 001", "U.S. Web Coated (SWOP)");
            pdf.Info.Elements.SetName("/Trapped", "/False");
            pdf.Info.Elements.SetString("/GTS_PDFXVersion", "PDF/X-4");

            // farby RGB zo zostavy sa označia profilom sRGB (pri výstupe CMYK nesmú byť „neoznačené“)
            var rgb = new PdfArray(pdf);
            rgb.Elements.Add(new PdfName("/ICCBased"));
            rgb.Elements.Add(srgb.Reference!);
            pdf.Internals.AddObject(rgb);
            foreach (var strana in pdf.Pages)
            {
                strana.TrimBox = strana.MediaBox;
                var zdroje = strana.Resources;
                var farby = zdroje.Elements.GetDictionary("/ColorSpace");
                if (farby is null)
                {
                    farby = new PdfDictionary(pdf);
                    zdroje.Elements["/ColorSpace"] = farby;
                }

                farby.Elements.SetReference("/DefaultRGB", rgb);

                // skupina priehľadnosti s farbami sRGB (inak by PDFsharp pri ukladaní doplnil neoznačené DeviceRGB)
                var skupina = new PdfDictionary(pdf);
                skupina.Elements.SetName("/S", "/Transparency");
                skupina.Elements.SetReference("/CS", rgb);
                strana.Elements["/Group"] = skupina;
            }
        }

        if (format == FormatVystupu.PdfA3b && data is not null)
        {
            Priloha(pdf, nazov + ".xlsx", data, teraz);
        }

        using var prud = new MemoryStream();
        pdf.Save(prud, false);
        var obsah = prud.ToArray();
        if (pdf.Internals.Catalog.Elements["/Metadata"] is not PdfReference metadata)
        {
            throw new InvalidOperationException("PDF nemá metadáta.");
        }

        return DoplnMetadata(obsah, metadata.ObjectNumber, metadata.GenerationNumber, Xmp(pdf, format, teraz));
    }

    /// <summary>Farebný profil ICC zo systému Windows ako prúd v PDF.</summary>
    private static PdfDictionary Profil(PdfDocument pdf, string subor, int zloziek)
    {
        var cesta = Path.Combine(PriecinokProfilov, subor);
        if (!File.Exists(cesta))
        {
            throw new EvitaException(Preklad.F($"V systéme Windows chýba farebný profil {subor} – PDF podľa normy sa nedá vytvoriť. Uložte bežné PDF."));
        }

        var profil = new PdfDictionary(pdf);
        profil.CreateStream(File.ReadAllBytes(cesta));
        profil.Elements.SetInteger("/N", zloziek);
        pdf.Internals.AddObject(profil);
        return profil;
    }

    /// <summary>Výstupný zámer (OutputIntent) – pre aké farby je dokument určený.</summary>
    private static void Zamer(PdfDocument pdf, string druh, PdfDictionary profil, string podmienka, string popis)
    {
        var zamer = new PdfDictionary(pdf);
        zamer.Elements.SetName("/Type", "/OutputIntent");
        zamer.Elements.SetName("/S", druh);
        zamer.Elements.SetString("/OutputConditionIdentifier", podmienka);
        zamer.Elements.SetString("/OutputCondition", popis);
        zamer.Elements.SetString("/Info", popis);
        zamer.Elements.SetString("/RegistryName", "http://www.color.org");
        zamer.Elements.SetReference("/DestOutputProfile", profil);
        pdf.Internals.AddObject(zamer);
        var zamery = new PdfArray(pdf);
        zamery.Elements.Add(zamer.Reference!);
        pdf.Internals.Catalog.Elements["/OutputIntents"] = zamery;
    }

    /// <summary>PDF/A-3: vložený súbor s dátami zostavy (príloha viditeľná v prehliadači PDF).</summary>
    /// <summary>Vloží súbor do PDF ako prílohu (PDF/A-3: /AF a strom /EmbeddedFiles).</summary>
    internal static void Priloha(PdfDocument pdf, string nazov, byte[] data, DateTime cas,
        string typ = "/application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", string? popis = null, string vztah = "/Data")
    {
        var subor = new PdfDictionary(pdf);
        subor.CreateStream(data);
        subor.Elements.SetName("/Type", "/EmbeddedFile");
        subor.Elements.SetName("/Subtype", typ);
        var parametre = new PdfDictionary(pdf);
        parametre.Elements.SetInteger("/Size", data.Length);
        parametre.Elements.SetDateTime("/ModDate", cas);
        subor.Elements["/Params"] = parametre;
        pdf.Internals.AddObject(subor);

        var specifikacia = new PdfDictionary(pdf);
        specifikacia.Elements.SetName("/Type", "/Filespec");
        specifikacia.Elements["/F"] = new PdfString(BezDiakritiky(nazov));
        specifikacia.Elements["/UF"] = new PdfString(nazov, PdfStringEncoding.Unicode);
        specifikacia.Elements["/Desc"] = new PdfString(popis ?? Preklad.T("Dáta zostavy v Exceli"), PdfStringEncoding.Unicode);
        specifikacia.Elements.SetName("/AFRelationship", vztah);
        var prudy = new PdfDictionary(pdf);
        prudy.Elements.SetReference("/F", subor);
        prudy.Elements.SetReference("/UF", subor);
        specifikacia.Elements["/EF"] = prudy;
        pdf.Internals.AddObject(specifikacia);

        var katalog = pdf.Internals.Catalog;
        var pridruzene = new PdfArray(pdf);
        pridruzene.Elements.Add(specifikacia.Reference!);
        katalog.Elements["/AF"] = pridruzene;
        var mena = katalog.Elements.GetDictionary("/Names");
        if (mena is null)
        {
            mena = new PdfDictionary(pdf);
            katalog.Elements["/Names"] = mena;
        }

        var strom = new PdfDictionary(pdf);
        var polozky = new PdfArray(pdf);
        polozky.Elements.Add(new PdfString(nazov, PdfStringEncoding.Unicode));
        polozky.Elements.Add(specifikacia.Reference!);
        strom.Elements["/Names"] = polozky;
        mena.Elements["/EmbeddedFiles"] = strom;
    }

    private static string BezDiakritiky(string text)
    {
        var rozlozeny = text.Normalize(NormalizationForm.FormD);
        var vysledok = new StringBuilder();
        foreach (var znak in rozlozeny)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(znak) != UnicodeCategory.NonSpacingMark)
            {
                vysledok.Append(znak is >= ' ' and <= '~' ? znak : '_');
            }
        }

        return vysledok.ToString();
    }

    /// <summary>Metadáta XMP zhodné so slovníkom Info, s označením normy (pdfaid / pdfxid).</summary>
    private static string Xmp(PdfDocument pdf, FormatVystupu format, DateTime cas)
    {
        static string E(string text) => SecurityElement.Escape(text) ?? "";
        var datum = cas.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        var info = pdf.Info;
        var x = new StringBuilder();
        x.Append("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
        x.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");
        x.Append("<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"");
        x.Append(" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\" xmlns:xmpMM=\"http://ns.adobe.com/xap/1.0/mm/\"");
        x.Append(format == FormatVystupu.PdfX4
            ? " xmlns:pdfxid=\"http://www.npes.org/pdfx/ns/id/\">\n"
            : " xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\">\n");
        x.Append("<dc:format>application/pdf</dc:format>\n");
        x.Append($"<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">{E(info.Title)}</rdf:li></rdf:Alt></dc:title>\n");
        if (info.Author.Length > 0)
        {
            x.Append($"<dc:creator><rdf:Seq><rdf:li>{E(info.Author)}</rdf:li></rdf:Seq></dc:creator>\n");
        }

        if (info.Subject.Length > 0)
        {
            x.Append($"<dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">{E(info.Subject)}</rdf:li></rdf:Alt></dc:description>\n");
        }

        x.Append($"<xmp:CreatorTool>{E(info.Creator)}</xmp:CreatorTool>\n");
        x.Append($"<xmp:CreateDate>{datum}</xmp:CreateDate>\n<xmp:ModifyDate>{datum}</xmp:ModifyDate>\n<xmp:MetadataDate>{datum}</xmp:MetadataDate>\n");
        x.Append($"<pdf:Producer>{E(info.Producer)}</pdf:Producer>\n");
        if (info.Keywords.Length > 0)
        {
            x.Append($"<pdf:Keywords>{E(info.Keywords)}</pdf:Keywords>\n");
        }

        x.Append($"<xmpMM:DocumentID>uuid:{Guid.NewGuid()}</xmpMM:DocumentID>\n<xmpMM:InstanceID>uuid:{Guid.NewGuid()}</xmpMM:InstanceID>\n");
        x.Append("<xmpMM:VersionID>1</xmpMM:VersionID>\n<xmpMM:RenditionClass>default</xmpMM:RenditionClass>\n");
        if (format == FormatVystupu.PdfX4)
        {
            x.Append("<pdf:Trapped>False</pdf:Trapped>\n<pdfxid:GTS_PDFXVersion>PDF/X-4</pdfxid:GTS_PDFXVersion>\n");
        }
        else
        {
            x.Append($"<pdfaid:part>{(format == FormatVystupu.PdfA3b ? 3 : 2)}</pdfaid:part>\n<pdfaid:conformance>B</pdfaid:conformance>\n");
        }

        x.Append("</rdf:Description>\n</rdf:RDF>\n</x:xmpmeta>\n");
        x.Append(new string(' ', 1500)).Append('\n');
        x.Append("<?xpacket end=\"w\"?>");
        return x.ToString();
    }

    /// <summary>
    /// Prírastková aktualizácia PDF (ako pri podpisovaní): nová verzia objektu metadát s XMP podľa normy. PDFsharp pri
    /// ukladaní vždy zapíše vlastné XMP bez označenia normy, preto sa na koniec súboru pripíše nová verzia objektu.
    /// </summary>
    public static byte[] DoplnMetadata(byte[] pdf, int cislo, int generacia, string xmp)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var zaciatokTraileru = text.LastIndexOf("trailer", StringComparison.Ordinal);
        var startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        if (zaciatokTraileru < 0 || startxref < zaciatokTraileru)
        {
            throw new InvalidOperationException("PDF nemá klasickú tabuľku odkazov.");
        }

        var trailer = text[zaciatokTraileru..startxref];
        var predosly = long.Parse(Regex.Match(text[startxref..], @"startxref\s+(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        string Hodnota(string vzor) => Regex.Match(trailer, vzor, RegexOptions.Singleline) is { Success: true } m ? m.Groups[1].Value : "";
        var koren = Hodnota(@"/Root\s+(\d+\s+\d+\s+R)");
        var info = Hodnota(@"/Info\s+(\d+\s+\d+\s+R)");
        var id = Hodnota(@"/ID\s*(\[[^\]]*\])");
        var velkost = Hodnota(@"/Size\s+(\d+)");

        using var vystup = new MemoryStream();
        vystup.Write(pdf);
        if (pdf.Length > 0 && pdf[^1] != '\n')
        {
            vystup.WriteByte((byte)'\n');
        }

        void Zapis(string s) => vystup.Write(Encoding.ASCII.GetBytes(s));
        var data = Encoding.UTF8.GetBytes(xmp);
        var pozicia = vystup.Position;
        Zapis($"{cislo} {generacia} obj\n<</Type/Metadata/Subtype/XML/Length {data.Length}>>\nstream\n");
        vystup.Write(data);
        Zapis("\nendstream\nendobj\n");
        var xref = vystup.Position;
        Zapis($"xref\n{cislo} 1\n{pozicia:D10} {generacia:D5} n\r\n");
        Zapis($"trailer\n<</Size {velkost}/Root {koren}" + (info.Length > 0 ? $"/Info {info}" : "") + (id.Length > 0 ? $"/ID {id}" : "")
              + $"/Prev {predosly}>>\nstartxref\n{xref}\n%%EOF\n");
        return vystup.ToArray();
    }
}
