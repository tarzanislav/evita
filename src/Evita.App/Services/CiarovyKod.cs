using System.Windows;
using System.Windows.Media;
using ZXing;
using ZXing.Common;
using ZXing.OneD;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace Evita.Services;

/// <summary>Druh kódu na štítku.</summary>
public enum DruhKodu
{
    Qr = 0,
    Ciarovy = 1,
    Ziadny = 2,
}

/// <summary>Vytvorenie QR a čiarových kódov (Code 128) a ich čítanie z obrázka (kamera, fotka z mobilu).</summary>
public static class CiarovyKod
{
    /// <summary>Moduly QR kódu (true = čierny štvorček), bez okraja.</summary>
    public static bool[,] Qr(string text)
    {
        var napovedy = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 0,
            [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
            [EncodeHintType.CHARACTER_SET] = "UTF-8",
        };
        var matica = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0, napovedy);
        var moduly = new bool[matica.Width, matica.Height];
        for (var x = 0; x < matica.Width; x++)
        {
            for (var y = 0; y < matica.Height; y++)
            {
                moduly[x, y] = matica[x, y];
            }
        }

        return moduly;
    }

    /// <summary>Pruhy čiarového kódu Code 128 (true = čierny modul), bez okrajov.</summary>
    public static bool[] Code128(string text) => new Code128Writer().encode(text);

    /// <summary>Text, ktorý sa dá zapísať do Code 128 (len znaky ASCII bez riadiacich).</summary>
    public static bool DaSaZapisatDoCode128(string text) => text.Length > 0 && text.All(z => z is >= ' ' and <= '~');

    /// <summary>QR kód ako obrázok pre okno (napr. adresa pre mobil). Rozmer je v jednotkách WPF vrátane okraja.</summary>
    public static ImageSource ObrazokQr(string text, double velkost = 220)
    {
        var moduly = Qr(text);
        var pocet = moduly.GetLength(0);
        const int okraj = 2;
        var celkom = pocet + 2 * okraj;
        var geometria = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var y = 0; y < pocet; y++)
        {
            for (var x = 0; x < pocet; x++)
            {
                if (moduly[x, y])
                {
                    geometria.Children.Add(new RectangleGeometry(new Rect(x + okraj, y + okraj, 1, 1)));
                }
            }
        }

        var kresba = new DrawingGroup();
        kresba.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, celkom, celkom))));
        kresba.Children.Add(new GeometryDrawing(Brushes.Black, null, geometria));
        kresba.Transform = new ScaleTransform(velkost / celkom, velkost / celkom);
        var obrazok = new DrawingImage(kresba);
        obrazok.Freeze();
        return obrazok;
    }

    private static readonly BarcodeFormat[] CitaneFormaty =
    {
        BarcodeFormat.QR_CODE, BarcodeFormat.CODE_128, BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A,
        BarcodeFormat.UPC_E, BarcodeFormat.CODE_39, BarcodeFormat.DATA_MATRIX, BarcodeFormat.ITF,
    };

    /// <summary>Prečíta kód z obrázka v pamäti (BGRA, 4 bajty na bod). Vráti text alebo null.</summary>
    public static string? Precitaj(byte[] bgra, int sirka, int vyska, bool dokladne = true)
    {
        var citac = new BarcodeReaderGeneric
        {
            AutoRotate = dokladne,
            Options = new DecodingOptions
            {
                TryHarder = dokladne,
                TryInverted = dokladne,
                PossibleFormats = CitaneFormaty,
            },
        };
        var vysledok = citac.Decode(bgra, sirka, vyska, RGBLuminanceSource.BitmapFormat.BGRA32);
        return string.IsNullOrWhiteSpace(vysledok?.Text) ? null : vysledok.Text.Trim();
    }

    /// <summary>Prečíta kód z obrázka v súbore (JPEG, PNG … – fotka z mobilu). Veľké fotky sa zmenšia.</summary>
    public static string? PrecitajZObrazka(byte[] subor)
    {
        using var prud = new System.IO.MemoryStream(subor);
        var dekoder = System.Windows.Media.Imaging.BitmapDecoder.Create(prud,
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat | System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        System.Windows.Media.Imaging.BitmapSource snimka = dekoder.Frames[0];

        // orientácia z EXIF (mobil ukladá fotku otočenú a informáciu o otočení zapíše zvlášť)
        var otocenie = OtocenieZExif(dekoder.Frames[0]);
        if (otocenie != 0)
        {
            snimka = new System.Windows.Media.Imaging.TransformedBitmap(snimka, new RotateTransform(otocenie));
        }

        foreach (var najvacsia in new[] { 1600, 2400, 1000 })
        {
            var zdroj = snimka;
            var vacsia = Math.Max(zdroj.PixelWidth, zdroj.PixelHeight);
            if (vacsia > najvacsia)
            {
                var mierka = (double)najvacsia / vacsia;
                zdroj = new System.Windows.Media.Imaging.TransformedBitmap(zdroj, new ScaleTransform(mierka, mierka));
            }

            var bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(zdroj, PixelFormats.Bgra32, null, 0);
            var sirka = bgra.PixelWidth;
            var vyska = bgra.PixelHeight;
            var data = new byte[sirka * vyska * 4];
            bgra.CopyPixels(data, sirka * 4, 0);
            if (Precitaj(data, sirka, vyska) is { } text)
            {
                return text;
            }

            if (vacsia <= najvacsia)
            {
                break;
            }
        }

        return null;
    }

    private static double OtocenieZExif(System.Windows.Media.Imaging.BitmapFrame snimka)
    {
        try
        {
            if (snimka.Metadata is System.Windows.Media.Imaging.BitmapMetadata metadata
                && metadata.ContainsQuery("System.Photo.Orientation")
                && metadata.GetQuery("System.Photo.Orientation") is ushort orientacia)
            {
                return orientacia switch
                {
                    3 => 180,
                    6 => 90,
                    8 => 270,
                    _ => 0,
                };
            }
        }
        catch (Exception)
        {
            // obrázok bez metadát
        }

        return 0;
    }
}
