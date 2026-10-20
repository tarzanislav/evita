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

            // originál (prehliadač fotku nevie zmenšiť) a presne to, čo pošle stránka v mobile: najviac 3200 bodov, JPEG 92
            foreach (var (nazov, data) in new[] { ("originál", original), ("z mobilu", AkoZMobilu(original, 3200, 92)) })
            {
                try
                {
                    var citanie = CiarovyKod.PrecitajFotku(data);
                    Krok($"fotka {subor} ({nazov}): {citanie.Popis}, kódy: {string.Join(", ", citanie.Kody)}");
                    if (!citanie.Kody.Contains(kod))
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
        }

        Over(chyby.Count == 0, $"fotky z mobilu: všetky kódy sa prečítali{(chyby.Count == 0 ? "" : " – " + string.Join("; ", chyby))}");

        // celá cesta cez server Mobil ako skener (POST /foto) vrátane hlásení pri neúspechu
        var server = new MobilnySkener(Dispatcher.CurrentDispatcher);
        try
        {
            server.Spusti(SkenovanieService.PortMobilu());
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            string Posli(byte[] data, string? info = null)
            {
                var obsah = new ByteArrayContent(data);
                obsah.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                using var poziadavka = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{server.Port}/foto?k={server.Kluc}") { Content = obsah };
                if (info is not null)
                {
                    poziadavka.Headers.Add("X-Evita-Foto", info);
                }

                return System.Text.RegularExpressions.Regex.Unescape(Cakaj(Cakaj(http.SendAsync(poziadavka)).Content.ReadAsStringAsync()));
            }

            var stranka = Cakaj(http.GetStringAsync($"http://127.0.0.1:{server.Port}/?k={server.Kluc}"));
            Over(stranka.Contains("X-Evita-Foto") && stranka.Contains("intent://") && stranka.Contains("package=com.android.chrome"),
                "mobil: stránka posiela popis fotky a ponúkne otvorenie v Chrome");

            foreach (var (subor, kod) in zoznam)
            {
                var json = Posli(AkoZMobilu(File.ReadAllBytes(Path.Combine(priecinok, subor)), 3200, 92), "image/jpeg 4000 kB, 3472x4640 -> 2394x3200 900 kB");
                Krok($"fotka {subor} cez /foto: {json}");
                Over(json.Contains($"\"kod\":\"{kod}\""), $"mobil: kód z fotky {subor} ({kod})");
            }

            // náhľad z vnútorného prehliadača aplikácie (MIUI Skener): 160 × 120 bodov
            var nahlad = AkoZMobilu(File.ReadAllBytes(Path.Combine(priecinok, zoznam[0].Subor)), 160, 80);
            var diagnostika = Path.Combine(MobilnySkener.PriecinokDiagnostiky, "necitatelna_fotka.txt");
            if (File.Exists(diagnostika))
            {
                File.Delete(diagnostika);
            }

            var malyJson = Posli(nahlad, "image/jpeg 9 kB, 120x160 -> 120x160 4 kB");
            Over(malyJson.Contains("\"ok\":false") && malyJson.Contains("náhľad") && malyJson.Contains("Chrome") && malyJson.Contains(" bodov"),
                $"mobil: malý náhľad fotky – rada otvoriť stránku v Chrome ({malyJson})");
            Over(File.Exists(Path.Combine(MobilnySkener.PriecinokDiagnostiky, "necitatelna_fotka.jpg"))
                 && File.ReadAllText(diagnostika).Contains("120x160 -> 120x160"),
                "mobil: nečitateľná fotka je odložená na rozbor aj s popisom z mobilu");

            var heic = new byte[64];
            System.Text.Encoding.ASCII.GetBytes("\0\0\0\u0018ftypheic").CopyTo(heic, 0);
            var heicJson = Posli(heic);
            Over(heicJson.Contains("\"ok\":false") && heicJson.Contains("HEIC") && heicJson.Contains("HEIF"), $"mobil: fotka HEIC – jasné hlásenie ({heicJson})");
            var zlyJson = Posli(System.Text.Encoding.ASCII.GetBytes("toto nie je obrazok"));
            Over(zlyJson.Contains("\"ok\":false") && zlyJson.Contains("nedá otvoriť"), $"mobil: poškodený súbor – hlásenie s príčinou ({zlyJson})");
        }
        finally
        {
            server.Dispose();
        }

        Krok("fotky z mobilu v poriadku");
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
}
