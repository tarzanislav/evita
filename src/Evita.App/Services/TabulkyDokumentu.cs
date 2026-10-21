using System.IO;
using System.Text;
using ClosedXML.Excel;
using Evita.Pomocne;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Fields;
using MigraDoc.DocumentObjectModel.Tables;

namespace Evita.Services;

/// <summary>Obsah zostavy (nadpisy a tabuľky) ako zošit Excelu – vkladá sa do PDF/A-3 ako príloha s dátami.</summary>
public static class TabulkyDokumentu
{
    public static byte[] Xlsx(Document dokument)
    {
        using var kniha = new XLWorkbook();
        var list = kniha.AddWorksheet(Preklad.T("Zostava"));
        var riadok = 1;
        if (!string.IsNullOrWhiteSpace(dokument.Info.Title))
        {
            list.Cell(riadok, 1).Value = dokument.Info.Title;
            list.Cell(riadok, 1).Style.Font.Bold = true;
            list.Cell(riadok, 1).Style.Font.FontSize = 13;
            riadok += 2;
        }

        foreach (var sekcia in dokument.Sections.OfType<Section>())
        {
            foreach (var prvok in sekcia.Elements.OfType<DocumentObject>())
            {
                switch (prvok)
                {
                    case Paragraph odsek when TextOdseku(odsek) is { Length: > 0 } text:
                        list.Cell(riadok, 1).Value = text;
                        list.Cell(riadok, 1).Style.Font.Bold = odsek.Format.OutlineLevel != OutlineLevel.BodyText || odsek.Format.Font.Bold;
                        riadok++;
                        break;
                    case Table tabulka:
                        foreach (var r in tabulka.Rows.OfType<Row>())
                        {
                            var stlpec = 1;
                            foreach (var bunka in r.Cells.OfType<Cell>())
                            {
                                var hodnota = string.Join(" ", bunka.Elements.OfType<Paragraph>().Select(TextOdseku).Where(t => t.Length > 0));
                                var ciel = list.Cell(riadok, stlpec++);
                                ciel.Value = hodnota;
                                if (r.HeadingFormat)
                                {
                                    ciel.Style.Font.Bold = true;
                                    ciel.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
                                }
                            }

                            riadok++;
                        }

                        riadok++;
                        break;
                }
            }
        }

        list.Columns().AdjustToContents(8, 60);
        using var prud = new MemoryStream();
        kniha.SaveAs(prud);
        return prud.ToArray();
    }

    /// <summary>Text odseku (bez obrázkov a polí s číslom strany).</summary>
    public static string TextOdseku(Paragraph odsek)
    {
        var text = new StringBuilder();
        Pridaj(odsek.Elements, text);
        return string.Join(" ", text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static void Pridaj(ParagraphElements? prvky, StringBuilder text)
    {
        if (prvky is null)
        {
            return;
        }

        foreach (var prvok in prvky.OfType<DocumentObject>())
        {
            switch (prvok)
            {
                case MigraDoc.DocumentObjectModel.Text t:
                    text.Append(t.Content);
                    break;
                case Character:
                    text.Append(' ');
                    break;
                case FormattedText f:
                    Pridaj(f.Elements, text);
                    break;
                case Hyperlink h:
                    Pridaj(h.Elements, text);
                    break;
                case PageField or NumPagesField:
                    break;
            }
        }
    }
}
