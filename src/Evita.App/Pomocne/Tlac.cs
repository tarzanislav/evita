using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Evita.Pomocne;

/// <summary>Jednoduchá tlač textu (protokoly) a otváranie súborov a priečinkov.</summary>
public static class Tlac
{
    public static void Text(string titulok, string text)
    {
        if (Dialogy.TichyRezim)
        {
            Dialogy.TicheHlasenia.Add($"[Tlač] {titulok}");
            return;
        }

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var odsek = new Paragraph();
        var riadky = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < riadky.Length; i++)
        {
            if (i > 0)
            {
                odsek.Inlines.Add(new LineBreak());
            }

            odsek.Inlines.Add(new Run(riadky[i]));
        }

        var dokument = new FlowDocument(odsek)
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            PagePadding = new Thickness(56),
            ColumnWidth = double.PositiveInfinity,
            PageWidth = dialog.PrintableAreaWidth,
            PageHeight = dialog.PrintableAreaHeight,
        };
        dialog.PrintDocument(((IDocumentPaginatorSource)dokument).DocumentPaginator, titulok);
    }

    /// <summary>Otvorí súbor v predvolenom programe (Excel, prehliadač …).</summary>
    public static void OtvorSubor(string cesta)
    {
        if (Dialogy.TichyRezim)
        {
            Dialogy.TicheHlasenia.Add($"[Otvoriť] {Path.GetFileName(cesta)}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(cesta) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogy.Chyba($"Súbor sa nepodarilo otvoriť:\n{cesta}", ex);
        }
    }

    /// <summary>Otvorí priečinok v Prieskumníkovi (voliteľne s označeným súborom).</summary>
    public static void OtvorPriecinok(string priecinok, string? oznacitSubor = null)
    {
        if (Dialogy.TichyRezim)
        {
            Dialogy.TicheHlasenia.Add($"[Priečinok] {priecinok}");
            return;
        }

        try
        {
            var argumenty = oznacitSubor is not null && File.Exists(oznacitSubor)
                ? $"/select,\"{oznacitSubor}\""
                : $"\"{priecinok}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", argumenty) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogy.Chyba($"Priečinok sa nepodarilo otvoriť:\n{priecinok}", ex);
        }
    }
}
