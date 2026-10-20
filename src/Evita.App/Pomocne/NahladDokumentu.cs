using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Drawing;

namespace Evita.Pomocne;

/// <summary>
/// Vykreslenie zostavy (dokument MigraDoc) na obrazovku a na tlačiareň. Náhľad aj tlač používajú
/// rovnaké vykreslenie ako PDF, takže vytlačená zostava vyzerá rovnako ako uložené PDF.
/// </summary>
public static class NahladDokumentu
{
    private const double BodovNaDip = 96.0 / 72.0;

    /// <summary>Pripraví rozloženie dokumentu (stránkovanie).</summary>
    public static DocumentRenderer Priprav(Document dokument)
    {
        var vykreslenie = new DocumentRenderer(dokument);
        vykreslenie.PrepareDocument();
        return vykreslenie;
    }

    public static int PocetStran(DocumentRenderer vykreslenie) => vykreslenie.FormattedDocument!.PageCount;

    /// <summary>Jedna strana ako vektorový obrázok (ostrý pri každom zväčšení).</summary>
    public static DrawingImage Obrazok(DocumentRenderer vykreslenie, int strana, bool naSirku)
    {
        var kresba = Strana(vykreslenie, strana, naSirku, out _);
        var obrazok = new DrawingImage(kresba.Drawing);
        obrazok.Freeze();
        return obrazok;
    }

    /// <summary>Vykreslí stranu (číslovanú od 1) do vizuálu vo veľkosti strany A4 v jednotkách WPF.</summary>
    public static DrawingVisual Strana(DocumentRenderer vykreslenie, int strana, bool naSirku, out Size velkost)
    {
        var info = vykreslenie.FormattedDocument!.GetPageInfo(strana);
        double sirka = info.Width;
        double vyska = info.Height;
        if (naSirku != (sirka > vyska))
        {
            (sirka, vyska) = (vyska, sirka);
        }

        velkost = new Size(sirka * BodovNaDip, vyska * BodovNaDip);
        var kresba = new DrawingVisual();
        using (var kontext = kresba.RenderOpen())
        {
            kontext.DrawRectangle(Brushes.White, null, new Rect(velkost));
            using var grafika = XGraphics.FromDrawingContext(kontext, new XSize(sirka, vyska), XGraphicsUnit.Point, new PdfSharp.Events.RenderEvents());
            vykreslenie.RenderPage(grafika, strana);
        }

        return kresba;
    }

    /// <summary>Vytlačí dokument cez okno tlače Windows. Vráti false, ak používateľ tlač zrušil.</summary>
    public static bool Vytlac(Func<Document> tvorba, bool naSirku, string nazovUlohy)
    {
        if (Dialogy.TichyRezim)
        {
            Dialogy.TicheHlasenia.Add($"[Tlač] {nazovUlohy}");
            return true;
        }

        var dialog = new PrintDialog { UserPageRangeEnabled = true };
        try
        {
            dialog.PrintTicket.PageOrientation = naSirku ? PageOrientation.Landscape : PageOrientation.Portrait;
            dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
        }
        catch (Exception)
        {
            // niektoré tlačiarne nastavenie nepodporujú – použijú svoje predvolené
        }

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        DocumentRenderer? vykreslenie = null;
        Dialogy.Cakaj(() => vykreslenie = Priprav(tvorba()));
        var strankovac = new Strankovac(vykreslenie!, naSirku);
        if (dialog.PageRangeSelection == PageRangeSelection.UserPages)
        {
            strankovac.Rozsah(dialog.PageRange.PageFrom, dialog.PageRange.PageTo);
        }

        dialog.PrintDocument(strankovac, nazovUlohy);
        return true;
    }

    /// <summary>Strany zostavy pre tlač Windows.</summary>
    private sealed class Strankovac : DocumentPaginator
    {
        private readonly DocumentRenderer _vykreslenie;
        private readonly bool _naSirku;
        private int _od = 1;
        private int _po;

        public Strankovac(DocumentRenderer vykreslenie, bool naSirku)
        {
            _vykreslenie = vykreslenie;
            _naSirku = naSirku;
            _po = PocetStran(vykreslenie);
            PageSize = naSirku ? new Size(29.7 / 2.54 * 96, 21.0 / 2.54 * 96) : new Size(21.0 / 2.54 * 96, 29.7 / 2.54 * 96);
        }

        public void Rozsah(int od, int po)
        {
            var pocet = PocetStran(_vykreslenie);
            _od = Math.Clamp(od, 1, pocet);
            _po = Math.Clamp(po, _od, pocet);
        }

        public override bool IsPageCountValid => true;

        public override int PageCount => _po - _od + 1;

        public override Size PageSize { get; set; }

        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            var kresba = Strana(_vykreslenie, _od + pageNumber, _naSirku, out var velkost);
            return new DocumentPage(kresba, velkost, new Rect(velkost), new Rect(velkost));
        }
    }
}
