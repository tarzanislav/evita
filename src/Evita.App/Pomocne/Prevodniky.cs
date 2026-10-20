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

/// <summary>Neprázdny text → viditeľné, prázdny alebo null → skryté.</summary>
public sealed class NieJePrazdne : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text && text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Číslo 0 → viditeľné (napr. text „žiadne položky“), inak skryté.</summary>
public sealed class NulaNaViditelnost : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

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
