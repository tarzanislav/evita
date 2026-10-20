using System.Windows;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.Views;

public partial class NastaveniaKalendaraWindow : Window
{
    private readonly KalendarService _sluzba = new();

    public NastaveniaKalendaraWindow()
    {
        InitializeComponent();
        var n = _sluzba.NacitajNastavenia();
        Zaruky.IsChecked = n.Zaruky;
        Revizie.IsChecked = n.Revizie;
        Zalohy.IsChecked = n.Zalohy;
        Inventura.IsChecked = n.Inventura;
        DatumInventury.SelectedDate = n.DatumInventury is { } d ? new DateTime(DateTime.Today.Year, d.Month, d.Day) : null;
        Prehlad.IsChecked = n.PrehladPriSpusteni;
        Upozornenia.IsChecked = n.Upozornenia;
    }

    private void Ulozit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _sluzba.UlozNastavenia(new NastaveniaKalendara(
                Zaruky.IsChecked == true,
                Revizie.IsChecked == true,
                Zalohy.IsChecked == true,
                Inventura.IsChecked == true,
                DatumInventury.SelectedDate,
                Prehlad.IsChecked == true,
                Upozornenia.IsChecked == true));
            try
            {
                DialogResult = true;
            }
            catch (InvalidOperationException)
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            Dialogy.Chyba("Nastavenia sa nepodarilo uložiť.", ex);
        }
    }
}
