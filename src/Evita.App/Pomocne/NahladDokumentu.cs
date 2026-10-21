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

    /// <summary>
    /// Vytlačí dokument cez okno tlače EviTA: výber tlačiarne, kópie, obojstranná tlač, 1/2/4/8 strán na list, opakovanie
    /// strán, brožúra, zväčšenie, rámik, odtiene sivej a dierovač. Vráti false, ak používateľ tlač zrušil.
    /// </summary>
    public static bool Vytlac(Func<Document> tvorba, bool naSirku, string nazovUlohy)
    {
        if (Dialogy.TichyRezim)
        {
            Dialogy.TicheHlasenia.Add($"[Tlač] {nazovUlohy}");
            return true;
        }

        DocumentRenderer? vykreslenie = null;
        Dialogy.Cakaj(() => vykreslenie = Priprav(tvorba()));
        var okno = new Views.TlacWindow(vykreslenie!, naSirku, nazovUlohy, Views.RezimOknaTlace.Tlac, tvorba, null) { Owner = Dialogy.Vlastnik };
        return okno.ShowDialog() == true;
    }

    /// <summary>Strana zostavy ako kresba na umiestnenie na list.</summary>
    public static LogickaStrana Logicka(DocumentRenderer vykreslenie, int strana, bool naSirku)
    {
        var vizual = Strana(vykreslenie, strana, naSirku, out var velkost);
        var kresba = vizual.Drawing;

        // obsah strany = všetko okrem bieleho podkladu (prvá kresba), s rezervou 2 mm
        var obsah = Rect.Empty;
        foreach (var cast in kresba.Children.Skip(1))
        {
            obsah.Union(cast.Bounds);
        }

        if (!obsah.IsEmpty)
        {
            obsah.Inflate(2 * 96 / 25.4, 2 * 96 / 25.4);
            obsah.Intersect(new Rect(velkost));
        }

        if (kresba.CanFreeze)
        {
            kresba.Freeze();
        }

        return new LogickaStrana(kresba, velkost, obsah);
    }

    /// <summary>Listy na tlač podľa volieb (rozmiestnenie strán, sivá …) pre tlačiareň Windows.</summary>
    public sealed class StrankovacRozvrhu : DocumentPaginator
    {
        private readonly DocumentRenderer _vykreslenie;
        private readonly bool _naSirku;
        private readonly VolbyTlace _volby;
        private readonly List<int?[]> _listy;
        private readonly Dictionary<int, LogickaStrana> _strany = new();

        public StrankovacRozvrhu(DocumentRenderer vykreslenie, bool naSirku, VolbyTlace volby)
        {
            _vykreslenie = vykreslenie;
            _naSirku = naSirku;
            _volby = volby;
            _listy = RozvrhTlace.Rozdel(PocetStran(vykreslenie), volby);
            ListNaSirku = RozvrhTlace.Mriezka(volby.Rozmiestnenie, naSirku).ListNaSirku;
            PageSize = RozvrhTlace.VelkostListu(ListNaSirku);
        }

        public bool ListNaSirku { get; }

        /// <summary>Strany listov (pri obojstrannej tlači každá druhá je zadná strana).</summary>
        public int PocetListov => _listy.Count;

        public override bool IsPageCountValid => true;

        public override int PageCount => _listy.Count;

        public override Size PageSize { get; set; }

        public override IDocumentPaginatorSource? Source => null;

        /// <summary>Strana listu ako vizuál (aj pre náhľad v okne tlače).</summary>
        public DrawingVisual List(int index) =>
            RozvrhTlace.List(_listy[index], Strana, _volby, _naSirku, index % 2 == 1);

        private LogickaStrana Strana(int cislo)
        {
            if (!_strany.TryGetValue(cislo, out var strana))
            {
                strana = Logicka(_vykreslenie, cislo, _naSirku);
                if (_volby.OdtieneSivej)
                {
                    strana = RozvrhTlace.NaSivu(strana, NahladDpi ?? 250);
                }

                _strany[cislo] = strana;
            }

            return strana;
        }

        /// <summary>Rozlíšenie sivých strán v náhľade (nižšie = rýchlejšie); pri tlači null = 250 dpi.</summary>
        public double? NahladDpi { get; init; }

        public override DocumentPage GetPage(int pageNumber)
        {
            var vizual = List(pageNumber);
            return new DocumentPage(vizual, PageSize, new Rect(PageSize), new Rect(PageSize));
        }
    }

    /// <summary>
    /// Tlačový lístok pre vybranú tlačiareň: A4, orientácia listu, obojstranná tlač, kópie a sivá – nastaví sa pri každej
    /// tlačiarni znova (každá má svoje predvolené) a zosúladí sa s jej možnosťami.
    /// </summary>
    public static PrintTicket Tiket(PrintQueue fronta, VolbyTlace volby, bool listNaSirku, bool? obojstranne = null)
    {
        var duplex = obojstranne ?? volby.TlacitObojstranne;
        var tiket = (fronta.UserPrintTicket ?? fronta.DefaultPrintTicket).Clone();
        tiket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
        tiket.PageOrientation = listNaSirku ? PageOrientation.Landscape : PageOrientation.Portrait;
        tiket.CopyCount = volby.Kopie;
        tiket.Collation = Collation.Collated;
        tiket.Duplexing = !duplex
            ? Duplexing.OneSided
            : listNaSirku ? Duplexing.TwoSidedShortEdge : Duplexing.TwoSidedLongEdge;
        if (volby.OdtieneSivej)
        {
            tiket.OutputColor = OutputColor.Grayscale;
        }

        try
        {
            var overeny = fronta.MergeAndValidatePrintTicket(fronta.UserPrintTicket ?? fronta.DefaultPrintTicket, tiket).ValidatedPrintTicket;
            overeny.PageOrientation = tiket.PageOrientation;
            return overeny;
        }
        catch (Exception)
        {
            return tiket;
        }
    }
}
