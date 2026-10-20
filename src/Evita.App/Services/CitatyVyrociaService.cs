using System.Net.Http;
using Evita.Data;
using Kalendarium;

namespace Evita.Services;

/// <summary>Riadok výročia na zobrazenie (dashboard, zoznam).</summary>
public sealed record VyrocieRiadok(Vyrocie Vyrocie, int AktualnyRok)
{
    public string RokText => Vyrocie.Rok?.ToString() ?? "";

    /// <summary>„pred 108 rokmi“ – len ak je rok známy.</summary>
    public string PredText => Vyrocie.RokovV(AktualnyRok) is { } rokov and > 0
        ? Preklad.Pocet(rokov, "pred {0} rokom", "pred {0} rokmi", "pred {0} rokmi")
        : "";

    public bool JeOkruhle => Vyrocie.RokovV(AktualnyRok) is { } r and > 0 && r % 25 == 0;

    public string DruhText => CitatyVyrociaService.NazovDruhu(Vyrocie.Druh);

    public string Symbol => Vyrocie.Druh switch
    {
        DruhVyrocia.Narodenie => "*",
        DruhVyrocia.Umrtie => "†",
        _ => "•",
    };

    public string Text => Vyrocie.Text;
    public bool Domace => Vyrocie.Domace;
    public bool Skryte => Vyrocie.JeSkryte;
    public string PovodText => CitatyVyrociaService.NazovPovodu(Vyrocie.Povod);
}

/// <summary>Citáty a výročia z Kalendária v jazyku programu; sťahovanie výročí z Wikipédie.</summary>
public sealed class CitatyVyrociaService
{
    public const string KlucStahovat = "Kalendarium.StahovatVyrocia";
    private static readonly Lazy<HttpClient> Http = new(() => VyrociaZWikipedie.VytvorKlienta($"EviTA/{AktualizaciaService.AktualnaVerzia}"));
    private static readonly SemaphoreSlim Zamok = new(1, 1);

    public static KalendariumDb Db => KalendariumDb.Spolocna;

    /// <summary>Jazyk citátov a výročí = jazyk programu (Wikipédia vo všetkých jazykoch programu).</summary>
    public static string Jazyk => VyrociaZWikipedie.Jazyky.Contains(Preklad.Aktualny.Kod) ? Preklad.Aktualny.Kod : "sk";

    public static string NazovDruhu(DruhVyrocia druh) => druh switch
    {
        DruhVyrocia.Narodenie => Preklad.T("narodenie"),
        DruhVyrocia.Umrtie => Preklad.T("úmrtie"),
        _ => Preklad.T("udalosť"),
    };

    public static string NazovPovodu(string povod) => povod switch
    {
        Povod.Zabudovane => Preklad.T("zabudovaný"),
        Povod.Wikipedia => Preklad.T("Wikipédia"),
        Povod.Import => Preklad.T("import"),
        _ => Preklad.T("vlastný"),
    };

    /// <summary>Automaticky sťahovať výročia z Wikipédie (predvolene áno).</summary>
    public static bool StahovatAutomaticky
    {
        get
        {
            try
            {
                using var db = Databaza.Otvor();
                return Databaza.Citaj(db, KlucStahovat) != "0";
            }
            catch (Exception)
            {
                return true;
            }
        }
        set
        {
            using var db = Databaza.Otvor();
            Databaza.Zapis(db, KlucStahovat, value ? "1" : "0");
            db.SaveChanges();
        }
    }

    /// <summary>Výročia dňa zoradené tak, aby boli hore domáce a okrúhle (pre dashboard).</summary>
    public static List<VyrocieRiadok> VyberNaDashboard(IEnumerable<VyrocieRiadok> vyrocia, int pocet)
    {
        static int Body(VyrocieRiadok v)
        {
            var body = v.Domace ? 4 : 0;
            if (v.Vyrocie.RokovV(v.AktualnyRok) is { } r and > 0)
            {
                body += r % 100 == 0 ? 4 : r % 50 == 0 ? 3 : r % 25 == 0 ? 2 : r % 10 == 0 ? 1 : 0;
            }

            return body + (v.Vyrocie.JeVlastne ? 5 : 0);
        }

        return vyrocia.OrderByDescending(Body).ThenByDescending(v => v.Vyrocie.Rok ?? 0).Take(pocet).ToList();
    }

    public static List<VyrocieRiadok> Vyrocia(DateOnly den, bool ajSkryte = false) =>
        Db.Vyrocia(den.Month, den.Day, Jazyk, ajSkryte).Select(v => new VyrocieRiadok(v, den.Year)).ToList();

    /// <summary>Stiahne výročia jedného dňa z Wikipédie a uloží ich. Vráti počet.</summary>
    public static async Task<int> StiahniDenAsync(int mesiac, int den, CancellationToken zrusenie = default)
    {
        var jazyk = Jazyk;
        var vyrocia = await VyrociaZWikipedie.StiahniAsync(Http.Value, jazyk, mesiac, den, zrusenie).ConfigureAwait(false);
        return Db.UlozStiahnute(jazyk, mesiac, den, vyrocia, VyrociaZWikipedie.AdresaStranky(jazyk, mesiac, den));
    }

    /// <summary>Doplní výročia pre dni, ktoré ešte nie sú stiahnuté (napr. dnes a týždeň dopredu). Vráti, či sa niečo stiahlo.</summary>
    public static async Task<bool> DoplnDniAsync(DateOnly od, int pocetDni, CancellationToken zrusenie = default)
    {
        if (!await Zamok.WaitAsync(0, zrusenie).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            var jazyk = Jazyk;
            var stiahnute = false;
            for (var i = 0; i < pocetDni; i++)
            {
                var den = od.AddDays(i);
                if (Db.Stiahnute(jazyk, den.Month, den.Day) is not null)
                {
                    continue;
                }

                await StiahniDenAsync(den.Month, den.Day, zrusenie).ConfigureAwait(false);
                stiahnute = true;
            }

            return stiahnute;
        }
        finally
        {
            Zamok.Release();
        }
    }

    /// <summary>Stiahne výročia všetkých dní roka (aj 29. februára). Hlási priebeh (hotové, spolu); chyby jednotlivých dní počíta.</summary>
    public static async Task<(int Dni, int Chyby)> StiahniRokAsync(bool ajStiahnute, IProgress<(int Hotove, int Spolu)>? priebeh, CancellationToken zrusenie)
    {
        await Zamok.WaitAsync(zrusenie).ConfigureAwait(false);
        try
        {
            var jazyk = Jazyk;
            var dni = Enumerable.Range(0, 366).Select(i => new DateOnly(2024, 1, 1).AddDays(i)).ToList();
            int hotove = 0, stiahnute = 0, chyby = 0;
            foreach (var den in dni)
            {
                zrusenie.ThrowIfCancellationRequested();
                if (ajStiahnute || Db.Stiahnute(jazyk, den.Month, den.Day) is null)
                {
                    try
                    {
                        await StiahniDenAsync(den.Month, den.Day, zrusenie).ConfigureAwait(false);
                        stiahnute++;
                        await Task.Delay(150, zrusenie).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        chyby++;
                    }
                }

                priebeh?.Report((++hotove, dni.Count));
            }

            return (stiahnute, chyby);
        }
        finally
        {
            Zamok.Release();
        }
    }
}
