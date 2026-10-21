using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Evita.Data;
using Evita.Pomocne;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace Evita.Services;

/// <summary>Formát ukladanej zostavy.</summary>
public enum FormatVystupu
{
    /// <summary>Bežné PDF (dá sa zašifrovať a chrániť heslom).</summary>
    Pdf,

    /// <summary>Archívne PDF/A-2b (ISO 19005-2).</summary>
    PdfA2b,

    /// <summary>Archívne PDF/A-3b s vloženými dátami zostavy v Exceli.</summary>
    PdfA3b,

    /// <summary>PDF/X-4 pre tlačiarne (ISO 15930-7).</summary>
    PdfX4,

    /// <summary>Strany ako obrázky PNG.</summary>
    Png,

    /// <summary>Všetky strany v jednom obrázku TIFF.</summary>
    Tiff,
}

/// <summary>Súbor vytvorený zo zostavy (PDF alebo obrázok) – na uloženie alebo ako príloha e-mailu.</summary>
public sealed record SuborVystupu(string Nazov, byte[] Data, string Mime);

/// <summary>
/// Voľby ukladania do PDF (podobne ako pdfFactory): formát (PDF, PDF/A, PDF/X, obrázky), šifrovanie, heslo na otvorenie,
/// zákazy (tlač, kopírovanie, úpravy, poznámky), odtiene sivej, zosvetlenie a rozmiestnenie strán (1/2/4/8 na list,
/// brožúra). Text v PDF zostáva textom – dá sa označiť a kopírovať (OCR netreba), písma sú vždy vložené.
/// </summary>
public sealed record VolbyPdf
{
    public const string KlucNastavenia = "Tlac.Pdf";

    public FormatVystupu Format { get; init; } = FormatVystupu.Pdf;

    /// <summary>Rozmiestnenie strán, rámik, zväčšenie, sivá, zosvetlenie, dierovač (rovnaké ako pri tlači).</summary>
    public VolbyTlace Rozvrh { get; init; } = new();

    public bool Sifrovat { get; init; }

    /// <summary>PDF sa otvorí len po zadaní hesla (heslo sa neukladá – platí do zatvorenia programu).</summary>
    public bool PouzitHeslo { get; init; }

    public bool ZakazTlace { get; init; }
    public bool ZakazKopirovania { get; init; }
    public bool ZakazUprav { get; init; }
    public bool ZakazPoznamok { get; init; }

    /// <summary>Heslo na otvorenie – len v pamäti počas behu programu, do databázy sa neukladá.</summary>
    [JsonIgnore] public static string? Heslo { get; set; }

    /// <summary>Heslo na zmenu zákazov (nepovinné) – tiež len v pamäti.</summary>
    [JsonIgnore] public static string? HesloVlastnika { get; set; }

    /// <summary>Výsledok je PDF (nie obrázok).</summary>
    [JsonIgnore] public bool JePdf => Format is FormatVystupu.Pdf or FormatVystupu.PdfA2b or FormatVystupu.PdfA3b or FormatVystupu.PdfX4;

    /// <summary>Šifrovanie, heslo a zákazy – len bežné PDF (normy PDF/A a PDF/X šifrovanie nepovoľujú).</summary>
    [JsonIgnore] public bool MozeZabezpecit => Format == FormatVystupu.Pdf;

    [JsonIgnore] public bool MaZakazy => MozeZabezpecit && (ZakazTlace || ZakazKopirovania || ZakazUprav || ZakazPoznamok);

    /// <summary>Heslo na otvorenie sa naozaj použije.</summary>
    [JsonIgnore] public bool SHeslom => MozeZabezpecit && PouzitHeslo;

    /// <summary>PDF sa zašifruje (zvolené šifrovanie, heslo alebo niektorý zákaz).</summary>
    [JsonIgnore] public bool Zasifrovat => MozeZabezpecit && (Sifrovat || PouzitHeslo || MaZakazy);

    /// <summary>Prípona súboru podľa formátu.</summary>
    [JsonIgnore] public string Pripona => Format switch
    {
        FormatVystupu.Png => ".png",
        FormatVystupu.Tiff => ".tif",
        _ => ".pdf",
    };

    /// <summary>Treba vlastné poskladanie strán (inak sa PDF uloží priamo, aj s odkazmi a záložkami).</summary>
    [JsonIgnore] public bool ZlozitStrany => Rozvrh.Rozmiestnenie != Rozmiestnenie.Jedna || Rozvrh.OdtieneSivej || Rozvrh.Zosvetlit
                                            || Rozvrh.Ramcek || Rozvrh.Dierovac || Rozvrh.Zvacsenie > 1.001;

    public static VolbyPdf Nacitaj()
    {
        try
        {
            using var db = Databaza.Otvor();
            if (Databaza.Citaj(db, KlucNastavenia) is { Length: > 0 } json && JsonSerializer.Deserialize<VolbyPdf>(json) is { } volby)
            {
                return volby with
                {
                    Rozvrh = volby.Rozvrh.Opravene(),
                    Format = Enum.IsDefined(volby.Format) ? volby.Format : FormatVystupu.Pdf,
                };
            }
        }
        catch (Exception)
        {
            // predvolené voľby
        }

        return new VolbyPdf();
    }

    public void Uloz()
    {
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucNastavenia, JsonSerializer.Serialize(this with { Rozvrh = Rozvrh.Opravene() with { Tlaciaren = null, Kopie = 1 } }));
        db.SaveChanges();
    }
}

/// <summary>Uloženie zostavy do PDF podľa volieb – vektorovo (text sa dá kopírovať), voliteľne zašifrované, PDF/A, PDF/X alebo obrázky.</summary>
public static class PdfVystup
{
    private const double BodovNaMm = 72.0 / 25.4;

    /// <summary>
    /// Uloží zostavu podľa uložených (alebo zadaných) volieb a vráti cesty k súborom. Pri obrázkoch PNG s viacerými
    /// stranami vznikne súbor pre každú stranu (názov-1.png, názov-2.png …). Cesta s príponou .pdf vždy dá PDF.
    /// </summary>
    public static IReadOnlyList<string> Uloz(Document dokument, string cesta, VolbyPdf? volby = null)
    {
        volby ??= VolbyPdf.Nacitaj();
        if (!volby.JePdf && string.Equals(Path.GetExtension(cesta), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            volby = volby with { Format = FormatVystupu.Pdf };
        }

        var priecinok = Path.GetDirectoryName(Path.GetFullPath(cesta))!;
        var cesty = new List<string>();
        foreach (var subor in Vytvor(dokument, Path.GetFileNameWithoutExtension(cesta), volby))
        {
            var ciel = Path.Combine(priecinok, subor.Nazov);
            File.WriteAllBytes(ciel, subor.Data);
            cesty.Add(ciel);
        }

        return cesty;
    }

    /// <summary>Vytvorí súbory zostavy v pamäti (na uloženie alebo odoslanie e-mailom bez ukladania na disk).</summary>
    public static IReadOnlyList<SuborVystupu> Vytvor(Document dokument, string nazov, VolbyPdf? volby = null)
    {
        volby ??= VolbyPdf.Nacitaj();
        if (volby.SHeslom && string.IsNullOrEmpty(VolbyPdf.Heslo))
        {
            throw new EvitaException(Preklad.T("PDF sa má chrániť heslom, ale heslo nie je zadané. Zadajte ho v nastaveniach PDF."));
        }

        if (volby.Rozvrh.BezPozadia)
        {
            PozadieDokumentu.Odstran(dokument);
        }

        if (!volby.JePdf)
        {
            return ObrazkyDokumentu.Vytvor(dokument, nazov, volby.Rozvrh, volby.Format == FormatVystupu.Tiff);
        }

        PdfDocument pdf;
        if (volby.ZlozitStrany)
        {
            pdf = Zloz(dokument, volby.Rozvrh);
        }
        else
        {
            var vykreslenie = new PdfDocumentRenderer { Document = dokument };
            vykreslenie.RenderDocument();
            pdf = vykreslenie.PdfDocument;
        }

        pdf.Info.Creator = $"EviTA {AktualizaciaService.AktualnaVerzia}";
        if (string.IsNullOrWhiteSpace(pdf.Info.Title))
        {
            pdf.Info.Title = string.IsNullOrWhiteSpace(dokument.Info.Title) ? nazov : dokument.Info.Title;
        }

        if (pdf.Outlines.Count > 0)
        {
            pdf.PageMode = PdfPageMode.UseOutlines;
        }

        byte[] data;
        if (volby.Format == FormatVystupu.Pdf)
        {
            Zabezpec(pdf, volby);
            using var prud = new MemoryStream();
            pdf.Save(prud, false);
            data = prud.ToArray();
        }
        else
        {
            data = PdfNorma.Uloz(pdf, volby.Format, nazov, volby.Format == FormatVystupu.PdfA3b ? TabulkyDokumentu.Xlsx(dokument) : null);
        }

        return new[] { new SuborVystupu(nazov + ".pdf", data, "application/pdf") };
    }

    /// <summary>
    /// Nové PDF zo strán zostavy: 1/2/4/8 strán na list alebo brožúra, rámik, zväčšenie a dierovač. Strany sa kreslia
    /// vektorovo (text zostáva textom). Zosvetlenie = biely priehľadný závoj, sivá = prekrytie v režime „Saturation“
    /// (farby sa zmenia na odtiene sivej, text sa dá stále označiť a kopírovať).
    /// </summary>
    public static PdfDocument Zloz(Document dokument, VolbyTlace volby)
    {
        volby = volby.Opravene();
        var vykreslenie = new DocumentRenderer(dokument);
        vykreslenie.PrepareDocument();
        var pocet = vykreslenie.FormattedDocument!.PageCount;
        var prva = vykreslenie.FormattedDocument.GetPageInfo(1);
        double sirkaStrany = prva.Width;
        double vyskaStrany = prva.Height;
        var stranyNaSirku = sirkaStrany > vyskaStrany;
        var (stlpce, riadky, listNaSirku) = RozvrhTlace.Mriezka(volby.Rozmiestnenie, stranyNaSirku);
        var listy = RozvrhTlace.Rozdel(pocet, volby);

        // oblasť obsahu strán (bez prázdnych okrajov) – na zväčšenie bez orezania textu
        var obsahy = new Dictionary<int, XRect>();
        XRect Obsah(int cislo)
        {
            if (!obsahy.TryGetValue(cislo, out var obsah))
            {
                obsah = new XRect(0, 0, sirkaStrany, vyskaStrany);
                if (volby.Zvacsenie > 1.001)
                {
                    try
                    {
                        var logicka = NahladDokumentu.Logicka(vykreslenie, cislo, stranyNaSirku);
                        if (!logicka.Obsah.IsEmpty)
                        {
                            var k = 72.0 / 96.0;
                            obsah = new XRect(logicka.Obsah.X * k, logicka.Obsah.Y * k, logicka.Obsah.Width * k, logicka.Obsah.Height * k);
                        }
                    }
                    catch (Exception)
                    {
                        // celá strana
                    }
                }

                obsahy[cislo] = obsah;
            }

            return obsah;
        }

        var pdf = new PdfDocument();
        pdf.Info.Title = dokument.Info.Title ?? "";
        for (var index = 0; index < listy.Count; index++)
        {
            var strana = pdf.AddPage();
            strana.Size = PdfSharp.PageSize.A4;
            strana.Orientation = listNaSirku ? PdfSharp.PageOrientation.Landscape : PdfSharp.PageOrientation.Portrait;
            double sirka, vyska;
            using (var gfx = XGraphics.FromPdfPage(strana))
            {
                sirka = gfx.PageSize.Width;
                vyska = gfx.PageSize.Height;
                if (volby.OdtieneSivej)
                {
                    // biely podklad: prekrytie „Saturation“ potom nechá svetlosť podkladu (inak by prázdne miesta zošedli)
                    gfx.DrawRectangle(XBrushes.White, new XRect(0, 0, sirka, vyska));
                }

                var dierovac = volby.Dierovac ? volby.DierovacMm * BodovNaMm : 0;
                var zadna = index % 2 == 1 && volby.TlacitObojstranne;
                var plocha = new XRect(zadna ? 0 : dierovac, 0, sirka - dierovac, vyska);
                var sirkaBunky = plocha.Width / stlpce;
                var vyskaBunky = plocha.Height / riadky;
                var bunky = listy[index];
                for (var i = 0; i < bunky.Length && i < stlpce * riadky; i++)
                {
                    var bunka = new XRect(plocha.X + i % stlpce * sirkaBunky, plocha.Y + i / stlpce * vyskaBunky, sirkaBunky, vyskaBunky);
                    if (bunky[i] is int cislo)
                    {
                        var cela = Math.Min(bunka.Width / sirkaStrany, bunka.Height / vyskaStrany);
                        var obsah = Obsah(cislo);
                        var naObsah = Math.Min(bunka.Width / obsah.Width, bunka.Height / obsah.Height);
                        var mierka = Math.Max(cela, Math.Min(cela * volby.Zvacsenie, naObsah));
                        var podiel = naObsah > cela ? Math.Clamp((mierka - cela) / (naObsah - cela), 0, 1) : 0;
                        var stredX = sirkaStrany / 2 + (obsah.X + obsah.Width / 2 - sirkaStrany / 2) * podiel;
                        var stredY = vyskaStrany / 2 + (obsah.Y + obsah.Height / 2 - vyskaStrany / 2) * podiel;
                        var stav = gfx.Save();
                        gfx.IntersectClip(bunka);
                        gfx.TranslateTransform(bunka.X + bunka.Width / 2 - stredX * mierka, bunka.Y + bunka.Height / 2 - stredY * mierka);
                        gfx.ScaleTransform(mierka);
                        vykreslenie.RenderPage(gfx, cislo);
                        gfx.Restore(stav);
                    }

                    if (volby.Ramcek && (bunky[i] is not null || volby.Rozmiestnenie != Rozmiestnenie.Jedna))
                    {
                        var okraj = 3 * BodovNaMm;
                        gfx.DrawRectangle(new XPen(XColors.Black, 0.5),
                            new XRect(bunka.X + okraj, bunka.Y + okraj, Math.Max(1, bunka.Width - 2 * okraj), Math.Max(1, bunka.Height - 2 * okraj)));
                    }
                }

                if (volby.Rozmiestnenie == Rozmiestnenie.Brozura && volby.Ramcek)
                {
                    gfx.DrawLine(new XPen(XColors.Gray, 0.4) { DashStyle = XDashStyle.Dash }, sirka / 2, 0, sirka / 2, vyska);
                }

                if (volby.Zosvetlit)
                {
                    gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb((int)(255 * VolbyTlace.SilaZosvetlenia), 255, 255, 255)), new XRect(0, 0, sirka, vyska));
                }
            }

            if (volby.OdtieneSivej)
            {
                // sivé prekrytie v režime miešania „Saturation“: sýtosť farieb pod ním klesne na nulu (zostanú odtiene sivej)
                var stav = new PdfExtGState(pdf);
                stav.Elements.SetName("/BM", "/Saturation");
                var nazov = strana.Resources.AddExtGState(stav);
                var obsahStrany = strana.Contents.AppendContent();
                obsahStrany.CreateStream(Encoding.ASCII.GetBytes(
                    FormattableString.Invariant($"q {nazov} gs 0.5 g -10 -10 {sirka + 20:0.##} {vyska + 20:0.##} re f Q\n")));
            }
        }

        // záložky (nadpisy skupín) – pri opakovaní strán len prvý výskyt
        var videne = new HashSet<string>();
        foreach (var zalozka in pdf.Outlines.ToList())
        {
            if (!videne.Add(zalozka.Title))
            {
                pdf.Outlines.Remove(zalozka);
            }
        }

        return pdf;
    }

    /// <summary>Šifrovanie (AES), heslo na otvorenie a zákazy. Bez hesla vlastníka sa použije náhodné – zákazy sa nedajú zrušiť.</summary>
    private static void Zabezpec(PdfDocument pdf, VolbyPdf volby)
    {
        if (!volby.Zasifrovat)
        {
            return;
        }

        var nastavenia = pdf.SecuritySettings;
        if (volby.PouzitHeslo)
        {
            nastavenia.UserPassword = VolbyPdf.Heslo!;
        }

        nastavenia.OwnerPassword = VolbyPdf.HesloVlastnika is { Length: > 0 } vlastnik ? vlastnik : Guid.NewGuid().ToString("N");
        nastavenia.PermitPrint = !volby.ZakazTlace;
        nastavenia.PermitFullQualityPrint = !volby.ZakazTlace;
        nastavenia.PermitExtractContent = !volby.ZakazKopirovania;
        nastavenia.PermitModifyDocument = !volby.ZakazUprav;
        nastavenia.PermitAssembleDocument = !volby.ZakazUprav;
        nastavenia.PermitAnnotations = !volby.ZakazPoznamok;
        nastavenia.PermitFormsFill = !volby.ZakazPoznamok;
    }
}
