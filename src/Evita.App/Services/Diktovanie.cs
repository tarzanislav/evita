using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Evita.Data;

namespace Evita.Services;

/// <summary>Ako sa diktovaná poznámka prepíše do textu.</summary>
public enum SposobDiktovania
{
    /// <summary>Hlasové písanie Windows (Win+H) – vie po slovensky, potrebuje internet (Microsoft Azure).</summary>
    Windows,

    /// <summary>Prepis v počítači (whisper.cpp) – bez internetu, nič neodchádza von; treba stiahnuť model.</summary>
    Offline,
}

/// <summary>Model na prepis reči (whisper.cpp, formát ggml) – súbor sa overí podľa SHA-256.</summary>
public sealed record ModelHlasu(string Kluc, string NazovSk, string PopisSk, string Subor, string Url, string Sha256, int VelkostMb)
{
    public string Nazov => Preklad.T(NazovSk);
    public string Popis => Preklad.T(PopisSk);
    public string Cesta => Path.Combine(Diktovanie.PriecinokModelov, Subor);
    public bool JeStiahnuty => File.Exists(Cesta);
}

/// <summary>Nastavenia diktovania poznámok a správa modelov na prepis bez internetu.</summary>
public static class Diktovanie
{
    public const string KlucSposob = "Poznamky.Diktovanie";
    public const string KlucModel = "Poznamky.DiktovanieModel";

    /// <summary>Jazyk prepisu (slovenčina).</summary>
    public const string Jazyk = "sk";

    /// <summary>Úvodný text pre model – slovenský pravopis, interpunkcia a slovník IT techniky.</summary>
    public const string Vyzva = "Poznámka k evidencii IT techniky: notebook, počítač, tlačiareň, projektor, učebňa, kabinet, oprava, záruka.";

    public static IReadOnlyList<ModelHlasu> Modely { get; } = new[]
    {
        new ModelHlasu("small", Preklad.N("Štandardný (odporúčaný)"),
            Preklad.N("dobrá presnosť po slovensky, prepis približne v čase nahrávky"),
            "ggml-small.bin", "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
            "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", 488),
        new ModelHlasu("turbo", Preklad.N("Presnejší"),
            Preklad.N("najlepšia presnosť, prepis trvá dlhšie (výkonnejší počítač)"),
            "ggml-large-v3-turbo-q5_0.bin", "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin",
            "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2", 574),
        new ModelHlasu("base", Preklad.N("Rýchly"),
            Preklad.N("rýchly aj na slabšom počítači, po slovensky menej presný"),
            "ggml-base.bin", "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
            "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", 148),
    };

    /// <summary>Modely sú mimo priečinka s databázou, aby nezväčšovali zálohy.</summary>
    public static string PriecinokModelov =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Databaza.Priecinok.TrimEnd('\\', '/'))) ?? Databaza.Priecinok, "Modely");

    public static SposobDiktovania Sposob
    {
        get
        {
            try
            {
                using var db = Databaza.Otvor();
                return Databaza.Citaj(db, KlucSposob) == "offline" ? SposobDiktovania.Offline : SposobDiktovania.Windows;
            }
            catch (Exception)
            {
                return SposobDiktovania.Windows;
            }
        }
        set
        {
            using var db = Databaza.Otvor();
            Databaza.Zapis(db, KlucSposob, value == SposobDiktovania.Offline ? "offline" : "windows");
            db.SaveChanges();
        }
    }

    public static ModelHlasu Model
    {
        get
        {
            try
            {
                using var db = Databaza.Otvor();
                var kluc = Databaza.Citaj(db, KlucModel);
                return Modely.FirstOrDefault(m => m.Kluc == kluc) ?? Modely[0];
            }
            catch (Exception)
            {
                return Modely[0];
            }
        }
        set
        {
            using var db = Databaza.Otvor();
            Databaza.Zapis(db, KlucModel, value.Kluc);
            db.SaveChanges();
        }
    }

    /// <summary>Stiahne model, overí SHA-256 a až potom ho uloží (pri chybe nič nezostane).</summary>
    public static async Task StiahniAsync(ModelHlasu model, IProgress<double>? priebeh, CancellationToken zrusenie)
    {
        Directory.CreateDirectory(PriecinokModelov);
        var docasny = model.Cesta + ".stahuje";
        try
        {
            using var klient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            klient.DefaultRequestHeaders.UserAgent.ParseAdd($"EviTA/{AktualizaciaService.AktualnaVerzia}");
            using var odpoved = await klient.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, zrusenie);
            odpoved.EnsureSuccessStatusCode();
            var spolu = odpoved.Content.Headers.ContentLength ?? model.VelkostMb * 1024L * 1024L;
            await using (var zdroj = await odpoved.Content.ReadAsStreamAsync(zrusenie))
            await using (var ciel = new FileStream(docasny, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
            {
                var buffer = new byte[1 << 20];
                long precitane = 0;
                int n;
                while ((n = await zdroj.ReadAsync(buffer, zrusenie)) > 0)
                {
                    await ciel.WriteAsync(buffer.AsMemory(0, n), zrusenie);
                    precitane += n;
                    priebeh?.Report(Math.Min(0.99, (double)precitane / Math.Max(1, spolu)));
                }
            }

            if (!await OverAsync(docasny, model.Sha256, zrusenie))
            {
                throw new EvitaException(Preklad.T("Stiahnutý model nemá správny kontrolný súčet (SHA-256) – nepoužije sa. Skúste to znova."));
            }

            File.Move(docasny, model.Cesta, true);
            priebeh?.Report(1);
        }
        catch (HttpRequestException ex)
        {
            throw new EvitaException(Preklad.F($"Model sa nepodarilo stiahnuť. Skontrolujte pripojenie k internetu.\n\n{ex.Message}"));
        }
        finally
        {
            try
            {
                if (File.Exists(docasny))
                {
                    File.Delete(docasny);
                }
            }
            catch (IOException)
            {
                // dočasný súbor sa zmaže nabudúce
            }
        }
    }

    /// <summary>SHA-256 súboru sa zhoduje s očakávaným.</summary>
    public static async Task<bool> OverAsync(string cesta, string sha256, CancellationToken zrusenie = default)
    {
        await using var subor = new FileStream(cesta, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        var hash = await SHA256.HashDataAsync(subor, zrusenie);
        return Convert.ToHexString(hash).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    public static void Vymaz(ModelHlasu model)
    {
        PrepisHlasu.Zavri();
        if (File.Exists(model.Cesta))
        {
            File.Delete(model.Cesta);
        }
    }
}
