using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Evita.Data;
using Evita.Models;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Evita.Services;

/// <summary>
/// Formát štítkov: hárok samolepiacich etikiet A4 (stĺpce × riadky) alebo jeden štítok na stranu pre tlačiareň štítkov.
/// Rozmery sú v milimetroch, krok = šírka (výška) štítku + medzera.
/// </summary>
public sealed record SablonaStitkov(
    string Kluc, string NazovSk, bool Harok, double Sirka, double Vyska, int Stlpce, int Riadky,
    double OkrajHore, double OkrajVlavo, double KrokVodorovne, double KrokZvisle)
{
    public string Nazov => Preklad.T(NazovSk);
    public int NaStranu => Harok ? Stlpce * Riadky : 1;
    public double SirkaStrany => Harok ? 210 : Sirka;
    public double VyskaStrany => Harok ? 297 : Vyska;

    public string Rozmer => Harok
        ? Preklad.F($"{Sk.Cislo(Sirka)} × {Sk.Cislo(Vyska)} mm, {Stlpce} × {Riadky} na hárku A4")
        : Preklad.F($"{Sk.Cislo(Sirka)} × {Sk.Cislo(Vyska)} mm, jeden štítok na stranu");
}

public enum ObsahKodu
{
    KodEvita = 0,
    Inventarne = 1,
    Seriove = 2,
}

/// <summary>Nastavenia tlače štítkov (ukladajú sa a pri ďalšej tlači sa použijú znova).</summary>
public sealed class MoznostiStitkov
{
    public string Sablona { get; set; } = "a4-3x8";
    public DruhKodu Kod { get; set; } = DruhKodu.Qr;
    public ObsahKodu ObsahKodu { get; set; } = ObsahKodu.KodEvita;
    public bool Organizacia { get; set; } = true;
    public bool Nazov { get; set; } = true;
    public bool Cislo { get; set; } = true;
    public bool Inventarne { get; set; } = true;
    public bool Seriove { get; set; }
    public bool Miestnost { get; set; }
    public bool Ramcek { get; set; }
    public int Kopie { get; set; } = 1;
    public int ZacatOd { get; set; } = 1;
    public double PosunX { get; set; }
    public double PosunY { get; set; }

    // vlastný formát
    public bool VlastnyHarok { get; set; } = true;
    public double VlastnaSirka { get; set; } = 70;
    public double VlastnaVyska { get; set; } = 37;
    public int VlastneStlpce { get; set; } = 3;
    public int VlastneRiadky { get; set; } = 8;
    public double VlastnyOkrajHore { get; set; }
    public double VlastnyOkrajVlavo { get; set; }
    public double VlastnaMedzeraVodorovne { get; set; }
    public double VlastnaMedzeraZvisle { get; set; }

    public MoznostiStitkov Kopia() => (MoznostiStitkov)MemberwiseClone();
}

/// <summary>Údaje jednej položky na štítok.</summary>
public sealed record UdajeStitku(int Id, int PrirastkoveCislo, string Nazov, string? Inventarne, string? Seriove, string? Miestnost);

/// <summary>Pripravené štítky: formát a umiestnenie každého štítku na stranách.</summary>
public sealed class RozlozenieStitkov
{
    public required SablonaStitkov Sablona { get; init; }
    public required MoznostiStitkov Moznosti { get; init; }
    public required string? Organizacia { get; init; }
    public required List<(int Strana, double X, double Y, UdajeStitku Udaje)> Stitky { get; init; }
    public int PocetStran { get; init; }
    public int PocetStitkov => Stitky.Count;
}

/// <summary>Tlač štítkov s QR alebo čiarovým kódom na zariadenia (do PDF, na tlačiareň aj ako náhľad).</summary>
public sealed class StitkyService
{
    public const string KlucMoznosti = "Stitky.Moznosti";
    public const string KlucVlastna = "vlastna";
    private const double BodovNaMm = 72.0 / 25.4;
    private const double DipNaBod = 96.0 / 72.0;

    public static IReadOnlyList<SablonaStitkov> Sablony { get; } = new SablonaStitkov[]
    {
        new("a4-3x8", Preklad.N("Hárok A4 – 24 štítkov 70 × 37 mm"), true, 70, 37, 3, 8, 0.5, 0, 70, 37),
        new("a4-3x7", Preklad.N("Hárok A4 – 21 štítkov 70 × 42,3 mm"), true, 70, 42.3, 3, 7, 0.4, 0, 70, 42.3),
        new("a4-4x10", Preklad.N("Hárok A4 – 40 štítkov 48,5 × 25,4 mm"), true, 48.5, 25.4, 4, 10, 21.5, 8, 48.5, 25.4),
        new("a4-5x13", Preklad.N("Hárok A4 – 65 štítkov 38,1 × 21,2 mm (L7651)"), true, 38.1, 21.2, 5, 13, 10.7, 4.75, 40.6, 21.2),
        new("a4-3x7-63", Preklad.N("Hárok A4 – 21 štítkov 63,5 × 38,1 mm (L7160)"), true, 63.5, 38.1, 3, 7, 15.1, 7.2, 66.0, 38.1),
        new("a4-2x7", Preklad.N("Hárok A4 – 14 štítkov 99,1 × 38,1 mm (L7163)"), true, 99.1, 38.1, 2, 7, 15.1, 4.65, 101.6, 38.1),
        new("rola-62x29", Preklad.N("Tlačiareň štítkov – 62 × 29 mm"), false, 62, 29, 1, 1, 0, 0, 62, 29),
        new("rola-50x30", Preklad.N("Tlačiareň štítkov – 50 × 30 mm"), false, 50, 30, 1, 1, 0, 0, 50, 30),
        new("rola-50x25", Preklad.N("Tlačiareň štítkov – 50 × 25 mm"), false, 50, 25, 1, 1, 0, 0, 50, 25),
        new("rola-38x25", Preklad.N("Tlačiareň štítkov – 38 × 25 mm"), false, 38, 25, 1, 1, 0, 0, 38, 25),
        new(KlucVlastna, Preklad.N("Vlastný formát"), true, 70, 37, 3, 8, 0, 0, 70, 37),
    };

    /// <summary>Šablóna podľa nastavení (vlastný formát z hodnôt zadaných používateľom).</summary>
    public static SablonaStitkov Sablona(MoznostiStitkov m)
    {
        if (m.Sablona != KlucVlastna)
        {
            return Sablony.FirstOrDefault(s => s.Kluc == m.Sablona) ?? Sablony[0];
        }

        var sirka = Math.Clamp(m.VlastnaSirka, 10, 200);
        var vyska = Math.Clamp(m.VlastnaVyska, 8, 287);
        return m.VlastnyHarok
            ? new SablonaStitkov(KlucVlastna, Preklad.N("Vlastný formát"), true, sirka, vyska,
                Math.Clamp(m.VlastneStlpce, 1, 10), Math.Clamp(m.VlastneRiadky, 1, 30),
                Math.Max(0, m.VlastnyOkrajHore), Math.Max(0, m.VlastnyOkrajVlavo),
                sirka + Math.Max(0, m.VlastnaMedzeraVodorovne), vyska + Math.Max(0, m.VlastnaMedzeraZvisle))
            : new SablonaStitkov(KlucVlastna, Preklad.N("Vlastný formát"), false, sirka, vyska, 1, 1, 0, 0, sirka, vyska);
    }

    public MoznostiStitkov NacitajMoznosti()
    {
        try
        {
            using var db = Databaza.Otvor();
            var json = Databaza.Citaj(db, KlucMoznosti);
            return string.IsNullOrEmpty(json) ? new MoznostiStitkov() : JsonSerializer.Deserialize<MoznostiStitkov>(json) ?? new MoznostiStitkov();
        }
        catch (Exception)
        {
            return new MoznostiStitkov();
        }
    }

    public void UlozMoznosti(MoznostiStitkov moznosti)
    {
        var ulozit = moznosti.Kopia();
        ulozit.ZacatOd = 1;
        ulozit.Kopie = 1;
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucMoznosti, JsonSerializer.Serialize(ulozit));
        db.SaveChanges();
    }

    public List<UdajeStitku> NacitajUdaje(IEnumerable<int> idPoloziek)
    {
        var idcka = idPoloziek.Distinct().ToList();
        using var db = Databaza.Otvor();
        return db.Polozky.AsNoTracking()
            .Where(p => idcka.Contains(p.Id))
            .OrderBy(p => p.PrirastkoveCislo)
            .Select(p => new UdajeStitku(p.Id, p.PrirastkoveCislo, p.Nazov, p.InventarneCislo, p.SerioveCislo,
                p.Miestnost != null ? p.Miestnost.Nazov : null))
            .ToList();
    }

    /// <summary>Rozmiestni štítky na strany (s kópiami a začiatkom na čiastočne použitom hárku).</summary>
    public static RozlozenieStitkov Priprav(MoznostiStitkov m, IReadOnlyList<UdajeStitku> udaje, string? organizacia)
    {
        var sablona = Sablona(m);
        var naStranu = sablona.NaStranu;
        var kopie = Math.Clamp(m.Kopie, 1, 20);
        var pozicia = sablona.Harok ? Math.Clamp(m.ZacatOd, 1, naStranu) - 1 : 0;
        var stitky = new List<(int, double, double, UdajeStitku)>();
        foreach (var u in udaje)
        {
            for (var k = 0; k < kopie; k++)
            {
                var strana = pozicia / naStranu;
                var miesto = pozicia % naStranu;
                var stlpec = miesto % sablona.Stlpce;
                var riadok = miesto / sablona.Stlpce;
                var x = sablona.OkrajVlavo + stlpec * sablona.KrokVodorovne + m.PosunX;
                var y = sablona.OkrajHore + riadok * sablona.KrokZvisle + m.PosunY;
                stitky.Add((strana, x, y, u));
                pozicia++;
            }
        }

        var pocetStran = stitky.Count == 0 ? 1 : stitky[^1].Item1 + 1;
        return new RozlozenieStitkov
        {
            Sablona = sablona,
            Moznosti = m,
            Organizacia = string.IsNullOrWhiteSpace(organizacia) ? null : organizacia.Trim(),
            Stitky = stitky,
            PocetStran = pocetStran,
        };
    }

    /// <summary>Text, ktorý sa zakóduje do QR alebo čiarového kódu.</summary>
    public static string TextKodu(UdajeStitku u, MoznostiStitkov m)
    {
        var text = m.ObsahKodu switch
        {
            ObsahKodu.Inventarne => u.Inventarne,
            ObsahKodu.Seriove => u.Seriove,
            _ => null,
        };
        text = string.IsNullOrWhiteSpace(text) ? KodyEvita.KodPolozky(u.PrirastkoveCislo) : text.Trim();
        if (m.Kod == DruhKodu.Ciarovy && !CiarovyKod.DaSaZapisatDoCode128(text))
        {
            text = KodyEvita.KodPolozky(u.PrirastkoveCislo);
        }

        return text;
    }

    // ------------------------------------------------------------------ kreslenie

    /// <summary>Nakreslí jednu stranu (číslovanú od 0). Súradnice XGraphics sú v bodoch (1/72 palca).</summary>
    public static void KresliStranu(XGraphics g, RozlozenieStitkov r, int strana)
    {
        var s = r.Sablona;
        foreach (var (_, x, y, udaje) in r.Stitky.Where(t => t.Strana == strana))
        {
            var oblast = new XRect(x * BodovNaMm, y * BodovNaMm, s.Sirka * BodovNaMm, s.Vyska * BodovNaMm);
            KresliStitok(g, oblast, udaje, r.Moznosti, r.Organizacia);
        }
    }

    private sealed record RiadokTextu(string Text, double Vaha, bool Tucne, bool Sedy);

    private static void KresliStitok(XGraphics g, XRect oblast, UdajeStitku u, MoznostiStitkov m, string? organizacia)
    {
        if (m.Ramcek)
        {
            g.DrawRectangle(new XPen(XColor.FromArgb(170, 170, 170), 0.4), oblast);
        }

        var okraj = Math.Clamp(Math.Min(oblast.Width, oblast.Height) * 0.07, 1.2 * BodovNaMm, 3 * BodovNaMm);
        var vnutro = new XRect(oblast.X + okraj, oblast.Y + okraj, oblast.Width - 2 * okraj, oblast.Height - 2 * okraj);
        if (vnutro.Width <= 0 || vnutro.Height <= 0)
        {
            return;
        }

        var text = vnutro;
        var kod = TextKodu(u, m);
        switch (m.Kod)
        {
            case DruhKodu.Qr:
            {
                var strana = Math.Min(vnutro.Height, vnutro.Width * 0.45);
                var qrOblast = new XRect(vnutro.X, vnutro.Y + (vnutro.Height - strana) / 2, strana, strana);
                KresliQr(g, qrOblast, kod);
                var medzera = Math.Min(2.5 * BodovNaMm, vnutro.Width * 0.04);
                text = new XRect(qrOblast.Right + medzera, vnutro.Y, Math.Max(1, vnutro.Right - qrOblast.Right - medzera), vnutro.Height);
                break;
            }

            case DruhKodu.Ciarovy:
            {
                var vyskaPisma = Math.Clamp(vnutro.Height * 0.09, 4.5, 7);
                var vyskaKodu = Math.Clamp(vnutro.Height * 0.42, 5 * BodovNaMm, 16 * BodovNaMm);
                var kodOblast = new XRect(vnutro.X, vnutro.Bottom - vyskaKodu - vyskaPisma * 1.15, vnutro.Width, vyskaKodu);
                KresliCode128(g, kodOblast, kod);
                var pismo = Pismo(vyskaPisma, false);
                g.DrawString(Skrat(g, kod, pismo, vnutro.Width), pismo, XBrushes.Black,
                    new XRect(vnutro.X, kodOblast.Bottom, vnutro.Width, vyskaPisma * 1.15), XStringFormats.Center);
                text = new XRect(vnutro.X, vnutro.Y, vnutro.Width, Math.Max(1, kodOblast.Y - vnutro.Y - 1));
                break;
            }
        }

        var riadky = new List<RiadokTextu>();
        if (m.Organizacia && !string.IsNullOrWhiteSpace(organizacia))
        {
            riadky.Add(new RiadokTextu(organizacia, 0.75, false, true));
        }

        if (m.Nazov)
        {
            riadky.Add(new RiadokTextu(u.Nazov, 1.0, true, false));
        }

        if (m.Cislo)
        {
            riadky.Add(new RiadokTextu(Preklad.F($"č. {u.PrirastkoveCislo}"), 1.2, true, false));
        }

        if (m.Inventarne && !string.IsNullOrWhiteSpace(u.Inventarne))
        {
            riadky.Add(new RiadokTextu(Preklad.F($"inv. č. {u.Inventarne}"), 0.85, false, false));
        }

        if (m.Seriove && !string.IsNullOrWhiteSpace(u.Seriove))
        {
            riadky.Add(new RiadokTextu(Preklad.F($"SN {u.Seriove}"), 0.85, false, false));
        }

        if (m.Miestnost && !string.IsNullOrWhiteSpace(u.Miestnost))
        {
            riadky.Add(new RiadokTextu(u.Miestnost, 0.85, false, false));
        }

        KresliRiadky(g, text, riadky);
    }

    private static void KresliRiadky(XGraphics g, XRect oblast, List<RiadokTextu> riadky)
    {
        if (riadky.Count == 0 || oblast.Height < 3)
        {
            return;
        }

        const double Riadkovanie = 1.18;
        var sucet = riadky.Sum(r => r.Vaha) * Riadkovanie;
        var zaklad = Math.Clamp(oblast.Height / sucet, 3.5, 11);

        // ak je miesta dosť, názov sa môže zalomiť do dvoch riadkov
        var nazovNaDva = oblast.Height / sucet > 11 * 1.25;
        var vyska = riadky.Sum(r => r.Vaha * zaklad * Riadkovanie);
        var y = oblast.Y + Math.Max(0, (oblast.Height - vyska - (nazovNaDva ? zaklad * Riadkovanie : 0)) / 2);
        foreach (var riadok in riadky)
        {
            var velkost = riadok.Vaha * zaklad;
            var pismo = Pismo(velkost, riadok.Tucne);

            // príliš dlhý text sa zmenší (najviac na 75 %), potom skráti
            var sirka = g.MeasureString(riadok.Text, pismo).Width;
            if (sirka > oblast.Width && !(nazovNaDva && riadok.Tucne && riadok.Vaha == 1.0))
            {
                velkost = Math.Max(velkost * 0.75, velkost * oblast.Width / sirka);
                pismo = Pismo(velkost, riadok.Tucne);
            }

            var stetec = riadok.Sedy ? new XSolidBrush(XColor.FromArgb(90, 90, 90)) : XBrushes.Black;
            var riadkyTextu = nazovNaDva && riadok.Tucne && riadok.Vaha == 1.0
                ? Zalom(g, riadok.Text, pismo, oblast.Width, 2)
                : new List<string> { Skrat(g, riadok.Text, pismo, oblast.Width) };
            foreach (var t in riadkyTextu)
            {
                g.DrawString(t, pismo, stetec, new XRect(oblast.X, y, oblast.Width, velkost * Riadkovanie), XStringFormats.CenterLeft);
                y += velkost * Riadkovanie;
            }
        }
    }

    private static List<string> Zalom(XGraphics g, string text, XFont pismo, double sirka, int najviac)
    {
        var slova = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var vysledok = new List<string>();
        var aktualny = "";
        var i = 0;
        for (; i < slova.Length; i++)
        {
            var skusit = aktualny.Length == 0 ? slova[i] : aktualny + " " + slova[i];
            if (g.MeasureString(skusit, pismo).Width <= sirka || aktualny.Length == 0)
            {
                aktualny = skusit;
                continue;
            }

            vysledok.Add(aktualny);
            aktualny = slova[i];
            if (vysledok.Count == najviac - 1)
            {
                aktualny = string.Join(' ', slova.Skip(i));
                i = slova.Length;
                break;
            }
        }

        if (aktualny.Length > 0)
        {
            vysledok.Add(aktualny);
        }

        return vysledok.Select(r => Skrat(g, r, pismo, sirka)).ToList();
    }

    private static string Skrat(XGraphics g, string text, XFont pismo, double sirka)
    {
        if (g.MeasureString(text, pismo).Width <= sirka)
        {
            return text;
        }

        for (var dlzka = text.Length - 1; dlzka > 1; dlzka--)
        {
            var kratsi = text[..dlzka].TrimEnd() + "…";
            if (g.MeasureString(kratsi, pismo).Width <= sirka)
            {
                return kratsi;
            }
        }

        return text[..1];
    }

    private static XFont Pismo(double velkost, bool tucne) =>
        new(PdfZostava.Pismo, Math.Max(3, velkost), tucne ? XFontStyleEx.Bold : XFontStyleEx.Regular);

    private static void KresliQr(XGraphics g, XRect oblast, string text)
    {
        var moduly = CiarovyKod.Qr(text);
        var pocet = moduly.GetLength(0);
        var modul = oblast.Width / (pocet + 2);
        var cesta = new XGraphicsPath { FillMode = XFillMode.Winding };
        for (var y = 0; y < pocet; y++)
        {
            var x = 0;
            while (x < pocet)
            {
                if (!moduly[x, y])
                {
                    x++;
                    continue;
                }

                var zaciatok = x;
                while (x < pocet && moduly[x, y])
                {
                    x++;
                }

                cesta.AddRectangle(oblast.X + (zaciatok + 1) * modul, oblast.Y + (y + 1) * modul, (x - zaciatok) * modul, modul);
            }
        }

        g.DrawPath(XBrushes.Black, cesta);
    }

    private static void KresliCode128(XGraphics g, XRect oblast, string text)
    {
        var pruhy = CiarovyKod.Code128(text);
        const int tichaZona = 10;
        var modul = Math.Min(oblast.Width / (pruhy.Length + 2 * tichaZona), 0.5 * BodovNaMm);
        var sirka = modul * pruhy.Length;
        var x0 = oblast.X + (oblast.Width - sirka) / 2;
        var cesta = new XGraphicsPath { FillMode = XFillMode.Winding };
        var i = 0;
        while (i < pruhy.Length)
        {
            if (!pruhy[i])
            {
                i++;
                continue;
            }

            var zaciatok = i;
            while (i < pruhy.Length && pruhy[i])
            {
                i++;
            }

            cesta.AddRectangle(x0 + zaciatok * modul, oblast.Y, (i - zaciatok) * modul, oblast.Height);
        }

        g.DrawPath(XBrushes.Black, cesta);
    }

    // ------------------------------------------------------------------ výstupy

    public static void UlozPdf(RozlozenieStitkov r, string cesta)
    {
        using var dokument = new PdfDocument();
        dokument.Info.Title = Preklad.T("Štítky EviTA");
        dokument.Info.Creator = "EviTA";
        for (var i = 0; i < r.PocetStran; i++)
        {
            var strana = dokument.AddPage();
            strana.Width = XUnit.FromMillimeter(r.Sablona.SirkaStrany);
            strana.Height = XUnit.FromMillimeter(r.Sablona.VyskaStrany);
            using var g = XGraphics.FromPdfPage(strana);
            KresliStranu(g, r, i);
        }

        dokument.Save(cesta);
    }

    /// <summary>Strana ako vizuál WPF (náhľad, tlač) vo veľkosti strany v jednotkách WPF.</summary>
    public static DrawingVisual Vizual(RozlozenieStitkov r, int strana, out Size velkost)
    {
        var sirka = r.Sablona.SirkaStrany * BodovNaMm;
        var vyska = r.Sablona.VyskaStrany * BodovNaMm;
        velkost = new Size(sirka * DipNaBod, vyska * DipNaBod);
        var kresba = new DrawingVisual();
        using (var kontext = kresba.RenderOpen())
        {
            kontext.DrawRectangle(Brushes.White, null, new Rect(velkost));
            using var g = XGraphics.FromDrawingContext(kontext, new XSize(sirka, vyska), XGraphicsUnit.Point, new PdfSharp.Events.RenderEvents());
            KresliStranu(g, r, strana);
        }

        return kresba;
    }

    /// <summary>Strany štítkov pre tlač Windows.</summary>
    public sealed class Strankovac : DocumentPaginator
    {
        private readonly RozlozenieStitkov _rozlozenie;

        public Strankovac(RozlozenieStitkov rozlozenie)
        {
            _rozlozenie = rozlozenie;
            PageSize = new Size(rozlozenie.Sablona.SirkaStrany / 25.4 * 96, rozlozenie.Sablona.VyskaStrany / 25.4 * 96);
        }

        public override bool IsPageCountValid => true;

        public override int PageCount => _rozlozenie.PocetStran;

        public override Size PageSize { get; set; }

        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            var kresba = Vizual(_rozlozenie, pageNumber, out var velkost);
            return new DocumentPage(kresba, velkost, new Rect(velkost), new Rect(velkost));
        }
    }
}
