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

    /// <summary>Najväčší rozmer fotky, v ktorom sa kód hľadá (väčšia fotka sa zmenší – čítanie je rýchlejšie a stačí to).</summary>
    public const int NajvacsiRozmerFotky = 4096;

    /// <summary>Všetky kódy v obrázku v pamäti (BGRA, 4 bajty na bod) – na fotke ich býva viac (EAN, sériové číslo, QR).</summary>
    public static List<string> PrecitajVsetky(byte[] bgra, int sirka, int vyska, bool dokladne = true) =>
        Citaj(bgra, sirka, vyska, ZXingCpp.ImageFormat.BGRA, dokladne, ZXingCpp.Binarizer.LocalAverage, 0)
            .OrderBy(k => k.Slaby).Select(k => k.Text).ToList();

    /// <summary>Prvý kód v obrázku (BGRA), alebo null.</summary>
    public static string? Precitaj(byte[] bgra, int sirka, int vyska, bool dokladne = true) =>
        PrecitajVsetky(bgra, sirka, vyska, dokladne).FirstOrDefault();

    /// <summary>Kód nájdený čítačkou. Slabý = čiarový kód potvrdený len na málo riadkoch obrázka (môže byť omyl).</summary>
    private readonly record struct NajdenyKod(string Text, bool Slaby);

    /// <summary>Jedno čítanie obrázka čítačkou zxing-cpp.</summary>
    /// <param name="najmenejRiadkov">čiarové kódy potvrdené na menej riadkoch sa vynechajú (0 = nevynechá sa nič)</param>
    private static List<NajdenyKod> Citaj(byte[] data, int sirka, int vyska, ZXingCpp.ImageFormat format, bool dokladne,
        ZXingCpp.Binarizer binarizer, int najmenejRiadkov)
    {
        var vysledok = new List<NajdenyKod>();
        var pamat = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var obraz = new ZXingCpp.ImageView(pamat.AddrOfPinnedObject(), sirka, vyska, format);
            using var citac = new ZXingCpp.BarcodeReader
            {
                TryHarder = dokladne,
                TryRotate = true,
                TryInvert = dokladne,
                TryDownscale = true,
                Binarizer = binarizer,
                MaxNumberOfSymbols = 10,
            };
            foreach (var kod in citac.From(obraz))
            {
                using (kod)
                {
                    var text = kod.IsValid ? KodyEvita.Ocisti(kod.Text) : "";
                    var riadky = kod.LineCount; // len čiarové kódy, pri QR a pod. je 0
                    if (text.Length == 0 || text.Length > NajdlhsiKod || (riadky > 0 && riadky < najmenejRiadkov)
                        || vysledok.Any(v => v.Text == text))
                    {
                        continue;
                    }

                    vysledok.Add(new NajdenyKod(text, riadky is > 0 and <= 2));
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

    /// <summary>Výsledok čítania fotky: nájdené kódy a stručný popis fotky a priebehu (do hlásenia pri neúspechu).</summary>
    /// <param name="Sirka">šírka fotky, ako prišla (po otočení podľa EXIF)</param>
    /// <param name="Vyska">výška fotky, ako prišla</param>
    public sealed record CitanieFotky(List<string> Kody, string Popis, int Sirka, int Vyska);

    /// <summary>Všetky kódy z obrázka v súbore (JPEG, PNG … – fotka z mobilu).</summary>
    public static List<string> PrecitajVsetkyZObrazka(byte[] subor) => PrecitajFotku(subor).Kody;

    /// <summary>Prvý kód z obrázka v súbore, alebo null.</summary>
    public static string? PrecitajZObrazka(byte[] subor) => PrecitajVsetkyZObrazka(subor).FirstOrDefault();

    /// <summary>
    /// Kódy z fotky v súbore. Fotka sa prevedie na odtiene sivej v plnom rozlíšení (najviac <see cref="NajvacsiRozmerFotky"/>)
    /// a číta sa postupne: priamo, po zaostrení, po zaostrení s pevným prahom a zmenšená. Skončí sa, keď sa nájde kód
    /// výrobku (EAN/UPC) alebo štítok EviTA – inak sa pozbierajú kódy zo všetkých pokusov. Čaká sa vlákno STA.
    /// </summary>
    /// <exception cref="EvitaException">súbor nie je obrázok, alebo ho Windows nevie otvoriť (napr. HEIC bez doplnku)</exception>
    public static CitanieFotky PrecitajFotku(byte[] subor)
    {
        var cas = System.Diagnostics.Stopwatch.StartNew();
        var druh = DruhObrazka(subor);
        var (sivy, sirka, vyska, povodny, povodnaSirka, povodnaVyska) = NacitajSivy(subor, druh);
        var popis = $"{druh} {povodny}, {Math.Max(1, subor.Length / 1024)} kB";

        var kody = new List<NajdenyKod>();
        var pokusy = 0;
        var zaostreny = new Lazy<byte[]>(() => Zaostri(sivy, sirka, vyska, sirka > 2000 || vyska > 2000 ? 2 : 1));
        var postupy = new List<Func<List<NajdenyKod>>>
        {
            () => Citaj(sivy, sirka, vyska, ZXingCpp.ImageFormat.Lum, true, ZXingCpp.Binarizer.LocalAverage, 0),
            () => Citaj(zaostreny.Value, sirka, vyska, ZXingCpp.ImageFormat.Lum, true, ZXingCpp.Binarizer.LocalAverage, 3),
            () => Citaj(RoztiahniKontrast(zaostreny.Value), sirka, vyska, ZXingCpp.ImageFormat.Lum, true, ZXingCpp.Binarizer.FixedThreshold, 3),
        };
        if (Math.Max(sirka, vyska) < 1200)
        {
            // malý obrázok (výrez, náhľad): zväčšený na dvojnásobok majú tenké pruhy viac bodov
            var dvojity = new Lazy<(byte[] Data, int Sirka, int Vyska)>(() => Dvojnasobok(sivy, sirka, vyska));
            postupy.Insert(1, () => Citaj(dvojity.Value.Data, dvojity.Value.Sirka, dvojity.Value.Vyska, ZXingCpp.ImageFormat.Lum, true, ZXingCpp.Binarizer.LocalAverage, 0));
            postupy.Insert(2, () => Citaj(Zaostri(dvojity.Value.Data, dvojity.Value.Sirka, dvojity.Value.Vyska, 1), dvojity.Value.Sirka, dvojity.Value.Vyska,
                ZXingCpp.ImageFormat.Lum, true, ZXingCpp.Binarizer.LocalAverage, 3));
        }

        if (Math.Min(sirka, vyska) >= 800)
        {
            postupy.Add(() =>
            {
                var (polovica, s2, v2) = Polovica(sivy, sirka, vyska);
                return Citaj(polovica, s2, v2, ZXingCpp.ImageFormat.Lum, true, ZXingCpp.Binarizer.LocalAverage, 3);
            });
        }

        foreach (var postup in postupy)
        {
            pokusy++;
            foreach (var kod in postup())
            {
                var ten = kody.FindIndex(k => k.Text == kod.Text);
                if (ten < 0)
                {
                    kody.Add(kod);
                }
                else if (kody[ten].Slaby && !kod.Slaby)
                {
                    kody[ten] = kod;
                }
            }

            if (kody.Any(k => !k.Slaby && (KodyEvita.NormalizujEan(k.Text) is not null || KodyEvita.PrirastkoveZKodu(k.Text) is not null)))
            {
                break;
            }
        }

        // slabé čítania (čiarový kód len na okraji pruhov) až nakoniec – pri výbere kódu majú prednosť isté
        var vysledok = kody.OrderBy(k => k.Slaby).Select(k => k.Text).ToList();
        return new CitanieFotky(vysledok, $"{popis} → {sirka}×{vyska}, {pokusy}×, {cas.ElapsedMilliseconds} ms", povodnaSirka, povodnaVyska);
    }

    /// <summary>Druh obrázka podľa prvých bajtov (len na popis a na jasné hlásenie pri nepodporovanom formáte).</summary>
    public static string DruhObrazka(byte[] subor)
    {
        static bool Je(byte[] s, int od, string text) =>
            s.Length >= od + text.Length && text.Select((z, i) => s[od + i] == (byte)z).All(x => x);

        if (subor.Length >= 3 && subor[0] == 0xFF && subor[1] == 0xD8 && subor[2] == 0xFF)
        {
            return "JPEG";
        }

        if (Je(subor, 1, "PNG"))
        {
            return "PNG";
        }

        if (Je(subor, 4, "ftyp"))
        {
            return Je(subor, 8, "avif") || Je(subor, 8, "avis") ? "AVIF" : "HEIC";
        }

        if (Je(subor, 0, "RIFF") && Je(subor, 8, "WEBP"))
        {
            return "WebP";
        }

        if (Je(subor, 0, "GIF8"))
        {
            return "GIF";
        }

        return Je(subor, 0, "BM") ? "BMP" : "?";
    }

    /// <summary>Fotka ako odtiene sivej (1 bajt na bod), otočená podľa EXIF a zmenšená na najviac <see cref="NajvacsiRozmerFotky"/>.</summary>
    private static (byte[] Sivy, int Sirka, int Vyska, string Povodny, int PovodnaSirka, int PovodnaVyska) NacitajSivy(byte[] subor, string druh)
    {
        BitmapSource snimka;
        double otocenie;
        try
        {
            using var prud = new System.IO.MemoryStream(subor);
            var dekoder = BitmapDecoder.Create(prud, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            snimka = dekoder.Frames[0];
            otocenie = OtocenieZExif(dekoder.Frames[0]);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new EvitaException(druh is "HEIC" or "AVIF"
                ? Preklad.F($"Fotka je vo formáte {druh}, ktorý Windows bez doplnku neotvorí. Vo fotoaparáte mobilu vypnite formát HEIF (vysoká účinnosť) a odfoťte kód znova.")
                : Preklad.F($"Súbor sa nedá otvoriť ako obrázok ({druh}, {Math.Max(1, subor.Length / 1024)} kB)."), ex);
        }

        var povodny = $"{snimka.PixelWidth}×{snimka.PixelHeight}" + (otocenie != 0 ? $" ↻{otocenie:0}°" : "");
        if (otocenie != 0)
        {
            snimka = new TransformedBitmap(snimka, new RotateTransform(otocenie));
        }

        int povodnaSirka = snimka.PixelWidth, povodnaVyska = snimka.PixelHeight;
        var vacsia = Math.Max(snimka.PixelWidth, snimka.PixelHeight);
        if (vacsia > NajvacsiRozmerFotky)
        {
            var mierka = (double)NajvacsiRozmerFotky / vacsia;
            snimka = new TransformedBitmap(snimka, new ScaleTransform(mierka, mierka));
        }

        var sivy = new FormatConvertedBitmap(snimka, PixelFormats.Gray8, null, 0);
        var data = new byte[sivy.PixelWidth * sivy.PixelHeight];
        sivy.CopyPixels(data, sivy.PixelWidth, 0);
        return (data, sivy.PixelWidth, sivy.PixelHeight, povodny, povodnaSirka, povodnaVyska);
    }

    /// <summary>Obrázok zväčšený na dvojnásobok (lineárne medzi susednými bodmi).</summary>
    private static (byte[] Data, int Sirka, int Vyska) Dvojnasobok(byte[] sivy, int sirka, int vyska)
    {
        int s2 = sirka * 2, v2 = vyska * 2;
        var vysledok = new byte[s2 * v2];
        for (var y = 0; y < v2; y++)
        {
            // stred nového bodu v súradniciach pôvodného obrázka
            var sy = Math.Clamp((y - 0.5) / 2, 0, vyska - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, vyska - 1);
            var dy = sy - y0;
            for (var x = 0; x < s2; x++)
            {
                var sx = Math.Clamp((x - 0.5) / 2, 0, sirka - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, sirka - 1);
                var dx = sx - x0;
                var hore = sivy[y0 * sirka + x0] * (1 - dx) + sivy[y0 * sirka + x1] * dx;
                var dole = sivy[y1 * sirka + x0] * (1 - dx) + sivy[y1 * sirka + x1] * dx;
                vysledok[y * s2 + x] = (byte)Math.Round(hore * (1 - dy) + dole * dy);
            }
        }

        return (vysledok, s2, v2);
    }

    /// <summary>Zaostrenie (neostrá maska): bod + 1,5 × (bod − priemer okolia). Pomôže pri mierne rozmazanej fotke.</summary>
    private static byte[] Zaostri(byte[] sivy, int sirka, int vyska, int polomer)
    {
        var rozmazany = Rozmaz(sivy, sirka, vyska, polomer);
        var vysledok = new byte[sivy.Length];
        for (var i = 0; i < sivy.Length; i++)
        {
            var hodnota = sivy[i] + 3 * (sivy[i] - rozmazany[i]) / 2;
            vysledok[i] = (byte)Math.Clamp(hodnota, 0, 255);
        }

        return vysledok;
    }

    /// <summary>Priemer štvorcového okolia (2r+1)² – dva prechody s kĺzavým súčtom, okraj sa opakuje.</summary>
    private static byte[] Rozmaz(byte[] sivy, int sirka, int vyska, int r)
    {
        var okno = 2 * r + 1;
        var vodorovne = new byte[sivy.Length];
        for (var y = 0; y < vyska; y++)
        {
            var riadok = y * sirka;
            var sucet = 0;
            for (var k = -r; k <= r; k++)
            {
                sucet += sivy[riadok + Math.Clamp(k, 0, sirka - 1)];
            }

            for (var x = 0; x < sirka; x++)
            {
                vodorovne[riadok + x] = (byte)(sucet / okno);
                sucet += sivy[riadok + Math.Min(x + r + 1, sirka - 1)] - sivy[riadok + Math.Max(x - r, 0)];
            }
        }

        var vysledok = new byte[sivy.Length];
        for (var x = 0; x < sirka; x++)
        {
            var sucet = 0;
            for (var k = -r; k <= r; k++)
            {
                sucet += vodorovne[Math.Clamp(k, 0, vyska - 1) * sirka + x];
            }

            for (var y = 0; y < vyska; y++)
            {
                vysledok[y * sirka + x] = (byte)(sucet / okno);
                sucet += vodorovne[Math.Min(y + r + 1, vyska - 1) * sirka + x] - vodorovne[Math.Max(y - r, 0) * sirka + x];
            }
        }

        return vysledok;
    }

    /// <summary>Roztiahnutie jasu: 1 % najtmavších bodov bude čiernych, 1 % najsvetlejších bielych (potom stačí pevný prah).</summary>
    private static byte[] RoztiahniKontrast(byte[] sivy)
    {
        var histogram = new int[256];
        foreach (var b in sivy)
        {
            histogram[b]++;
        }

        int Hranica(long pocet, bool odZaciatku)
        {
            long sucet = 0;
            for (var i = 0; i < 256; i++)
            {
                var index = odZaciatku ? i : 255 - i;
                sucet += histogram[index];
                if (sucet >= pocet)
                {
                    return index;
                }
            }

            return odZaciatku ? 0 : 255;
        }

        var percento = Math.Max(1, sivy.Length / 100);
        var dole = Hranica(percento, true);
        var hore = Hranica(percento, false);
        var rozsah = Math.Max(1, hore - dole);
        var tabulka = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            tabulka[i] = (byte)Math.Clamp((i - dole) * 255 / rozsah, 0, 255);
        }

        var vysledok = new byte[sivy.Length];
        for (var i = 0; i < sivy.Length; i++)
        {
            vysledok[i] = tabulka[sivy[i]];
        }

        return vysledok;
    }

    /// <summary>Obrázok zmenšený na polovicu (priemer 2 × 2 bodov).</summary>
    private static (byte[] Data, int Sirka, int Vyska) Polovica(byte[] sivy, int sirka, int vyska)
    {
        int s2 = sirka / 2, v2 = vyska / 2;
        var vysledok = new byte[s2 * v2];
        for (var y = 0; y < v2; y++)
        {
            var a = 2 * y * sirka;
            var b = a + sirka;
            for (var x = 0; x < s2; x++)
            {
                vysledok[y * s2 + x] = (byte)((sivy[a + 2 * x] + sivy[a + 2 * x + 1] + sivy[b + 2 * x] + sivy[b + 2 * x + 1] + 2) / 4);
            }
        }

        return (vysledok, s2, v2);
    }

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
