using System.Globalization;
using System.Printing;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Evita.Data;
using Evita.Services;

namespace Evita.Pomocne;

/// <summary>Ako tlačiareň tlačí obojstranne.</summary>
public enum RezimObojstrannej
{
    /// <summary>Tlačiareň otáča list sama (duplex).</summary>
    Automaticky = 0,

    /// <summary>Najprv sa vytlačia predné strany, používateľ vloží listy späť a vytlačia sa zadné strany.</summary>
    Rucne = 1,
}

/// <summary>
/// Nastavenie obojstrannej tlače pre jednu tlačiareň – zistené sprievodcom so skúšobnými listami (ako vyšla zadná strana).
/// Tlačiarne otáčajú list rôzne, preto sa zadná strana podľa potreby otočí o 180° a pri ručnom otáčaní aj obráti poradie.
/// </summary>
public sealed record ProfilObojstrannej
{
    public RezimObojstrannej Rezim { get; init; }

    /// <summary>Zadné strany listov na výšku (1 alebo 4 strany na list) otočiť o 180°.</summary>
    public bool OtocitNaVysku { get; init; }

    /// <summary>Zadné strany listov na šírku (2 alebo 8 strán na list, brožúra) otočiť o 180°.</summary>
    public bool OtocitNaSirku { get; init; }

    /// <summary>Ručné otáčanie: zadné strany tlačiť v obrátenom poradí (posledný list prvý).</summary>
    public bool ObratitPoradie { get; init; }

    public const string KlucNastavenia = "Tlac.Obojstranne";

    public bool Otocit(bool listNaSirku) => listNaSirku ? OtocitNaSirku : OtocitNaVysku;

    private static Dictionary<string, ProfilObojstrannej> Vsetky()
    {
        try
        {
            using var db = Databaza.Otvor();
            if (Databaza.Citaj(db, KlucNastavenia) is { Length: > 0 } json
                && JsonSerializer.Deserialize<Dictionary<string, ProfilObojstrannej>>(json) is { } profily)
            {
                return new Dictionary<string, ProfilObojstrannej>(profily, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception)
        {
            // bez uložených profilov
        }

        return new Dictionary<string, ProfilObojstrannej>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Profil tlačiarne, alebo null, ak ešte nebol nastavený sprievodcom.</summary>
    public static ProfilObojstrannej? Pre(string? tlaciaren) =>
        tlaciaren is not null && Vsetky().TryGetValue(tlaciaren, out var profil) ? profil : null;

    public static void Uloz(string tlaciaren, ProfilObojstrannej profil)
    {
        var profily = Vsetky();
        profily[tlaciaren] = profil;
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucNastavenia, JsonSerializer.Serialize(profily));
        db.SaveChanges();
    }

    /// <summary>Popis pre okno tlače.</summary>
    public string Popis => Rezim == RezimObojstrannej.Rucne
        ? Preklad.T("ručné otáčanie listov") + (ObratitPoradie ? ", " + Preklad.T("obrátené poradie") : "")
        : Preklad.T("tlačiareň otáča sama") + (OtocitNaVysku || OtocitNaSirku ? ", " + Preklad.T("zadné strany otočené") : "");
}

/// <summary>Zoznam strán na tlač (ľubovoľné kresby rovnakej veľkosti).</summary>
public sealed class ZoznamStran : DocumentPaginator
{
    private readonly Func<int, Visual> _strana;
    private readonly int _pocet;

    public ZoznamStran(int pocet, Size velkost, Func<int, Visual> strana)
    {
        _pocet = pocet;
        _strana = strana;
        PageSize = velkost;
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => _pocet;

    public override Size PageSize { get; set; }

    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber) =>
        new(_strana(pageNumber), PageSize, new Rect(PageSize), new Rect(PageSize));
}

/// <summary>Pomocné kreslenie pre obojstrannú tlač: otočenie, prázdna strana a skúšobné listy sprievodcu.</summary>
public static class ObojstrannaKresba
{
    private const double Mm = 96.0 / 25.4;

    /// <summary>Strana otočená o 180° (zadná strana pri tlačiarni, ktorá ju inak vytlačí hore nohami).</summary>
    public static DrawingVisual Otocena(DrawingVisual povodna, Size velkost)
    {
        var vysledok = new DrawingVisual();
        using (var dc = vysledok.RenderOpen())
        {
            dc.PushTransform(new RotateTransform(180, velkost.Width / 2, velkost.Height / 2));
            dc.DrawDrawing(povodna.Drawing);
            dc.Pop();
        }

        return vysledok;
    }

    public static DrawingVisual Prazdna(Size velkost)
    {
        var vysledok = new DrawingVisual();
        using (var dc = vysledok.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(velkost));
        }

        return vysledok;
    }

    /// <summary>
    /// Skúšobná strana: veľké číslo, šípka HORE a dva „krúžky dierovača“ – na prednej strane pri ľavej hrane, na zadnej pri
    /// pravej. Ak je zadná strana správne, po otočení listu ako stránky knihy (okolo ľavej hrany) stojí číslo rovno
    /// a krúžky sa pri pohľade proti svetlu prekrývajú.
    /// </summary>
    public static DrawingVisual SkusobnaStrana(int cislo, Size velkost, bool zadna)
    {
        var vysledok = new DrawingVisual();
        var pismo = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var pismoText = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        using (var dc = vysledok.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(velkost));
            var ramcek = new Pen(Brushes.Black, 1.5);
            dc.DrawRectangle(null, ramcek, new Rect(8 * Mm, 8 * Mm, velkost.Width - 16 * Mm, velkost.Height - 16 * Mm));

            var text = new FormattedText(cislo.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, pismo,
                Math.Min(velkost.Width, velkost.Height) * 0.45, Brushes.Black, 1.0);
            dc.DrawText(text, new Point((velkost.Width - text.Width) / 2, (velkost.Height - text.Height) / 2));

            // šípka hore
            var stred = velkost.Width / 2;
            var sipka = new StreamGeometry();
            using (var g = sipka.Open())
            {
                g.BeginFigure(new Point(stred, 16 * Mm), true, true);
                g.LineTo(new Point(stred + 14 * Mm, 34 * Mm), true, false);
                g.LineTo(new Point(stred + 5 * Mm, 34 * Mm), true, false);
                g.LineTo(new Point(stred + 5 * Mm, 52 * Mm), true, false);
                g.LineTo(new Point(stred - 5 * Mm, 52 * Mm), true, false);
                g.LineTo(new Point(stred - 5 * Mm, 34 * Mm), true, false);
                g.LineTo(new Point(stred - 14 * Mm, 34 * Mm), true, false);
            }

            dc.DrawGeometry(Brushes.Black, null, sipka);
            var hore = new FormattedText(Preklad.T("HORE"), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, pismo, 16 * Mm / 2, Brushes.Black, 1.0);
            dc.DrawText(hore, new Point(stred - hore.Width / 2, 54 * Mm));

            var popis = new FormattedText(
                zadna ? Preklad.F($"EviTA – skúšobný list, zadná strana {cislo}") : Preklad.F($"EviTA – skúšobný list, predná strana {cislo}"),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, pismoText, 14, Brushes.Black, 1.0);
            dc.DrawText(popis, new Point(stred - popis.Width / 2, velkost.Height - 22 * Mm));

            // krúžky dierovača (nesúmerne – pri otočení o 180° by sa neprekrývali)
            var x = zadna ? velkost.Width - 18 * Mm : 18 * Mm;
            foreach (var podiel in new[] { 0.25, 0.5 })
            {
                dc.DrawEllipse(null, new Pen(Brushes.Black, 2), new Point(x, velkost.Height * podiel), 5 * Mm, 5 * Mm);
                dc.DrawEllipse(Brushes.Black, null, new Point(x, velkost.Height * podiel), 1.2 * Mm, 1.2 * Mm);
            }
        }

        return vysledok;
    }

    /// <summary>Odošle strany na tlačiareň ako jednu tlačovú úlohu.</summary>
    public static void Posli(PrintQueue fronta, PrintTicket tiket, DocumentPaginator strany, string nazov)
    {
        fronta.CurrentJobSettings.Description = nazov;
        var zapis = PrintQueue.CreateXpsDocumentWriter(fronta);
        zapis.Write(strany, tiket);
    }

    /// <summary>Tlačiareň podľa názvu (lokálna alebo pripojená zo siete).</summary>
    public static PrintQueue NajdiTlaciaren(LocalPrintServer server, string nazov) =>
        server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
            .FirstOrDefault(f => f.FullName == nazov) ?? throw new EvitaException(Preklad.F($"Tlačiareň „{nazov}“ sa nenašla."));
}
