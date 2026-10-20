using System.Globalization;
using System.Text;

namespace Evita.Services;

/// <summary>Formátovanie podľa jazyka programu, radenie a porovnávanie textov (pôvodne len slovenské – odtiaľ názov).</summary>
public static class Sk
{
    private static StringComparer _porovnanie = StringComparer.Create(new CultureInfo("sk-SK"), CompareOptions.IgnoreCase);

    /// <summary>Kultúra aktuálneho jazyka programu (formát čísel, názvy dní a mesiacov).</summary>
    public static CultureInfo Kultura => Preklad.Kultura;

    /// <summary>Abecedné radenie podľa pravidiel jazyka programu (v slovenčine ch za h, á pri a …).</summary>
    public static StringComparer Porovnanie => _porovnanie;

    internal static void ZmenaJazyka() => _porovnanie = StringComparer.Create(Preklad.Kultura, CompareOptions.IgnoreCase);

    public static string Datum(DateTime? datum) =>
        datum.HasValue ? datum.Value.ToString(Preklad.Aktualny.Datum, Kultura) : "";

    public static string DatumCas(DateTime datum) => datum.ToString(Preklad.Aktualny.DatumCas, Kultura);

    /// <summary>Deň a mesiac bez roka (napr. 20. 10.).</summary>
    public static string KratkyDatum(DateTime? datum) =>
        datum.HasValue ? datum.Value.ToString(Preklad.Aktualny.KratkyDatum, Kultura) : "";

    /// <summary>Deň v týždni (skratka), deň a mesiac – napr. „po 20. 10.“.</summary>
    public static string DenAKratkyDatum(DateTime datum) => $"{datum.ToString("ddd", Kultura)} {KratkyDatum(datum)}";

    /// <summary>Dlhý dátum s dňom v týždni a veľkým začiatočným písmenom (napr. „Utorok 20. októbra 2026“).</summary>
    public static string DlhyDatum(DateTime datum)
    {
        var text = Preklad.JeSlovencina ? datum.ToString("dddd d. MMMM yyyy", Kultura) : datum.ToString("D", Kultura);
        return VelkePrve(text);
    }

    /// <summary>Deň v týždni, deň a mesiac bez roka (napr. „nedeľa 1. novembra“).</summary>
    public static string DenAMesiac(DateTime datum) =>
        Preklad.JeSlovencina
            ? datum.ToString("dddd d. MMMM", Kultura)
            : datum.ToString("dddd", Kultura) + (Preklad.Kod is "zh" or "hu" ? " " : ", ") + datum.ToString(Kultura.DateTimeFormat.MonthDayPattern, Kultura);

    /// <summary>Mesiac a rok s veľkým začiatočným písmenom (napr. „Október 2026“).</summary>
    public static string MesiacRok(DateTime datum) =>
        VelkePrve(datum.ToString(Preklad.JeSlovencina ? "MMMM yyyy" : "Y", Kultura));

    public static string VelkePrve(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], Kultura) + text[1..];

    public static string Cas(DateTime cas) => cas.ToString("t", Kultura);

    public static string Suma(decimal? suma) =>
        suma.HasValue ? suma.Value.ToString("N2", Kultura) + " €" : "";

    /// <summary>Text bez diakritiky, malými písmenami – na vyhľadávanie „notebook“ = „Notebook“, „ucebna“ = „Učebňa“.</summary>
    public static string BezDiakritiky(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var rozlozeny = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(rozlozeny.Length);
        foreach (var znak in rozlozeny)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(znak) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(znak));
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Prečíta sumu napísanú s čiarkou aj bodkou, s medzerami aj so znakom €.</summary>
    public static bool SkusPrecitatSumu(string? text, out decimal? suma)
    {
        suma = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var cisty = new string(text.Where(z => !char.IsWhiteSpace(z) && z != '\u202F' && z != '\'' && z != '€').ToArray());
        // 1,234.50 aj 1.234,50 – desatinný je posledný oddeľovač, ostatné oddeľujú tisíce
        var posledny = Math.Max(cisty.LastIndexOf(','), cisty.LastIndexOf('.'));
        if (posledny >= 0)
        {
            var celaCast = cisty[..posledny].Replace(",", "").Replace(".", "");
            var desatinna = cisty[(posledny + 1)..];
            cisty = desatinna.Length == 3 && celaCast.Length > 0 && cisty.Count(z => z is ',' or '.') > 1
                ? celaCast + desatinna
                : celaCast + "." + desatinna;
        }

        if (decimal.TryParse(cisty, NumberStyles.Number, CultureInfo.InvariantCulture, out var hodnota) && hodnota >= 0)
        {
            suma = Math.Round(hodnota, 2);
            return true;
        }

        return false;
    }

    public static string? Prazdne(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
