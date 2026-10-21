using Kalendarium;

namespace Evita.Services;

/// <summary>Pracovné dni podľa kalendára: bez sobôt, nedieľ a dní pracovného pokoja (sviatky z Kalendária).</summary>
public static class PracovneDni
{
    /// <summary>Je deň pracovný?</summary>
    public static bool JePracovny(DateTime den)
    {
        if (den.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        try
        {
            var datum = DateOnly.FromDateTime(den);
            return !KalendariumDb.Spolocna.Sviatky(den.Year).Any(s => s.DenPracovnehoPokoja && s.Datum == datum);
        }
        catch (Exception)
        {
            // bez Kalendária sa berú do úvahy len víkendy
            return true;
        }
    }

    /// <summary>Daný deň, ak je pracovný, inak najbližší nasledujúci pracovný deň.</summary>
    public static DateTime NajblizsiPracovny(DateTime den)
    {
        var vysledok = den.Date;
        for (var i = 0; i < 30 && !JePracovny(vysledok); i++)
        {
            vysledok = vysledok.AddDays(1);
        }

        return vysledok;
    }
}
