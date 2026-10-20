using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace Evita.Services;

public sealed record KameraInfo(string Id, string Nazov);

/// <summary>Jedna snímka z kamery (BGRA, 4 bajty na bod).</summary>
public sealed record SnimkaKamery(byte[] Data, int Sirka, int Vyska);

/// <summary>
/// Čítanie kódov kamerou počítača (webkamera, kamera notebooku či tabletu) cez Windows.Media.Capture.
/// Snímky sa posielajú na zobrazenie a zároveň sa v nich hľadá kód (niekoľkokrát za sekundu).
/// </summary>
public sealed class KameraSkener : IAsyncDisposable
{
    private MediaCapture? _kamera;
    private MediaFrameReader? _citac;
    private int _citam;
    private int _zobrazujem;
    private DateTime _posledneCitanie = DateTime.MinValue;

    /// <summary>Nová snímka na zobrazenie (volá sa mimo vlákna rozhrania).</summary>
    public event Action<SnimkaKamery>? Snimka;

    /// <summary>Prečítaný kód (volá sa mimo vlákna rozhrania).</summary>
    public event Action<string>? Kod;

    public static async Task<List<KameraInfo>> ZoznamAsync()
    {
        var zariadenia = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        return zariadenia.Where(z => z.IsEnabled).Select(z => new KameraInfo(z.Id, z.Name)).ToList();
    }

    public async Task SpustiAsync(string? idKamery)
    {
        await ZastavAsync();
        var vylucne = true;
        try
        {
            _kamera = new MediaCapture();
            try
            {
                await _kamera.InitializeAsync(Nastavenia(idKamery, MediaCaptureSharingMode.ExclusiveControl));
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                // kameru používa iný program – stačí zdieľaný prístup (bez voľby rozlíšenia)
                _kamera.Dispose();
                _kamera = new MediaCapture();
                vylucne = false;
                await _kamera.InitializeAsync(Nastavenia(idKamery, MediaCaptureSharingMode.SharedReadOnly));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new EvitaException(Preklad.T("Windows nepovolil prístup ku kamere. Zapnite ho v Nastaveniach Windows → Ochrana osobných údajov a zabezpečenie → Kamera (povoliť prístup ku kamere aj pre klasické aplikácie)."), ex);
        }
        catch (Exception ex) when (ex is not EvitaException)
        {
            throw new EvitaException(Preklad.F($"Kameru sa nepodarilo zapnúť. Možno ju práve používa iný program.\n\n{ex.Message}"), ex);
        }

        var zdroj = _kamera.FrameSources.Values.FirstOrDefault(z => z.Info.SourceKind == MediaFrameSourceKind.Color && z.Info.MediaStreamType == MediaStreamType.VideoPreview)
                    ?? _kamera.FrameSources.Values.FirstOrDefault(z => z.Info.SourceKind == MediaFrameSourceKind.Color)
                    ?? throw new EvitaException(Preklad.T("Kamera neposiela farebný obraz."));

        // rozlíšenie okolo 1280 × 720 stačí na čítanie kódov a je dosť rýchle
        var format = zdroj.SupportedFormats
            .Where(f => f.VideoFormat.Width >= 640 && f.VideoFormat.Width <= 1920)
            .OrderBy(f => Math.Abs((int)f.VideoFormat.Width - 1280))
            .ThenByDescending(f => f.FrameRate.Denominator == 0 ? 0 : (double)f.FrameRate.Numerator / f.FrameRate.Denominator)
            .FirstOrDefault();
        if (format is not null && vylucne)
        {
            try
            {
                await zdroj.SetFormatAsync(format);
            }
            catch (Exception)
            {
                // kamera ponechá svoj predvolený formát
            }
        }

        _citac = await _kamera.CreateFrameReaderAsync(zdroj, MediaEncodingSubtypes.Bgra8);
        _citac.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
        _citac.FrameArrived += PriSnimke;
        var stav = await _citac.StartAsync();
        if (stav != MediaFrameReaderStartStatus.Success)
        {
            throw new EvitaException(Preklad.F($"Kamera sa nespustila ({stav})."));
        }
    }

    private static MediaCaptureInitializationSettings Nastavenia(string? idKamery, MediaCaptureSharingMode rezim) => new()
    {
        VideoDeviceId = idKamery ?? "",
        StreamingCaptureMode = StreamingCaptureMode.Video,
        SharingMode = rezim,
        MemoryPreference = MediaCaptureMemoryPreference.Cpu,
    };

    /// <summary>Zobrazenie dokončilo prácu so snímkou – môže prísť ďalšia.</summary>
    public void SnimkaZobrazena() => Interlocked.Exchange(ref _zobrazujem, 0);

    private void PriSnimke(MediaFrameReader citac, MediaFrameArrivedEventArgs args)
    {
        var zobrazit = Interlocked.CompareExchange(ref _zobrazujem, 1, 0) == 0;
        var citat = (DateTime.Now - _posledneCitanie).TotalMilliseconds > 180 && Interlocked.CompareExchange(ref _citam, 1, 0) == 0;
        if (!zobrazit && !citat)
        {
            return;
        }

        SnimkaKamery? snimka = null;
        try
        {
            using var ramec = citac.TryAcquireLatestFrame();
            var bitmapa = ramec?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmapa is not null)
            {
                SoftwareBitmap? prevedena = null;
                if (bitmapa.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
                {
                    prevedena = SoftwareBitmap.Convert(bitmapa, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
                }

                var zdroj = prevedena ?? bitmapa;
                var data = new byte[zdroj.PixelWidth * zdroj.PixelHeight * 4];
                zdroj.CopyToBuffer(data.AsBuffer());
                snimka = new SnimkaKamery(data, zdroj.PixelWidth, zdroj.PixelHeight);
                prevedena?.Dispose();
            }
        }
        catch (Exception)
        {
            // snímka sa preskočí
        }

        if (snimka is null)
        {
            if (zobrazit)
            {
                SnimkaZobrazena();
            }

            if (citat)
            {
                Interlocked.Exchange(ref _citam, 0);
            }

            return;
        }

        if (zobrazit)
        {
            Snimka?.Invoke(snimka);
        }

        if (citat)
        {
            _posledneCitanie = DateTime.Now;
            Task.Run(() =>
            {
                try
                {
                    if (CiarovyKod.Precitaj(snimka.Data, snimka.Sirka, snimka.Vyska) is { } text)
                    {
                        Kod?.Invoke(text);
                    }
                }
                catch (Exception)
                {
                    // nečitateľná snímka
                }
                finally
                {
                    Interlocked.Exchange(ref _citam, 0);
                }
            });
        }
    }

    public async Task ZastavAsync()
    {
        var citac = _citac;
        var kamera = _kamera;
        _citac = null;
        _kamera = null;
        if (citac is not null)
        {
            citac.FrameArrived -= PriSnimke;
            try
            {
                await citac.StopAsync();
            }
            catch (Exception)
            {
                // kamera už mohla byť odpojená
            }

            citac.Dispose();
        }

        kamera?.Dispose();
    }

    public async ValueTask DisposeAsync() => await ZastavAsync();
}
