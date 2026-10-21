using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Evita.Pomocne;
using MigraDoc.DocumentObjectModel;

namespace Evita.Services;

/// <summary>Strany zostavy ako obrázky: PNG (súbor pre každú stranu) alebo jeden viacstranový TIFF – 300 dpi, s voľbami rozvrhu.</summary>
public static class ObrazkyDokumentu
{
    public const double Dpi = 300;

    public static IReadOnlyList<SuborVystupu> Vytvor(Document dokument, string nazov, VolbyTlace volby, bool tiff)
    {
        volby = volby.Opravene() with { Obojstranne = false };
        var vykreslenie = NahladDokumentu.Priprav(dokument);
        var prva = vykreslenie.FormattedDocument!.GetPageInfo(1);
        double sirka = prva.Width;
        double vyska = prva.Height;
        var rozvrh = new NahladDokumentu.StrankovacRozvrhu(vykreslenie, sirka > vyska, volby) { NahladDpi = Dpi };

        // každá strana hneď do PNG (v pamäti len skomprimovaná – aj pri desiatkach strán)
        var strany = new List<byte[]>();
        for (var i = 0; i < rozvrh.PocetListov; i++)
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(Snimka(rozvrh.List(i), rozvrh.PageSize, volby.OdtieneSivej)));
            strany.Add(Zakoduj(png));
        }

        if (tiff)
        {
            var kodovac = new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw };
            var prudy = new List<MemoryStream>();
            try
            {
                foreach (var strana in strany)
                {
                    var prud = new MemoryStream(strana);
                    prudy.Add(prud);
                    kodovac.Frames.Add(BitmapDecoder.Create(prud, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames[0]);
                }

                return new[] { new SuborVystupu(nazov + ".tif", Zakoduj(kodovac), "image/tiff") };
            }
            finally
            {
                prudy.ForEach(p => p.Dispose());
            }
        }

        var cislice = strany.Count.ToString().Length;
        return strany.Select((data, i) => new SuborVystupu(
            (strany.Count == 1 ? nazov : $"{nazov}-{(i + 1).ToString().PadLeft(cislice, '0')}") + ".png", data, "image/png")).ToList();
    }

    private static BitmapSource Snimka(DrawingVisual list, Size velkost, bool siva)
    {
        var sirka = (int)Math.Round(velkost.Width * Dpi / 96);
        var vyska = (int)Math.Round(velkost.Height * Dpi / 96);
        var vizual = new DrawingVisual();
        using (var dc = vizual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(velkost));
            dc.DrawDrawing(list.Drawing);
        }

        var obraz = new RenderTargetBitmap(sirka, vyska, Dpi, Dpi, PixelFormats.Pbgra32);
        obraz.Render(vizual);
        var vysledok = new FormatConvertedBitmap(obraz, siva ? PixelFormats.Gray8 : PixelFormats.Bgr24, null, 0);
        vysledok.Freeze();
        return vysledok;
    }

    private static byte[] Zakoduj(BitmapEncoder kodovac)
    {
        using var prud = new MemoryStream();
        kodovac.Save(prud);
        return prud.ToArray();
    }
}
