using System.Globalization;
using Evita.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Evita.Services;

/// <summary>Export tabuľky položiek do PDF (A4, pri viacerých stĺpcoch na šírku, s číslami strán).</summary>
internal static class PdfExport
{
    public static void Zapis(string cesta, string titulok, string popis, IReadOnlyList<StlpecPrenosu> stlpce, IReadOnlyList<Polozka> polozky)
    {
        var texty = polozky.Select(p => stlpce.Select(s => Text(StlpcePrenosu.Hodnota(p, s.Kluc), s.Typ)).ToArray()).ToList();

        // šírky stĺpcov podľa obsahu: najdlhšie slovo (nezalomí sa) a typická dĺžka textu
        var vahy = new double[stlpce.Count];
        for (var i = 0; i < stlpce.Count; i++)
        {
            var dlzkaHlavicky = Hlavicka(stlpce[i]).Split(' ').Max(w => w.Length);
            var dlzky = texty.Select(t => t[i].Length).OrderBy(x => x).ToList();
            var typicka = dlzky.Count == 0 ? 0 : dlzky[(int)Math.Floor((dlzky.Count - 1) * 0.9)];
            var slovo = texty.Count == 0 ? 0 : texty.Max(t => t[i].Split(' ').Max(w => w.Length));
            vahy[i] = Math.Max(dlzkaHlavicky, Math.Max(Math.Min(slovo, 16), Math.Min(typicka, 30) * 0.7)) + 1.5;
        }

        var spolu = vahy.Sum();
        var naSirku = spolu > 105 || stlpce.Count > 7;
        var velkostPisma = spolu > 190 ? 7 : 8;
        const double okraj = 1.2;
        var sirkaStrany = naSirku ? 29.7 : 21.0;
        var sirkaTabulky = sirkaStrany - 2 * okraj;

        var dokument = new Document();
        dokument.Info.Title = titulok;
        dokument.Info.Author = "EviTA";
        var normal = dokument.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Segoe UI";
        normal.Font.Size = velkostPisma;

        var sekcia = dokument.AddSection();
        sekcia.PageSetup = dokument.DefaultPageSetup.Clone();
        sekcia.PageSetup.PageFormat = PageFormat.A4;
        sekcia.PageSetup.Orientation = naSirku ? Orientation.Landscape : Orientation.Portrait;
        sekcia.PageSetup.LeftMargin = Unit.FromCentimeter(okraj);
        sekcia.PageSetup.RightMargin = Unit.FromCentimeter(okraj);
        sekcia.PageSetup.TopMargin = Unit.FromCentimeter(1.4);
        sekcia.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);
        sekcia.PageSetup.FooterDistance = Unit.FromCentimeter(0.7);

        var nadpis = sekcia.AddParagraph(titulok);
        nadpis.Format.Font.Size = 14;
        nadpis.Format.Font.Bold = true;
        nadpis.Format.SpaceAfter = Unit.FromPoint(2);

        var podnadpis = sekcia.AddParagraph(popis);
        podnadpis.Format.Font.Color = Colors.DimGray;
        podnadpis.Format.SpaceAfter = Unit.FromPoint(8);

        var tabulka = sekcia.AddTable();
        tabulka.Borders.Width = 0.25;
        tabulka.Borders.Color = new Color(210, 210, 210);
        tabulka.Borders.Left.Visible = false;
        tabulka.Borders.Right.Visible = false;
        tabulka.TopPadding = Unit.FromPoint(1.5);
        tabulka.BottomPadding = Unit.FromPoint(1.5);

        foreach (var vaha in vahy)
        {
            tabulka.AddColumn(Unit.FromCentimeter(sirkaTabulky * vaha / spolu));
        }

        var hlavicka = tabulka.AddRow();
        hlavicka.HeadingFormat = true;
        hlavicka.Format.Font.Bold = true;
        hlavicka.Shading.Color = new Color(220, 230, 241);
        for (var i = 0; i < stlpce.Count; i++)
        {
            hlavicka.Cells[i].AddParagraph(Hlavicka(stlpce[i]));
        }

        var poradie = 0;
        for (var index = 0; index < polozky.Count; index++)
        {
            var p = polozky[index];
            var riadok = tabulka.AddRow();
            if (poradie++ % 2 == 1)
            {
                riadok.Shading.Color = new Color(247, 249, 252);
            }

            if (p.Stav == StavPolozky.Vyradene)
            {
                riadok.Format.Font.Color = Colors.Gray;
            }

            for (var i = 0; i < stlpce.Count; i++)
            {
                var odsek = riadok.Cells[i].AddParagraph(texty[index][i]);
                if (stlpce[i].Typ is TypStlpca.CeleCislo or TypStlpca.Suma)
                {
                    odsek.Format.Alignment = ParagraphAlignment.Right;
                }
            }
        }

        var paticka = sekcia.Footers.Primary.AddParagraph();
        paticka.Format.Alignment = ParagraphAlignment.Center;
        paticka.Format.Font.Size = 7;
        paticka.Format.Font.Color = Colors.Gray;
        paticka.AddText($"EviTA · {Sk.DatumCas(DateTime.Now)} · strana ");
        paticka.AddPageField();
        paticka.AddText(" z ");
        paticka.AddNumPagesField();

        var vykreslenie = new PdfDocumentRenderer { Document = dokument };
        vykreslenie.RenderDocument();
        vykreslenie.PdfDocument.Save(cesta);
    }

    private static string Hlavicka(StlpecPrenosu stlpec) => stlpec.Kluc == StlpcePrenosu.Cislo ? "Č." : stlpec.Nazov;

    /// <summary>Text bunky: kompaktné dátumy (20.10.2026), suma s nezalomiteľnou medzerou pred €, bez zalomení riadkov.</summary>
    private static string Text(object? hodnota, TypStlpca typ)
    {
        var text = hodnota switch
        {
            DateTime d when typ == TypStlpca.DatumCas && d.TimeOfDay != TimeSpan.Zero => d.ToString("d.M.yyyy H:mm", CultureInfo.InvariantCulture),
            DateTime d => d.ToString("d.M.yyyy", CultureInfo.InvariantCulture),
            decimal s => s.ToString("N2", Sk.Kultura) + "\u00A0€",
            _ => Prevod.TextZobrazenia(hodnota, typ),
        };
        return text.Replace("\r\n", " / ").Replace('\n', ' ').Replace('\r', ' ');
    }
}
