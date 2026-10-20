using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Evita.Pomocne;

/// <summary>true → skryté, false → viditeľné.</summary>
public sealed class InverzBoolNaViditelnost : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

/// <summary>Obráti hodnotu bool.</summary>
public sealed class InverzBool : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Neprázdny text → viditeľné, prázdny alebo null → skryté (s parametrom „opacne“ naopak).</summary>
public sealed class NieJePrazdne : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is string text && text.Length > 0) != (parameter as string == "opacne") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Číslo 0 → viditeľné (napr. text „žiadne položky“), inak skryté (s parametrom „opacne“ naopak).</summary>
public sealed class NulaNaViditelnost : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is 0) != (parameter as string == "opacne") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Farba v tvare #RRGGBB → štetec (farby typov udalostí v kalendári).</summary>
public sealed class FarbaNaStetec : IValueConverter
{
    private static readonly Dictionary<string, System.Windows.Media.SolidColorBrush> Vyrovnavacia = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? "#7F8C8D";
        if (parameter is string priehladnost && byte.TryParse(priehladnost, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var alfa))
        {
            text = "#" + alfa.ToString("X2") + text.TrimStart('#');
        }

        lock (Vyrovnavacia)
        {
            if (Vyrovnavacia.TryGetValue(text, out var stetec))
            {
                return stetec;
            }

            try
            {
                stetec = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(text));
            }
            catch (FormatException)
            {
                stetec = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
            }

            stetec.Freeze();
            Vyrovnavacia[text] = stetec;
            return stetec;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Hodnoty (napr. kurz za posledné dni) → body malého grafu 60 × 18.</summary>
public sealed class HodnotyNaKrivku : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var body = new System.Windows.Media.PointCollection();
        if (value is not IReadOnlyList<decimal> hodnoty || hodnoty.Count < 2)
        {
            return body;
        }

        const double sirka = 60, vyska = 18;
        var min = hodnoty.Min();
        var max = hodnoty.Max();
        var rozsah = max - min;
        for (var i = 0; i < hodnoty.Count; i++)
        {
            var x = sirka * i / (hodnoty.Count - 1);
            var y = rozsah == 0 ? vyska / 2 : vyska - (double)((hodnoty[i] - min) / rozsah) * vyska;
            body.Add(new Point(x, y));
        }

        body.Freeze();
        return body;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
