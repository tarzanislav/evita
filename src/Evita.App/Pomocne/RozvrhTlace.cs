using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Evita.Data;

namespace Evita.Pomocne;

/// <summary>Koľko strán zostavy sa vytlačí na jeden list papiera.</summary>
public enum Rozmiestnenie
{
    Jedna = 1,
    Dve = 2,
    Styri = 4,
    Osem = 8,

    /// <summary>Brožúra: listy sa po obojstrannej tlači preložia v strede a vznikne zošit A5.</summary>
    Brozura = 100,
}

/// <summary>Voľby tlače (vlastné rozmiestnenie strán – bez akéhokoľvek doplnkového programu).</summary>
public sealed record VolbyTlace
{
    public string? Tlaciaren { get; init; }
    public int Kopie { get; init; } = 1;
    public bool Obojstranne { get; init; }
    public Rozmiestnenie Rozmiestnenie { get; init; } = Rozmiestnenie.Jedna;

    /// <summary>Každá strana sa zopakuje toľkokrát, koľko sa ich zmestí na list (2, 4, 8 rovnakých strán vedľa seba).</summary>
    public bool OpakovatStranky { get; init; }

    /// <summary>Zväčšenie zmenšených strán (1 = bez zväčšenia, 1,3 = o 30 %) – okraje strán sa orežú, obsah je väčší.</summary>
    public double Zvacsenie { get; init; } = 1.0;

    /// <summary>Tenký rám okolo každej strany na liste.</summary>
    public bool Ramcek { get; init; }

    public bool OdtieneSivej { get; init; }

    /// <summary>Vynechať okraj na dierovanie (pri obojstrannej tlači na zadnej strane sprava).</summary>
    public bool Dierovac { get; init; }

    public double DierovacMm { get; init; } = 10;

    public const string KlucNastavenia = "Tlac.Volby";

    public static VolbyTlace Nacitaj()
    {
        try
        {
            using var db = Databaza.Otvor();
            if (Databaza.Citaj(db, KlucNastavenia) is { Length: > 0 } json && JsonSerializer.Deserialize<VolbyTlace>(json) is { } volby)
            {
                return volby.Opravene();
            }
        }
        catch (Exception)
        {
            // predvolené voľby
        }

        return new VolbyTlace();
    }

    public void Uloz()
    {
        try
        {
            using var db = Databaza.Otvor();
            Databaza.Zapis(db, KlucNastavenia, JsonSerializer.Serialize(Opravene()));
            db.SaveChanges();
        }
        catch (Exception)
        {
            // voľby sa nezapamätajú – tlač funguje aj tak
        }
    }

    /// <summary>Hodnoty v povolenom rozsahu.</summary>
    public VolbyTlace Opravene() => this with
    {
        Kopie = Math.Clamp(Kopie, 1, 999),
        Zvacsenie = Math.Clamp(double.IsFinite(Zvacsenie) ? Zvacsenie : 1, 1, 1.6),
        DierovacMm = Math.Clamp(double.IsFinite(DierovacMm) ? DierovacMm : 10, 0, 40),
        Rozmiestnenie = Enum.IsDefined(Rozmiestnenie) ? Rozmiestnenie : Rozmiestnenie.Jedna,
        OpakovatStranky = OpakovatStranky && Rozmiestnenie is not (Rozmiestnenie.Jedna or Rozmiestnenie.Brozura),
        Dierovac = Dierovac && Rozmiestnenie != Rozmiestnenie.Brozura,
    };

    /// <summary>Brožúra sa tlačí vždy obojstranne (otáčanie po kratšej strane).</summary>
    public bool TlacitObojstranne => Obojstranne || Rozmiestnenie == Rozmiestnenie.Brozura;
}

/// <summary>Strana zostavy pripravená na umiestnenie na list.</summary>
/// <param name="Obsah">oblasť strany, kde je text a obrázky (bez prázdnych okrajov) – na zväčšenie bez orezania obsahu</param>
public sealed record LogickaStrana(Drawing Kresba, Size Velkost, Rect Obsah);

/// <summary>
/// Rozmiestnenie strán zostavy na listy A4: 1, 2, 4 alebo 8 strán na list, opakovanie strán, brožúra, zväčšenie, rámik,
/// odtiene sivej a okraj na dierovanie. Vlastná implementácia (WPF), žiadna knižnica ani program tretej strany.
/// </summary>
public static class RozvrhTlace
{
    private const double MmNaDip = 96.0 / 25.4;
    public static readonly Size A4NaVysku = new(210 * MmNaDip, 297 * MmNaDip);

    /// <summary>Stĺpce a riadky strán na liste a orientácia listu podľa orientácie strán zostavy.</summary>
    public static (int Stlpce, int Riadky, bool ListNaSirku) Mriezka(Rozmiestnenie r, bool stranyNaSirku) => r switch
    {
        Rozmiestnenie.Dve or Rozmiestnenie.Brozura => stranyNaSirku ? (1, 2, false) : (2, 1, true),
        Rozmiestnenie.Styri => (2, 2, stranyNaSirku),
        Rozmiestnenie.Osem => stranyNaSirku ? (2, 4, false) : (4, 2, true),
        _ => (1, 1, stranyNaSirku),
    };

    public static Size VelkostListu(bool naSirku) => naSirku ? new Size(A4NaVysku.Height, A4NaVysku.Width) : A4NaVysku;

    /// <summary>
    /// Ktoré strany zostavy (číslované od 1, null = prázdne miesto) budú na jednotlivých stranách listov – v poradí tlače.
    /// Brožúra: počet strán sa doplní na násobok 4 a strany sa usporiadajú tak, aby po preložení listov išli za sebou.
    /// </summary>
    public static List<int?[]> Rozdel(int pocetStran, VolbyTlace volby)
    {
        var listy = new List<int?[]>();
        if (pocetStran <= 0)
        {
            return listy;
        }

        if (volby.Rozmiestnenie == Rozmiestnenie.Brozura)
        {
            var spolu = (pocetStran + 3) / 4 * 4;
            int? Strana(int c) => c <= pocetStran ? c : null;
            for (var k = 0; k < spolu / 4; k++)
            {
                listy.Add(new[] { Strana(spolu - 2 * k), Strana(2 * k + 1) });
                listy.Add(new[] { Strana(2 * k + 2), Strana(spolu - 2 * k - 1) });
            }

            return listy;
        }

        var naList = (int)volby.Rozmiestnenie;
        if (volby.OpakovatStranky)
        {
            for (var s = 1; s <= pocetStran; s++)
            {
                listy.Add(Enumerable.Repeat<int?>(s, naList).ToArray());
            }

            return listy;
        }

        for (var s = 1; s <= pocetStran; s += naList)
        {
            listy.Add(Enumerable.Range(0, naList).Select(i => s + i <= pocetStran ? (int?)(s + i) : null).ToArray());
        }

        return listy;
    }

    /// <summary>
    /// Nakreslí jednu stranu listu. Zadná strana (obojstranná tlač, každá druhá) má okraj na dierovanie sprava,
    /// aby po otočení listu bol na tej istej hrane papiera.
    /// </summary>
    public static DrawingVisual List(IReadOnlyList<int?> bunky, Func<int, LogickaStrana> strana, VolbyTlace volby, bool stranyNaSirku, bool zadnaStrana)
    {
        var (stlpce, riadky, listNaSirku) = Mriezka(volby.Rozmiestnenie, stranyNaSirku);
        var list = VelkostListu(listNaSirku);
        var dierovac = volby.Dierovac ? volby.DierovacMm * MmNaDip : 0;
        var zrkadlit = zadnaStrana && volby.TlacitObojstranne;
        var plocha = new Rect(zrkadlit ? 0 : dierovac, 0, list.Width - dierovac, list.Height);

        var vizual = new DrawingVisual();
        using (var dc = vizual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(list));
            var sirkaBunky = plocha.Width / stlpce;
            var vyskaBunky = plocha.Height / riadky;
            var ramcek = new Pen(Brushes.Black, 0.6);
            ramcek.Freeze();
            for (var i = 0; i < bunky.Count && i < stlpce * riadky; i++)
            {
                var bunka = new Rect(plocha.X + i % stlpce * sirkaBunky, plocha.Y + i / stlpce * vyskaBunky, sirkaBunky, vyskaBunky);
                if (bunky[i] is int cislo)
                {
                    // zväčšenie oreže len prázdne okraje strany – obsah (text, tabuľky) sa nikdy neoreže
                    var s = strana(cislo);
                    var cela = Math.Min(bunka.Width / s.Velkost.Width, bunka.Height / s.Velkost.Height);
                    var obsah = s.Obsah.IsEmpty || s.Obsah.Width < 1 ? new Rect(s.Velkost) : s.Obsah;
                    var naObsah = Math.Min(bunka.Width / obsah.Width, bunka.Height / obsah.Height);
                    var mierka = Math.Max(cela, Math.Min(cela * volby.Zvacsenie, naObsah));
                    // stred: pri zväčšení stred obsahu, inak stred strany (plynulý prechod)
                    var podiel = naObsah > cela ? Math.Clamp((mierka - cela) / (naObsah - cela), 0, 1) : 0;
                    var stredX = s.Velkost.Width / 2 + (obsah.X + obsah.Width / 2 - s.Velkost.Width / 2) * podiel;
                    var stredY = s.Velkost.Height / 2 + (obsah.Y + obsah.Height / 2 - s.Velkost.Height / 2) * podiel;
                    dc.PushClip(new RectangleGeometry(bunka));
                    dc.PushTransform(new TranslateTransform(bunka.X + bunka.Width / 2 - stredX * mierka, bunka.Y + bunka.Height / 2 - stredY * mierka));
                    dc.PushTransform(new ScaleTransform(mierka, mierka));
                    dc.DrawDrawing(s.Kresba);
                    dc.Pop();
                    dc.Pop();
                    dc.Pop();
                }

                if (volby.Ramcek && (bunky[i] is not null || volby.Rozmiestnenie != Rozmiestnenie.Jedna))
                {
                    var okraj = 3 * MmNaDip;
                    dc.DrawRectangle(null, ramcek, new Rect(bunka.X + okraj, bunka.Y + okraj, Math.Max(1, bunka.Width - 2 * okraj), Math.Max(1, bunka.Height - 2 * okraj)));
                }
            }

            if (volby.Rozmiestnenie == Rozmiestnenie.Brozura && volby.Ramcek)
            {
                // čiara preloženia v strede listu
                var stred = new Pen(Brushes.Gray, 0.4) { DashStyle = DashStyles.Dash };
                dc.DrawLine(stred, new Point(list.Width / 2, 0), new Point(list.Width / 2, list.Height));
            }
        }

        return vizual;
    }

    /// <summary>Strana v odtieňoch sivej (rastrovaná – sivá tlač funguje na každej tlačiarni, aj keď ovládač farbu nevypne).</summary>
    public static LogickaStrana NaSivu(LogickaStrana strana, double dpi)
    {
        var mierka = dpi / 96.0;
        var sirka = Math.Max(1, (int)Math.Round(strana.Velkost.Width * mierka));
        var vyska = Math.Max(1, (int)Math.Round(strana.Velkost.Height * mierka));
        var vizual = new DrawingVisual();
        using (var dc = vizual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(mierka, mierka));
            dc.DrawRectangle(Brushes.White, null, new Rect(strana.Velkost));
            dc.DrawDrawing(strana.Kresba);
            dc.Pop();
        }

        var obraz = new RenderTargetBitmap(sirka, vyska, 96, 96, PixelFormats.Pbgra32);
        obraz.Render(vizual);
        var siva = new FormatConvertedBitmap(obraz, PixelFormats.Gray8, null, 0);
        siva.Freeze();
        var kresba = new ImageDrawing(siva, new Rect(strana.Velkost));
        kresba.Freeze();
        return new LogickaStrana(kresba, strana.Velkost, strana.Obsah);
    }
}
