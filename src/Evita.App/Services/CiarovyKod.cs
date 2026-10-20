using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Evita.Services;

/// <summary>Druh kódu na štítku.</summary>
public enum DruhKodu
{
    Qr = 0,
    Ciarovy = 1,
    Ziadny = 2,
}

/// <summary>
/// Vytvorenie QR a čiarových kódov (Code 128) a ich čítanie z obrázka (kamera, fotka z mobilu).
/// Používa jedinú knižnicu – zxing-cpp (Apache-2.0). Jej natívnu časť ZXing.dll zostavuje GitHub Actions
/// zo zdrojového kódu overenej verzie a pri každom zostavení kontroluje, že nepoužíva sieť, registre
/// ani spúšťanie iných programov. Program kódy z obrázkov len číta – nič z nich nespúšťa ani neotvára.
/// </summary>
public static class CiarovyKod
{
    /// <summary>Najdlhší text kódu, ktorý program spracuje (dlhší je určite nezmysel alebo pokus o zneužitie).</summary>
    public const int NajdlhsiKod = 512;

    /// <summary>Moduly QR kódu (true = čierny štvorček), bez okraja.</summary>
    public static bool[,] Qr(string text)
    {
        var (data, sirka, vyska) = Vytvor(ZXingCpp.BarcodeFormat.QRCode, text, "ecLevel=3");
        var moduly = new bool[sirka, vyska];
        for (var y = 0; y < vyska; y++)
        {
            for (var x = 0; x < sirka; x++)
            {
                moduly[x, y] = data[y * sirka + x] < 128;
            }
        }

        return moduly;
    }

    /// <summary>Pruhy čiarového kódu Code 128 (true = čierny modul), bez okrajov.</summary>
    public static bool[] Code128(string text)
    {
        // kódovač nechá nad a pod pruhmi biele riadky – vzor pruhov je v strednom riadku
        var (data, sirka, vyska) = Vytvor(ZXingCpp.BarcodeFormat.Code128, text, null);
        var riadok = vyska / 2 * sirka;
        var pruhy = new bool[sirka];
        for (var x = 0; x < sirka; x++)
        {
            pruhy[x] = data[riadok + x] < 128;
        }

        return pruhy;
    }

    /// <summary>Text, ktorý sa dá zapísať do Code 128 (len znaky ASCII bez riadiacich).</summary>
    public static bool DaSaZapisatDoCode128(string text) => text.Length > 0 && text.All(z => z is >= ' ' and <= '~');

    /// <summary>Kód ako obrázok v odtieňoch sivej, 1 bod na modul, bez okrajov a bez textu.</summary>
    /// <remarks>
    /// Volá priamo C rozhranie ZXing.dll, aby sa text odovzdal v UTF-8 (aj s diakritikou) – .NET obal ho posiela
    /// v kódovaní Windows. Pamäť vrátenú knižnicou vždy uvoľní tou istou knižnicou.
    /// </remarks>
    private static (byte[] Data, int Sirka, int Vyska) Vytvor(ZXingCpp.BarcodeFormat format, string text, string? moznosti)
    {
        IntPtr nastavenia = IntPtr.Zero, kod = IntPtr.Zero, zapis = IntPtr.Zero, obraz = IntPtr.Zero;
        try
        {
            nastavenia = Zapis.ZXing_CreatorOptions_new(format);
            if (nastavenia == IntPtr.Zero)
            {
                throw new EvitaException(Chyba());
            }

            if (moznosti is not null)
            {
                Zapis.ZXing_CreatorOptions_setOptions(nastavenia, Encoding.ASCII.GetBytes(moznosti + "\0"));
            }

            var utf8 = Encoding.UTF8.GetBytes(text);
            if (utf8.Length == 0)
            {
                throw new EvitaException(Preklad.T("Kód sa nepodarilo vytvoriť."));
            }

            kod = Zapis.ZXing_CreateBarcodeFromText(utf8, utf8.Length, nastavenia);
            if (kod == IntPtr.Zero)
            {
                throw new EvitaException(Chyba());
            }

            zapis = Zapis.ZXing_WriterOptions_new();
            Zapis.ZXing_WriterOptions_setScale(zapis, 1);
            Zapis.ZXing_WriterOptions_setAddQuietZones(zapis, false);
            Zapis.ZXing_WriterOptions_setAddHRT(zapis, false);
            obraz = Zapis.ZXing_WriteBarcodeToImage(kod, zapis);
            if (obraz == IntPtr.Zero)
            {
                throw new EvitaException(Chyba());
            }

            var sirka = Zapis.ZXing_Image_width(obraz);
            var vyska = Zapis.ZXing_Image_height(obraz);
            var data = new byte[sirka * vyska];
            Marshal.Copy(Zapis.ZXing_Image_data(obraz), data, 0, data.Length);
            return (data, sirka, vyska);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new EvitaException(Preklad.T("Knižnica na čiarové a QR kódy (ZXing.dll) chýba alebo je poškodená. Preinštalujte EviTA."), ex);
        }
        finally
        {
            if (obraz != IntPtr.Zero)
            {
                Zapis.ZXing_Image_delete(obraz);
            }

            if (zapis != IntPtr.Zero)
            {
                Zapis.ZXing_WriterOptions_delete(zapis);
            }

            if (kod != IntPtr.Zero)
            {
                Zapis.ZXing_Barcode_delete(kod);
            }

            if (nastavenia != IntPtr.Zero)
            {
                Zapis.ZXing_CreatorOptions_delete(nastavenia);
            }
        }

        static string Chyba()
        {
            var sprava = Zapis.ZXing_LastErrorMsg();
            if (sprava == IntPtr.Zero)
            {
                return Preklad.T("Kód sa nepodarilo vytvoriť.");
            }

            var text = Marshal.PtrToStringUTF8(sprava) ?? "";
            Zapis.ZXing_free(sprava);
            return Preklad.F($"Kód sa nepodarilo vytvoriť: {text}");
        }
    }

    /// <summary>Funkcie C rozhrania zxing-cpp na vytvorenie kódu (text v UTF-8).</summary>
    private static class Zapis
    {
        private const string Kniznica = "ZXing";

        [DllImport(Kniznica)] public static extern IntPtr ZXing_CreatorOptions_new(int format);
        [DllImport(Kniznica)] public static extern void ZXing_CreatorOptions_delete(IntPtr nastavenia);
        [DllImport(Kniznica)] public static extern void ZXing_CreatorOptions_setOptions(IntPtr nastavenia, byte[] moznosti);
        [DllImport(Kniznica)] public static extern IntPtr ZXing_CreateBarcodeFromText(byte[] data, int dlzka, IntPtr nastavenia);
        [DllImport(Kniznica)] public static extern void ZXing_Barcode_delete(IntPtr kod);
        [DllImport(Kniznica)] public static extern IntPtr ZXing_WriterOptions_new();
        [DllImport(Kniznica)] public static extern void ZXing_WriterOptions_delete(IntPtr zapis);
        [DllImport(Kniznica)] public static extern void ZXing_WriterOptions_setScale(IntPtr zapis, int mierka);
        [DllImport(Kniznica)] public static extern void ZXing_WriterOptions_setAddHRT(IntPtr zapis, [MarshalAs(UnmanagedType.I1)] bool text);
        [DllImport(Kniznica)] public static extern void ZXing_WriterOptions_setAddQuietZones(IntPtr zapis, [MarshalAs(UnmanagedType.I1)] bool okraje);
        [DllImport(Kniznica)] public static extern IntPtr ZXing_WriteBarcodeToImage(IntPtr kod, IntPtr zapis);
        [DllImport(Kniznica)] public static extern IntPtr ZXing_Image_data(IntPtr obraz);
        [DllImport(Kniznica)] public static extern int ZXing_Image_width(IntPtr obraz);
        [DllImport(Kniznica)] public static extern int ZXing_Image_height(IntPtr obraz);
        [DllImport(Kniznica)] public static extern void ZXing_Image_delete(IntPtr obraz);
        [DllImport(Kniznica)] public static extern IntPtr ZXing_LastErrorMsg();
        [DllImport(Kniznica)] public static extern void ZXing_free(IntPtr pamat);
    }

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

    // ------------------------------------------------------------------ čítanie

    /// <summary>Všetky kódy v obrázku v pamäti (BGRA, 4 bajty na bod) – na fotke ich býva viac (EAN, sériové číslo, QR).</summary>
    public static List<string> PrecitajVsetky(byte[] bgra, int sirka, int vyska, bool dokladne = true)
    {
        var vysledok = new List<string>();
        var pamat = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            var obraz = new ZXingCpp.ImageView(pamat.AddrOfPinnedObject(), sirka, vyska, ZXingCpp.ImageFormat.BGRA);
            using var citac = new ZXingCpp.BarcodeReader
            {
                TryHarder = dokladne,
                TryRotate = true,
                TryInvert = dokladne,
                TryDownscale = true,
                MaxNumberOfSymbols = 10,
            };
            foreach (var kod in citac.From(obraz))
            {
                using (kod)
                {
                    var text = kod.IsValid ? KodyEvita.Ocisti(kod.Text) : "";
                    if (text.Length > 0 && text.Length <= NajdlhsiKod && !vysledok.Contains(text))
                    {
                        vysledok.Add(text);
                    }
                }
            }

            GC.KeepAlive(obraz);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new EvitaException(Preklad.T("Knižnica na čiarové a QR kódy (ZXing.dll) chýba alebo je poškodená. Preinštalujte EviTA."), ex);
        }
        finally
        {
            pamat.Free();
        }

        return vysledok;
    }

    /// <summary>Prvý kód v obrázku (BGRA), alebo null.</summary>
    public static string? Precitaj(byte[] bgra, int sirka, int vyska, bool dokladne = true) =>
        PrecitajVsetky(bgra, sirka, vyska, dokladne).FirstOrDefault();

    /// <summary>Všetky kódy z obrázka v súbore (JPEG, PNG … – fotka z mobilu). Číta sa vo vysokom rozlíšení.</summary>
    public static List<string> PrecitajVsetkyZObrazka(byte[] subor)
    {
        using var prud = new System.IO.MemoryStream(subor);
        var dekoder = BitmapDecoder.Create(prud, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);
        BitmapSource snimka = dekoder.Frames[0];

        // orientácia z EXIF (mobil ukladá fotku otočenú a informáciu o otočení zapíše zvlášť)
        var otocenie = OtocenieZExif(dekoder.Frames[0]);
        if (otocenie != 0)
        {
            snimka = new TransformedBitmap(snimka, new RotateTransform(otocenie));
        }

        var vysledok = new List<string>();
        foreach (var najvacsia in new[] { 3200, 1600 })
        {
            var zdroj = snimka;
            var vacsia = Math.Max(zdroj.PixelWidth, zdroj.PixelHeight);
            if (vacsia > najvacsia)
            {
                var mierka = (double)najvacsia / vacsia;
                zdroj = new TransformedBitmap(zdroj, new ScaleTransform(mierka, mierka));
            }

            var bgra = new FormatConvertedBitmap(zdroj, PixelFormats.Bgra32, null, 0);
            var data = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
            bgra.CopyPixels(data, bgra.PixelWidth * 4, 0);
            foreach (var kod in PrecitajVsetky(data, bgra.PixelWidth, bgra.PixelHeight))
            {
                if (!vysledok.Contains(kod))
                {
                    vysledok.Add(kod);
                }
            }

            if (vysledok.Count > 0 || vacsia <= najvacsia)
            {
                break;
            }
        }

        return vysledok;
    }

    /// <summary>Prvý kód z obrázka v súbore, alebo null.</summary>
    public static string? PrecitajZObrazka(byte[] subor) => PrecitajVsetkyZObrazka(subor).FirstOrDefault();

    private static double OtocenieZExif(BitmapFrame snimka)
    {
        try
        {
            if (snimka.Metadata is BitmapMetadata metadata
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
