using System.Globalization;
using Evita.Data;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Evita.Services;

/// <summary>Názov organizácie a logo do hlavičky tlačových zostáv (uložené v databáze, takže sú aj v zálohe).</summary>
public sealed record Organizacia(string? Nazov, byte[]? Logo);

public static class NastaveniaTlace
{
    public const string KlucNazov = "Organizacia.Nazov";
    public const string KlucLogo = "Organizacia.Logo";
    public const int NajvacsieLogo = 2 * 1024 * 1024;

    public static Organizacia Nacitaj()
    {
        try
        {
            using var db = Databaza.Otvor();
            var nazov = Databaza.Citaj(db, KlucNazov);
            var logo = Databaza.Citaj(db, KlucLogo);
            byte[]? bajty = null;
            if (!string.IsNullOrEmpty(logo))
            {
                try
                {
                    bajty = Convert.FromBase64String(logo);
                }
                catch (FormatException)
                {
                    // poškodené logo ignorujeme
                }
            }

            return new Organizacia(string.IsNullOrWhiteSpace(nazov) ? null : nazov.Trim(), bajty);
        }
        catch (Exception)
        {
            return new Organizacia(null, null);
        }
    }

    public static void UlozNazov(string? nazov)
    {
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucNazov, string.IsNullOrWhiteSpace(nazov) ? null : nazov.Trim());
        db.SaveChanges();
    }

    public static void UlozLogo(byte[]? logo)
    {
        if (logo is { Length: > NajvacsieLogo })
        {
            throw new EvitaException(Preklad.T("Logo je príliš veľké (najviac 2 MB). Zmenšite obrázok a skúste znova."));
        }

        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucLogo, logo is null ? null : Convert.ToBase64String(logo));
        db.SaveChanges();
    }
}

/// <summary>
/// Spoločný vzhľad všetkých tlačových zostáv a exportu do PDF: hlavička s názvom organizácie a logom,
/// nadpis zostavy, tabuľky so šírkou stĺpcov podľa obsahu, päta s dátumom tlače a číslom strany.
/// </summary>
public sealed class PdfZostava
{
    private const double Okraj = 1.2;
    private readonly int _velkostPisma;

    private static readonly Dictionary<string, string> _pisma = new();

    static PdfZostava() => PismaPdf.Zaregistruj();

    /// <summary>Chyby pri výbere písma (pre samotest).</summary>
    public static List<string> ChybyPisma { get; } = new();

    /// <summary>
    /// Písmo zostáv: Segoe UI; pre čínštinu písmo s čínskymi znakmi – zo súborov Windows (Microsoft YaHei …),
    /// inak prvé nainštalované, ktoré PDFsharp dokáže použiť.
    /// </summary>
    public static string Pismo
    {
        get
        {
            var kod = Preklad.Aktualny.Kod;
            lock (_pisma)
            {
                if (_pisma.TryGetValue(kod, out var znameNazov))
                {
                    return znameNazov;
                }

                var nazov = "Segoe UI";
                if (kod == "zh")
                {
                    var kandidati = new List<string>();
                    if (PismaPdf.NajdiSubory() is not null)
                    {
                        kandidati.Add(PismaPdf.NazovCinskeho);
                    }

                    var nainstalovane = System.Windows.Media.Fonts.SystemFontFamilies
                        .Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    kandidati.AddRange(new[] { "Microsoft YaHei", "Microsoft YaHei UI", "DengXian", "SimHei", "SimSun", "Microsoft JhengHei" }
                        .Where(nainstalovane.Contains));
                    foreach (var kandidat in kandidati)
                    {
                        try
                        {
                            _ = new PdfSharp.Drawing.XFont(kandidat, 10);
                            _ = new PdfSharp.Drawing.XFont(kandidat, 10, PdfSharp.Drawing.XFontStyleEx.Bold);
                            nazov = kandidat;
                            break;
                        }
                        catch (Exception ex)
                        {
                            // písmo PDFsharp nevie načítať – skúsi sa ďalšie
                            ChybyPisma.Add($"{kandidat}: {ex.Message}");
                        }
                    }
                }

                _pisma[kod] = nazov;
                return nazov;
            }
        }
    }

    public PdfZostava(string titulok, string? podtitulok, bool naSirku, int velkostPisma = 8)
    {
        Titulok = titulok;
        NaSirku = naSirku;
        _velkostPisma = velkostPisma;
        Cas = DateTime.Now;
        SirkaTabulky = (naSirku ? 29.7 : 21.0) - 2 * Okraj;

        Dokument = new Document();
        Dokument.Info.Title = titulok;
        Dokument.Info.Author = "EviTA";
        var normal = Dokument.Styles[StyleNames.Normal]!;
        normal.Font.Name = Pismo;
        normal.Font.Size = velkostPisma;

        Sekcia = Dokument.AddSection();
        Sekcia.PageSetup = Dokument.DefaultPageSetup.Clone();
        Sekcia.PageSetup.PageFormat = PageFormat.A4;
        Sekcia.PageSetup.Orientation = naSirku ? Orientation.Landscape : Orientation.Portrait;
        Sekcia.PageSetup.LeftMargin = Unit.FromCentimeter(Okraj);
        Sekcia.PageSetup.RightMargin = Unit.FromCentimeter(Okraj);
        Sekcia.PageSetup.TopMargin = Unit.FromCentimeter(2.4);
        Sekcia.PageSetup.HeaderDistance = Unit.FromCentimeter(0.8);
        Sekcia.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);
        Sekcia.PageSetup.FooterDistance = Unit.FromCentimeter(0.7);

        Hlavicka(NastaveniaTlace.Nacitaj());
        Paticka();

        var nadpis = Sekcia.AddParagraph(titulok);
        nadpis.Format.Font.Size = 14;
        nadpis.Format.Font.Bold = true;
        nadpis.Format.SpaceAfter = Unit.FromPoint(2);

        var popis = Sekcia.AddParagraph(string.IsNullOrWhiteSpace(podtitulok) ? Preklad.F($"vytlačené {Sk.DatumCas(Cas)}") : podtitulok);
        popis.Format.Font.Color = Colors.DimGray;
        popis.Format.SpaceAfter = Unit.FromPoint(8);
    }

    public Document Dokument { get; }
    public Section Sekcia { get; }
    public string Titulok { get; }
    public bool NaSirku { get; }
    public DateTime Cas { get; }

    /// <summary>Šírka tlačovej plochy v cm.</summary>
    public double SirkaTabulky { get; }

    // ------------------------------------------------------------------ hlavička a päta

    private void Hlavicka(Organizacia organizacia)
    {
        var tabulka = Sekcia.Headers.Primary.AddTable();
        tabulka.Borders.Visible = false;
        tabulka.AddColumn(Unit.FromCentimeter(SirkaTabulky - 5));
        tabulka.AddColumn(Unit.FromCentimeter(5));
        var riadok = tabulka.AddRow();
        riadok.VerticalAlignment = VerticalAlignment.Bottom;

        var vlavo = riadok.Cells[0];
        var nazov = vlavo.AddParagraph(organizacia.Nazov ?? Preklad.T("EviTA – evidencia IT techniky"));
        nazov.Format.Font.Bold = true;
        nazov.Format.Font.Size = 10;
        var podnazov = vlavo.AddParagraph(Titulok);
        podnazov.Format.Font.Size = 7.5;
        podnazov.Format.Font.Color = Colors.Gray;

        if (organizacia.Logo is { Length: > 0 } logo)
        {
            var vpravo = riadok.Cells[1].AddParagraph();
            vpravo.Format.Alignment = ParagraphAlignment.Right;
            var obrazok = vpravo.AddImage("base64:" + Convert.ToBase64String(logo));
            obrazok.Height = Unit.FromCentimeter(1.1);
            obrazok.LockAspectRatio = true;
        }

        var ciara = Sekcia.Headers.Primary.AddParagraph();
        ciara.Format.Borders.Bottom.Width = 0.5;
        ciara.Format.Borders.Bottom.Color = new Color(180, 180, 180);
        ciara.Format.Font.Size = 2;
    }

    private void Paticka()
    {
        var paticka = Sekcia.Footers.Primary.AddParagraph();
        paticka.Format.Alignment = ParagraphAlignment.Center;
        paticka.Format.Font.Size = 7;
        paticka.Format.Font.Color = Colors.Gray;
        paticka.AddText(Preklad.F($"Vytlačené {Sk.DatumCas(Cas)} · EviTA · strana "));
        paticka.AddPageField();
        paticka.AddText(Preklad.T(" z "));
        paticka.AddNumPagesField();
    }

    // ------------------------------------------------------------------ obsah

    public void NovaStrana() => Sekcia.AddPageBreak();

    /// <summary>Nadpis skupiny (miestnosť, kategória, osoba …) s počtom vpravo.</summary>
    public void NadpisSkupiny(string text, string? doplnok = null)
    {
        var odsek = Sekcia.AddParagraph();
        odsek.Format.SpaceBefore = Unit.FromPoint(10);
        odsek.Format.SpaceAfter = Unit.FromPoint(4);
        odsek.Format.KeepWithNext = true;
        var hlavny = odsek.AddFormattedText(text);
        hlavny.Font.Size = 11;
        hlavny.Font.Bold = true;
        if (!string.IsNullOrEmpty(doplnok))
        {
            var dalsi = odsek.AddFormattedText("   " + doplnok);
            dalsi.Font.Color = Colors.DimGray;
        }
    }

    public Paragraph Text(string text, bool tucne = false, bool sedy = false)
    {
        var odsek = Sekcia.AddParagraph(text);
        odsek.Format.Font.Bold = tucne;
        if (sedy)
        {
            odsek.Format.Font.Color = Colors.DimGray;
        }

        return odsek;
    }

    /// <summary>Riadok so súčtom pod tabuľkou.</summary>
    public void Spolu(string text)
    {
        var odsek = Sekcia.AddParagraph(text);
        odsek.Format.Font.Bold = true;
        odsek.Format.Alignment = ParagraphAlignment.Right;
        odsek.Format.SpaceBefore = Unit.FromPoint(3);
    }

    /// <summary>Podpisové riadky (napr. „Inventúru vykonal(a)“, „Dátum“, „Podpis“).</summary>
    public void Podpis(params string[] polia)
    {
        var tabulka = Sekcia.AddTable();
        tabulka.Borders.Visible = false;
        var sirka = SirkaTabulky / polia.Length;
        foreach (var _ in polia)
        {
            tabulka.AddColumn(Unit.FromCentimeter(sirka));
        }

        var riadok = tabulka.AddRow();
        riadok.TopPadding = Unit.FromPoint(24);
        riadok.KeepWith = 0;
        for (var i = 0; i < polia.Length; i++)
        {
            var bunka = riadok.Cells[i];
            bunka.Borders.Top.Visible = false;
            var ciara = bunka.AddParagraph("....................................................");
            ciara.Format.Font.Color = Colors.Gray;
            var popis = bunka.AddParagraph(polia[i]);
            popis.Format.Font.Size = _velkostPisma - 0.5;
            popis.Format.Font.Color = Colors.DimGray;
        }
    }

    /// <summary>Stĺpec s obrázkom (ikona kategórie) vložený pred stĺpec s daným poradím; obrázok PNG pre každý riadok.</summary>
    public sealed record StlpecIkon(int Poradie, Func<object, byte[]> Obrazok)
    {
        public const double Sirka = 0.7;

        public (int Poradie, IReadOnlyList<byte[]> Obrazky) Pre<T>(IEnumerable<T> riadky) where T : notnull =>
            (Poradie, riadky.Select(r => Obrazok(r)).ToList());
    }

    /// <summary>Tabuľka s hlavičkou, ktorá sa opakuje na každej strane. Sivé riadky (napr. vyradené) sú voliteľné.</summary>
    public void Tabulka(
        IReadOnlyList<string> hlavicky,
        IReadOnlyList<bool> vpravo,
        IReadOnlyList<string[]> riadky,
        double[] sirky,
        IReadOnlyList<bool>? sede = null,
        (int Poradie, IReadOnlyList<byte[]> Obrazky)? ikony = null)
    {
        if (ikony is { } stlpec)
        {
            // stĺpec s obrázkom: v textoch prázdna bunka, šírka sa vezme z ostatných stĺpcov (celková šírka sa nemení)
            var poradie = Math.Clamp(stlpec.Poradie, 0, hlavicky.Count);
            hlavicky = hlavicky.Take(poradie).Append("").Concat(hlavicky.Skip(poradie)).ToList();
            vpravo = vpravo.Take(poradie).Append(false).Concat(vpravo.Skip(poradie)).ToList();
            riadky = riadky.Select(r => r.Take(poradie).Append("").Concat(r.Skip(poradie)).ToArray()).ToList();
            var zvysok = sirky.Sum() - StlpecIkon.Sirka;
            var nasobok = zvysok / Math.Max(0.01, sirky.Sum());
            sirky = sirky.Take(poradie).Select(s => s * nasobok).Append(StlpecIkon.Sirka)
                .Concat(sirky.Skip(poradie).Select(s => s * nasobok)).ToArray();
            _ikonyTabulky = (poradie, stlpec.Obrazky);
        }
        else
        {
            _ikonyTabulky = null;
        }

        var tabulka = Sekcia.AddTable();
        tabulka.Borders.Width = 0.25;
        tabulka.Borders.Color = new Color(210, 210, 210);
        tabulka.Borders.Left.Visible = false;
        tabulka.Borders.Right.Visible = false;
        tabulka.TopPadding = Unit.FromPoint(1.5);
        tabulka.BottomPadding = Unit.FromPoint(1.5);
        foreach (var sirka in sirky)
        {
            tabulka.AddColumn(Unit.FromCentimeter(sirka));
        }

        var hlavicka = tabulka.AddRow();
        hlavicka.HeadingFormat = true;
        hlavicka.Format.Font.Bold = true;
        hlavicka.Shading.Color = new Color(220, 230, 241);
        for (var i = 0; i < hlavicky.Count; i++)
        {
            var odsek = hlavicka.Cells[i].AddParagraph(hlavicky[i]);
            if (vpravo[i])
            {
                odsek.Format.Alignment = ParagraphAlignment.Right;
            }
        }

        for (var r = 0; r < riadky.Count; r++)
        {
            var riadok = tabulka.AddRow();
            if (r % 2 == 1)
            {
                riadok.Shading.Color = new Color(247, 249, 252);
            }

            if (sede is not null && r < sede.Count && sede[r])
            {
                riadok.Format.Font.Color = Colors.Gray;
            }

            for (var i = 0; i < hlavicky.Count; i++)
            {
                if (_ikonyTabulky is { } ik && i == ik.Poradie && r < ik.Obrazky.Count)
                {
                    var obrazok = riadok.Cells[i].AddParagraph().AddImage("base64:" + Convert.ToBase64String(ik.Obrazky[r]));
                    obrazok.Width = Unit.FromPoint(_velkostPisma + 3);
                    obrazok.Height = Unit.FromPoint(_velkostPisma + 3);
                    riadok.Cells[i].VerticalAlignment = VerticalAlignment.Center;
                    continue;
                }

                var odsek = riadok.Cells[i].AddParagraph(riadky[r][i]);
                if (vpravo[i])
                {
                    odsek.Format.Alignment = ParagraphAlignment.Right;
                }
            }
        }

        _ikonyTabulky = null;
    }

    private (int Poradie, IReadOnlyList<byte[]> Obrazky)? _ikonyTabulky;

    // ------------------------------------------------------------------ šírky stĺpcov

    /// <summary>Šírky stĺpcov v cm podľa obsahu: najdlhšie slovo (nezalomí sa) a typická dĺžka textu.</summary>
    public double[] Sirky(IReadOnlyList<string> hlavicky, IReadOnlyList<string[]> riadky)
    {
        var vahy = new double[hlavicky.Count];
        for (var i = 0; i < hlavicky.Count; i++)
        {
            var dlzkaHlavicky = hlavicky[i].Split(' ').Max(w => SirkaTextu(w) * 1.1);
            var dlzky = riadky.Select(t => SirkaTextu(t[i])).OrderBy(x => x).ToList();
            var typicka = dlzky.Count == 0 ? 0 : dlzky[(int)Math.Floor((dlzky.Count - 1) * 0.9)];
            var slovo = riadky.Count == 0 ? 0 : riadky.Max(t => t[i].Split(' ').Max(SirkaTextu));
            vahy[i] = Math.Max(dlzkaHlavicky, Math.Max(Math.Min(slovo, 16), Math.Min(typicka, 30) * 0.7)) + 1.5;
        }

        var spolu = vahy.Sum();
        return vahy.Select(v => SirkaTabulky * v / spolu).ToArray();
    }

    /// <summary>Odporúčaná orientácia pre tabuľku s danými stĺpcami (na šírku pri viac ako 7 stĺpcoch alebo dlhých textoch).</summary>
    public static bool OdporucNaSirku(IReadOnlyList<string> hlavicky, IReadOnlyList<string[]> riadky)
    {
        var vaha = 0.0;
        for (var i = 0; i < hlavicky.Count; i++)
        {
            var dlzky = riadky.Select(t => SirkaTextu(t[i])).OrderBy(x => x).ToList();
            var typicka = dlzky.Count == 0 ? 0 : dlzky[(int)Math.Floor((dlzky.Count - 1) * 0.9)];
            vaha += Math.Max(hlavicky[i].Split(' ').Max(w => SirkaTextu(w)), Math.Min(typicka, 30) * 0.7) + 1.5;
        }

        return vaha > 105 || hlavicky.Count > 7;
    }

    /// <summary>Približná šírka textu v „priemerných znakoch“ (veľké písmená a číslice sú širšie, i, l, bodka užšie).</summary>
    public static double SirkaTextu(string text) => text.Sum(z =>
        char.IsUpper(z) || char.IsDigit(z) ? 1.3
        : z is 'i' or 'l' or 'j' or 't' or 'f' or 'í' or '.' or ',' or ':' or ' ' or '\'' ? 0.55
        : z is 'm' or 'w' or '€' ? 1.45
        : 1.0);

    /// <summary>Text bunky: kompaktné dátumy (20.10.2026), suma s nezalomiteľnou medzerou pred €, bez zalomení riadkov.</summary>
    public static string TextBunky(object? hodnota, TypStlpca typ)
    {
        var text = hodnota switch
        {
            DateTime d when typ == TypStlpca.DatumCas && d.TimeOfDay != TimeSpan.Zero => Sk.DatumCas(d),
            DateTime d => Sk.Datum(d),
            decimal s => s.ToString("N2", Sk.Kultura) + " €",
            _ => Prevod.TextZobrazenia(hodnota, typ),
        };
        return text.Replace("\r\n", " / ").Replace('\n', ' ').Replace('\r', ' ');
    }

    public static string Suma(decimal suma) => suma.ToString("N2", Sk.Kultura) + " €";

    // ------------------------------------------------------------------ výstup

    public static void UlozPdf(Document dokument, string cesta)
    {
        var vykreslenie = new PdfDocumentRenderer { Document = dokument };
        vykreslenie.RenderDocument();
        vykreslenie.PdfDocument.Save(cesta);
    }
}
