using System.IO;
using System.Text;
using Evita.Services;
using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Evita.Pomocne;

/// <summary>
/// Rozpoznanie textu z PDF alebo obrázka (sken, fotka faktúry) vstavaným OCR systému Windows – bez internetu a bez
/// ďalších knižníc. Slová sa skladajú do riadkov podľa polohy, aby riadok tabuľky (názov … počet … cena) zostal spolu.
/// </summary>
public static class OcrDokladu
{
    /// <summary>Jazyk rozpoznávania: slovenčina, inak čeština, inak jazyk používateľa Windows. Null = OCR nie je.</summary>
    public static OcrEngine? Engine(out string jazyk)
    {
        jazyk = "";
        try
        {
            foreach (var kod in new[] { "sk", "sk-SK", "cs", "cs-CZ" })
            {
                var l = new Language(kod);
                if (OcrEngine.IsLanguageSupported(l) && OcrEngine.TryCreateFromLanguage(l) is { } e)
                {
                    jazyk = l.DisplayName;
                    return e;
                }
            }

            if (OcrEngine.TryCreateFromUserProfileLanguages() is { } predvoleny)
            {
                jazyk = predvoleny.RecognizerLanguage.DisplayName;
                return predvoleny;
            }
        }
        catch (Exception)
        {
            // OCR v systéme nie je
        }

        return null;
    }

    /// <summary>Text z PDF (každá strana sa vykreslí a rozpozná) alebo z obrázka.</summary>
    /// <remarks>
    /// Beží na vlákne mimo okna (MTA): objekty Windows Runtime (PDF, OCR) tak nie sú viazané na vlákno okna a uvoľnia sa
    /// ešte počas behu, nie až pri ukončení programu.
    /// </remarks>
    public static Task<(string Text, string Jazyk)> PrecitajAsync(string cesta, CancellationToken zrusenie = default) =>
        Task.Run(async () =>
        {
            try
            {
                return await PrecitajVPozadiAsync(cesta, zrusenie);
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }, zrusenie);

    private static async Task<(string Text, string Jazyk)> PrecitajVPozadiAsync(string cesta, CancellationToken zrusenie)
    {
        var engine = Engine(out var jazyk)
                     ?? throw new EvitaException(Preklad.T("Rozpoznávanie textu (OCR) nie je vo Windows dostupné. Skopírujte text dokladu a vložte ho do okna."));
        var text = new StringBuilder();
        var subor = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(cesta));
        if (Path.GetExtension(cesta).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdf = await PdfDocument.LoadFromFileAsync(subor);
            for (uint i = 0; i < Math.Min(pdf.PageCount, 20u); i++)
            {
                zrusenie.ThrowIfCancellationRequested();
                using var strana = pdf.GetPage(i);
                using var prud = new InMemoryRandomAccessStream();
                var mierka = Math.Min(300.0 / 96.0, OcrEngine.MaxImageDimension / Math.Max(strana.Size.Width, strana.Size.Height));
                await strana.RenderToStreamAsync(prud, new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)(strana.Size.Width * mierka),
                    DestinationHeight = (uint)(strana.Size.Height * mierka),
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
                });
                text.AppendLine(await RozpoznajAsync(engine, prud));
            }
        }
        else
        {
            using var prud = await subor.OpenReadAsync();
            text.AppendLine(await RozpoznajAsync(engine, prud));
        }

        return (text.ToString(), jazyk);
    }

    private static async Task<string> RozpoznajAsync(OcrEngine engine, IRandomAccessStream prud)
    {
        var dekoder = await BitmapDecoder.CreateAsync(prud);
        var najviac = OcrEngine.MaxImageDimension;
        var transformacia = new BitmapTransform();
        if (dekoder.PixelWidth > najviac || dekoder.PixelHeight > najviac)
        {
            var k = (double)najviac / Math.Max(dekoder.PixelWidth, dekoder.PixelHeight);
            transformacia.ScaledWidth = (uint)(dekoder.PixelWidth * k);
            transformacia.ScaledHeight = (uint)(dekoder.PixelHeight * k);
        }

        using var obraz = await dekoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transformacia,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var vysledok = await engine.RecognizeAsync(obraz);
        return Riadky(vysledok.Lines.SelectMany(l => l.Words).Select(w => (w.Text, w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Height,
            w.BoundingRect.Width)));
    }

    /// <summary>Slová do riadkov podľa výšky na strane; veľká medzera medzi slovami = tabulátor (stĺpec tabuľky).</summary>
    public static string Riadky(IEnumerable<(string Text, double X, double Y, double Vyska, double Sirka)> slova)
    {
        var zoznam = slova.OrderBy(s => s.Y + s.Vyska / 2).ToList();
        var riadky = new List<List<(string Text, double X, double Y, double Vyska, double Sirka)>>();
        foreach (var slovo in zoznam)
        {
            var stred = slovo.Y + slovo.Vyska / 2;
            var riadok = riadky.LastOrDefault();
            if (riadok is not null && Math.Abs(riadok.Average(s => s.Y + s.Vyska / 2) - stred) < Math.Max(4, riadok.Average(s => s.Vyska) * 0.55))
            {
                riadok.Add(slovo);
            }
            else
            {
                riadky.Add(new() { slovo });
            }
        }

        var text = new StringBuilder();
        foreach (var riadok in riadky)
        {
            var poradie = riadok.OrderBy(s => s.X).ToList();
            var vyska = poradie.Average(s => s.Vyska);
            for (var i = 0; i < poradie.Count; i++)
            {
                if (i > 0)
                {
                    var medzera = poradie[i].X - (poradie[i - 1].X + poradie[i - 1].Sirka);
                    text.Append(medzera > vyska * 1.8 ? '\t' : ' ');
                }

                text.Append(poradie[i].Text);
            }

            text.AppendLine();
        }

        return text.ToString();
    }
}
