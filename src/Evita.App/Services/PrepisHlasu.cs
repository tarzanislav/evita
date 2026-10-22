using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace Evita.Services;

/// <summary>
/// Prepis reči do textu bez internetu: EvitaHlas.dll (whisper.cpp v1.9.5, MIT) zostavená v GitHub Actions zo zdrojového
/// kódu overeného commitu. Model sa načíta raz a ostáva v pamäti, kým sa program nezavrie alebo nezmení model.
/// </summary>
public static class PrepisHlasu
{
    private const string Kniznica = "EvitaHlas.dll";
    public const int VzorkovaFrekvencia = 16000;

    [DllImport(Kniznica, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern IntPtr evita_hlas_otvor(string cesta);

    [DllImport(Kniznica, CallingConvention = CallingConvention.Cdecl)]
    private static extern int evita_hlas_prepis(IntPtr kontext, float[] zvuk, int vzoriek, byte[] jazyk, int vlakna, byte[] vyzva,
        byte[] vystup, int velkost);

    [DllImport(Kniznica, CallingConvention = CallingConvention.Cdecl)]
    private static extern void evita_hlas_zavri(IntPtr kontext);

    private static readonly SemaphoreSlim Zamok = new(1, 1);
    private static IntPtr _kontext;
    private static string? _model;

    /// <summary>Knižnica je pri programe a procesor zvládne prepis (AVX2 – procesory približne od roku 2013).</summary>
    public static string? Prekazka()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, Kniznica)))
        {
            return Preklad.T("Prepis bez internetu nie je v tejto inštalácii k dispozícii (chýba súčasť EvitaHlas.dll).");
        }

        return Avx2.IsSupported && Fma.IsSupported
            ? null
            : Preklad.T("Tento procesor nepodporuje inštrukcie AVX2 – prepis bez internetu by bol príliš pomalý. Použite hlasové písanie Windows.");
    }

    /// <summary>Počet vlákien – polovica logických jadier (zvyčajne fyzické jadrá), 2 až 8.</summary>
    public static int Vlakna => Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    /// <summary>Prepíše zvuk (16 kHz mono) po slovensky. Model sa pri prvom použití načíta (niekoľko sekúnd).</summary>
    public static async Task<string> PrepisAsync(float[] zvuk, ModelHlasu model, CancellationToken zrusenie = default)
    {
        if (Prekazka() is { } prekazka)
        {
            throw new EvitaException(prekazka);
        }

        if (!model.JeStiahnuty)
        {
            throw new EvitaException(Preklad.T("Model na prepis nie je stiahnutý. Stiahnite ho v nastavení diktovania."));
        }

        if (zvuk.Length < VzorkovaFrekvencia / 4)
        {
            return "";
        }

        await Zamok.WaitAsync(zrusenie);
        try
        {
            return await Task.Run(() =>
            {
                if (_kontext == IntPtr.Zero || _model != model.Cesta)
                {
                    ZavriBezZamku();
                    _kontext = evita_hlas_otvor(model.Cesta);
                    if (_kontext == IntPtr.Zero)
                    {
                        throw new EvitaException(Preklad.T("Model na prepis sa nepodarilo načítať. Stiahnite ho v nastavení diktovania znova."));
                    }

                    _model = model.Cesta;
                }

                var vystup = new byte[64 * 1024];
                var dlzka = evita_hlas_prepis(_kontext, zvuk, zvuk.Length, Utf8(Diktovanie.Jazyk), Vlakna, Utf8(Diktovanie.Vyzva), vystup, vystup.Length);
                if (dlzka < 0)
                {
                    throw new EvitaException(Preklad.T("Prepis sa nepodaril."));
                }

                return Uprav(Encoding.UTF8.GetString(vystup, 0, dlzka));
            }, zrusenie);
        }
        finally
        {
            Zamok.Release();
        }
    }

    /// <summary>Uvoľní model z pamäte (pri zmene alebo vymazaní modelu).</summary>
    public static void Zavri()
    {
        Zamok.Wait();
        try
        {
            ZavriBezZamku();
        }
        finally
        {
            Zamok.Release();
        }
    }

    private static void ZavriBezZamku()
    {
        if (_kontext != IntPtr.Zero)
        {
            evita_hlas_zavri(_kontext);
            _kontext = IntPtr.Zero;
            _model = null;
        }
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

    /// <summary>Medzery navyše a typické „halucinácie“ z ticha (titulky, poďakovanie) preč.</summary>
    public static string Uprav(string text)
    {
        var t = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        foreach (var smetie in new[] { "[BLANK_AUDIO]", "[ Silence ]", "(ticho)", "[hudba]" })
        {
            t = t.Replace(smetie, "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        return t;
    }

    /// <summary>Zvuk z WAV (PCM 16 bit, ľubovoľná frekvencia a počet kanálov) prevedený na 16 kHz mono.</summary>
    public static float[] NacitajWav(string cesta)
    {
        using var citac = new BinaryReader(File.OpenRead(cesta));
        if (new string(citac.ReadChars(4)) != "RIFF")
        {
            throw new InvalidDataException("WAV");
        }

        citac.ReadInt32();
        citac.ReadChars(4);
        int kanaly = 1, frekvencia = 16000, bity = 16;
        byte[]? data = null;
        while (citac.BaseStream.Position + 8 <= citac.BaseStream.Length)
        {
            var id = new string(citac.ReadChars(4));
            var velkost = citac.ReadInt32();
            if (id == "fmt ")
            {
                citac.ReadInt16();
                kanaly = citac.ReadInt16();
                frekvencia = citac.ReadInt32();
                citac.ReadInt32();
                citac.ReadInt16();
                bity = citac.ReadInt16();
                citac.BaseStream.Seek(velkost - 16, SeekOrigin.Current);
            }
            else if (id == "data")
            {
                data = citac.ReadBytes(Math.Min(velkost, (int)(citac.BaseStream.Length - citac.BaseStream.Position)));
            }
            else
            {
                citac.BaseStream.Seek(velkost + (velkost & 1), SeekOrigin.Current);
            }
        }

        if (data is null || bity != 16)
        {
            throw new InvalidDataException("WAV PCM 16");
        }

        var vzoriek = data.Length / 2 / kanaly;
        var mono = new float[vzoriek];
        for (var i = 0; i < vzoriek; i++)
        {
            var sucet = 0f;
            for (var k = 0; k < kanaly; k++)
            {
                sucet += BitConverter.ToInt16(data, (i * kanaly + k) * 2) / 32768f;
            }

            mono[i] = sucet / kanaly;
        }

        return Prevzorkuj(mono, frekvencia);
    }

    /// <summary>Lineárne prevzorkovanie na 16 kHz.</summary>
    public static float[] Prevzorkuj(float[] zvuk, int frekvencia)
    {
        if (frekvencia == VzorkovaFrekvencia)
        {
            return zvuk;
        }

        var pomer = (double)frekvencia / VzorkovaFrekvencia;
        var vysledok = new float[(int)(zvuk.Length / pomer)];
        for (var i = 0; i < vysledok.Length; i++)
        {
            var pozicia = i * pomer;
            var j = (int)pozicia;
            var podiel = (float)(pozicia - j);
            vysledok[i] = j + 1 < zvuk.Length ? zvuk[j] * (1 - podiel) + zvuk[j + 1] * podiel : zvuk[^1];
        }

        return vysledok;
    }
}
