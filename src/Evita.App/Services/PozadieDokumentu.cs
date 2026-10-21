using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;

namespace Evita.Services;

/// <summary>
/// Voľba „Bez pozadia“ pri tlači a PDF: z dokumentu sa odstránia farebné podklady (hlavička tabuľky, striedanie riadkov,
/// zvýraznené dni v kalendári, výplne rámčekov). Zostane biely papier – text, čiary, obrázky a logo sa nemenia.
/// </summary>
public static class PozadieDokumentu
{
    /// <summary>Odstráni podklady priamo v dokumente a vráti ten istý dokument.</summary>
    public static Document Odstran(Document dokument)
    {
        foreach (var styl in dokument.Styles.OfType<Style>())
        {
            Format(styl.Values.ParagraphFormat);
        }

        foreach (var sekcia in dokument.Sections.OfType<Section>())
        {
            Prvky(sekcia.Values.Elements);
            if (sekcia.Values.Headers is { } hlavicky)
            {
                HlavickaPata(hlavicky.Values.Primary);
                HlavickaPata(hlavicky.Values.FirstPage);
                HlavickaPata(hlavicky.Values.EvenPage);
            }

            if (sekcia.Values.Footers is { } paty)
            {
                HlavickaPata(paty.Values.Primary);
                HlavickaPata(paty.Values.FirstPage);
                HlavickaPata(paty.Values.EvenPage);
            }
        }

        return dokument;
    }

    private static void HlavickaPata(HeaderFooter? cast)
    {
        if (cast is not null)
        {
            Format(cast.Values.Format);
            Prvky(cast.Values.Elements);
        }
    }

    private static void Prvky(DocumentElements? prvky)
    {
        if (prvky is null)
        {
            return;
        }

        foreach (var prvok in prvky.OfType<DocumentObject>())
        {
            switch (prvok)
            {
                case Paragraph odsek:
                    Format(odsek.Values.Format);
                    break;
                case Table tabulka:
                    Tabulka(tabulka);
                    break;
                case TextFrame ramec:
                    ramec.FillFormat.Visible = false;
                    Prvky(ramec.Elements);
                    break;
            }
        }
    }

    private static void Tabulka(Table tabulka)
    {
        Skry(tabulka.Values.Shading);
        Format(tabulka.Values.Format);
        foreach (var stlpec in tabulka.Columns.OfType<Column>())
        {
            Skry(stlpec.Values.Shading);
            Format(stlpec.Values.Format);
        }

        foreach (var riadok in tabulka.Rows.OfType<Row>())
        {
            Skry(riadok.Values.Shading);
            Format(riadok.Values.Format);
            foreach (var bunka in riadok.Cells.OfType<Cell>())
            {
                Skry(bunka.Values.Shading);
                Format(bunka.Values.Format);
                Prvky(bunka.Values.Elements);
            }
        }
    }

    private static void Format(ParagraphFormat? format) => Skry(format?.Values.Shading);

    private static void Skry(Shading? podklad)
    {
        if (podklad is not null)
        {
            podklad.Visible = false;
        }
    }
}
