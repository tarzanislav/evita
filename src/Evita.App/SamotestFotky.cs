using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Evita.Services;

namespace Evita;

/// <summary>Samotest čítania kódov zo skutočných fotiek z mobilu (priečinok testy/fotky v repozitári).</summary>
internal static partial class Samotest
{
    private static void SkenovanieFotky()
    {
        var priecinok = Environment.GetEnvironmentVariable("EVITA_SAMOTEST_FOTKY");
        if (string.IsNullOrEmpty(priecinok) || !File.Exists(Path.Combine(priecinok, "fotky.txt")))
        {
            Krok("fotky z mobilu: priečinok s fotkami nie je zadaný (EVITA_SAMOTEST_FOTKY) – test sa vynecháva");
            return;
        }

        var zoznam = File.ReadAllLines(Path.Combine(priecinok, "fotky.txt"))
            .Where(r => r.Contains('|') && !r.TrimStart().StartsWith('#'))
            .Select(r => r.Split('|', 2))
            .Select(c => (Subor: c[0].Trim(), Kod: c[1].Trim()))
            .ToList();
        Over(zoznam.Count > 0, "fotky z mobilu: zoznam fotiek");

        var chyby = new List<string>();
        foreach (var (subor, kod) in zoznam)
        {
            var original = File.ReadAllBytes(Path.Combine(priecinok, subor));
            Krok($"fotka {subor}: {original.Length} B, {PopisObrazka(original)}");

            // presne to, čo pošle stránka v mobile: zmenšenie na 2400 bodov a JPEG s kvalitou 92
            var zMobilu = AkoZMobilu(original, 2400, 92);
            Krok($"fotka {subor} ako z mobilu: {zMobilu.Length} B, {PopisObrazka(zMobilu)}");

            foreach (var (nazov, data) in new[] { ("originál", original), ("z mobilu", zMobilu) })
            {
                var cas = Stopwatch.StartNew();
                try
                {
                    var kody = CiarovyKod.PrecitajVsetkyZObrazka(data);
                    Krok($"fotka {subor} ({nazov}): {cas.ElapsedMilliseconds} ms, kódy: {string.Join(", ", kody)}");
                    if (!kody.Contains(kod))
                    {
                        chyby.Add($"{subor} ({nazov}) – chýba {kod}");
                    }
                }
                catch (Exception ex)
                {
                    Krok($"fotka {subor} ({nazov}): VÝNIMKA {ex}");
                    chyby.Add($"{subor} ({nazov}) – {ex.GetType().Name}: {ex.Message}");
                }
            }

            // priamo surové body BGRA (bez zmenšenia) – oddelí chybu dekódovania od chyby čítačky
            try
            {
                var (bgra, sirka, vyska) = BodyBgra(zMobilu);
                long sucet = 0;
                for (var i = 0; i < bgra.Length; i += 4)
                {
                    sucet += bgra[i] + bgra[i + 1] + bgra[i + 2];
                }

                Krok($"fotka {subor}: BGRA {sirka}×{vyska}, priemerný jas {sucet / 3.0 / (bgra.Length / 4):0.0}, "
                     + $"kódy: {string.Join(", ", CiarovyKod.PrecitajVsetky(bgra, sirka, vyska))}");
            }
            catch (Exception ex)
            {
                Krok($"fotka {subor}: BGRA VÝNIMKA {ex}");
            }
        }

        // celá cesta cez server Mobil ako skener (POST /foto)
        var server = new MobilnySkener(Dispatcher.CurrentDispatcher);
        try
        {
            server.Spusti(SkenovanieService.PortMobilu());
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            foreach (var (subor, kod) in zoznam)
            {
                var zMobilu = AkoZMobilu(File.ReadAllBytes(Path.Combine(priecinok, subor)), 2400, 92);
                var obsah = new ByteArrayContent(zMobilu);
                obsah.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                var odpoved = Cakaj(http.PostAsync($"http://127.0.0.1:{server.Port}/foto?k={server.Kluc}", obsah));
                var json = Cakaj(odpoved.Content.ReadAsStringAsync());
                Krok($"fotka {subor} cez /foto: {json}");
                if (!json.Contains(kod.Replace("/", "\\/")) && !json.Contains(kod))
                {
                    chyby.Add($"{subor} cez /foto – chýba {kod}");
                }
            }
        }
        finally
        {
            server.Dispose();
        }

        Over(chyby.Count == 0, $"fotky z mobilu: všetky kódy sa prečítali{(chyby.Count == 0 ? "" : " – " + string.Join("; ", chyby))}");
    }

    private static string PopisObrazka(byte[] subor)
    {
        try
        {
            using var prud = new MemoryStream(subor);
            var dekoder = BitmapDecoder.Create(prud, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var snimka = dekoder.Frames[0];
            string orientacia;
            try
            {
                orientacia = snimka.Metadata is BitmapMetadata m && m.ContainsQuery("System.Photo.Orientation")
                    ? $"{m.GetQuery("System.Photo.Orientation")} ({m.GetQuery("System.Photo.Orientation")?.GetType().Name})"
                    : "bez EXIF otočenia";
            }
            catch (Exception ex)
            {
                orientacia = "metadáta: " + ex.GetType().Name;
            }

            return $"{dekoder.CodecInfo?.FriendlyName}, {snimka.PixelWidth}×{snimka.PixelHeight}, {snimka.Format}, DPI {snimka.DpiX:0}, {orientacia}";
        }
        catch (Exception ex)
        {
            return "NEDÁ SA DEKÓDOVAŤ: " + ex.Message;
        }
    }

    /// <summary>Fotka upravená tak ako na stránke v mobile (canvas: zmenšenie, JPEG).</summary>
    private static byte[] AkoZMobilu(byte[] subor, int najvacsia, int kvalita)
    {
        using var prud = new MemoryStream(subor);
        BitmapSource snimka = BitmapFrame.Create(prud, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        var m = Math.Min(1.0, (double)najvacsia / Math.Max(snimka.PixelWidth, snimka.PixelHeight));
        if (m < 1)
        {
            snimka = new TransformedBitmap(snimka, new ScaleTransform(m, m));
        }

        var koder = new JpegBitmapEncoder { QualityLevel = kvalita };
        koder.Frames.Add(BitmapFrame.Create(snimka));
        using var vystup = new MemoryStream();
        koder.Save(vystup);
        return vystup.ToArray();
    }

    private static (byte[] Data, int Sirka, int Vyska) BodyBgra(byte[] subor)
    {
        using var prud = new MemoryStream(subor);
        var snimka = BitmapFrame.Create(prud, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(snimka, PixelFormats.Bgra32, null, 0);
        var data = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(data, bgra.PixelWidth * 4, 0);
        return (data, bgra.PixelWidth, bgra.PixelHeight);
    }
}
