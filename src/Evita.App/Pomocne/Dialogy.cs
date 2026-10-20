using System.Windows;
using System.Windows.Input;

namespace Evita.Pomocne;

/// <summary>Jednotné hlásenia a otázky pre používateľa.</summary>
public static class Dialogy
{
    private const string Titulok = "EviTA";

    public static Window? Vlastnik
    {
        get
        {
            var aktivne = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (aktivne is not null)
            {
                return aktivne;
            }

            var hlavne = Application.Current?.MainWindow;
            return hlavne is { IsVisible: true } ? hlavne : null;
        }
    }

    public static void Info(string sprava) => Ukaz(sprava, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Upozornenie(string sprava) => Ukaz(sprava, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Chyba(string sprava, Exception? ex = null)
    {
        var text = ex is null ? sprava : $"{sprava}\n\nPodrobnosti: {ex.GetBaseException().Message}";
        Ukaz(text, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static bool Potvrd(string otazka) =>
        Ukaz(otazka, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>Zobrazí okno s jedným textovým poľom. Vráti null, ak používateľ zrušil.</summary>
    public static string? Vstup(string titulok, string otazka, string predvolene = "", bool viacRiadkov = false)
    {
        var okno = new Views.VstupWindow(titulok, otazka, predvolene, viacRiadkov) { Owner = Vlastnik };
        return okno.ShowDialog() == true ? okno.Text : null;
    }

    /// <summary>Vykoná akciu s kurzorom „čakajte“.</summary>
    public static void Cakaj(Action akcia)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            akcia();
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private static MessageBoxResult Ukaz(string sprava, MessageBoxButton tlacidla, MessageBoxImage ikona)
    {
        var vlastnik = Vlastnik;
        return vlastnik is null
            ? MessageBox.Show(sprava, Titulok, tlacidla, ikona)
            : MessageBox.Show(vlastnik, sprava, Titulok, tlacidla, ikona);
    }
}
