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
        var naSirku = stlpce.Count > 5;
        var velkostPisma = stlpce.Count > 10 ? 7 : 8;
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

        var spolu = stlpce.Sum(s => s.Sirka);
        foreach (var s in stlpce)
        {
            tabulka.AddColumn(Unit.FromCentimeter(sirkaTabulky * s.Sirka / spolu));
        }

        var hlavicka = tabulka.AddRow();
        hlavicka.HeadingFormat = true;
        hlavicka.Format.Font.Bold = true;
        hlavicka.Shading.Color = new Color(220, 230, 241);
        for (var i = 0; i < stlpce.Count; i++)
        {
            hlavicka.Cells[i].AddParagraph(stlpce[i].Nazov);
        }

        var poradie = 0;
        foreach (var p in polozky)
        {
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
                var text = Prevod.TextZobrazenia(StlpcePrenosu.Hodnota(p, stlpce[i].Kluc), stlpce[i].Typ)
                    .Replace("\r\n", " / ").Replace('\n', ' ').Replace('\r', ' ');
                var odsek = riadok.Cells[i].AddParagraph(text);
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
}
