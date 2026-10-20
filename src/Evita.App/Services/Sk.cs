using System.Globalization;
using System.Text;

namespace Evita.Services;

/// <summary>Slovenské formátovanie, radenie a porovnávanie textov.</summary>
public static class Sk
{
    public static readonly CultureInfo Kultura = new("sk-SK");

    /// <summary>Abecedné radenie podľa slovenských pravidiel (ch za h, á pri a …).</summary>
    public static readonly StringComparer Porovnanie = StringComparer.Create(Kultura, CompareOptions.IgnoreCase);

    public static string Datum(DateTime? datum) =>
        datum.HasValue ? datum.Value.ToString("d. M. yyyy", Kultura) : "";

    public static string DatumCas(DateTime datum) => datum.ToString("d. M. yyyy H:mm", Kultura);

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

        var cisty = text.Replace("€", "").Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace(",", ".").Trim();
        if (decimal.TryParse(cisty, NumberStyles.Number, CultureInfo.InvariantCulture, out var hodnota) && hodnota >= 0)
        {
            suma = Math.Round(hodnota, 2);
            return true;
        }

        return false;
    }

    public static string? Prazdne(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
