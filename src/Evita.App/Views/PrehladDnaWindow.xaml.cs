using System.Windows;
using System.Windows.Controls;
using Evita.Models;
using Evita.Services;

namespace Evita.Views;

/// <summary>Prehľad dňa pri spustení: úlohy po termíne, dnešné udalosti a pripomienky.</summary>
public partial class PrehladDnaWindow : Window
{
    private readonly KalendarService _sluzba = new();

    public PrehladDnaWindow()
    {
        InitializeComponent();
        var dnes = DateTime.Today;
        Nadpis.Text = Sk.DlhyDatum(dnes);
        try
        {
            var meniny = Kalendarium.KalendariumDb.Spolocna.Meniny(DateOnly.FromDateTime(dnes));
            Meniny.Text = meniny.Count > 0 ? Preklad.F($"Meniny má {string.Join(", ", meniny)}") : "";
        }
        catch (Exception)
        {
            Meniny.Text = "";
        }

        ZobrazovatPriSpusteni.IsChecked = _sluzba.NacitajNastavenia().PrehladPriSpusteni;
        Nacitaj();
    }

    public PrehladDna Prehlad { get; private set; } = new(new(), new(), new());

    /// <summary>Používateľ chce prejsť do kalendára.</summary>
    public bool OtvoritKalendar { get; private set; }

    private void Nacitaj()
    {
        Prehlad = _sluzba.Prehlad(DateTime.Today);
        PoTermine.ItemsSource = Prehlad.PoTermine;
        Dnes.ItemsSource = Prehlad.Dnes;
        Pripomienky.ItemsSource = Prehlad.Pripomienky;
        NadpisPoTermine.Visibility = Prehlad.PoTermine.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NadpisDnes.Visibility = Prehlad.Dnes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NadpisPripomienky.Visibility = Prehlad.Pripomienky.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Nic.Visibility = Prehlad.JePrazdny ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Hotovo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: VyskytUdalosti { UdalostId: int id } })
        {
            _sluzba.NastavStav(id, StavUdalosti.Hotovo);
            Nacitaj();
        }
    }

    private void UlozVolbu()
    {
        var n = _sluzba.NacitajNastavenia();
        var zobrazovat = ZobrazovatPriSpusteni.IsChecked == true;
        if (n.PrehladPriSpusteni != zobrazovat)
        {
            _sluzba.UlozNastavenia(n with { PrehladPriSpusteni = zobrazovat });
        }
    }

    private void Kalendar_Click(object sender, RoutedEventArgs e)
    {
        OtvoritKalendar = true;
        Close();
    }

    private void Zavriet_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            UlozVolbu();
        }
        catch (Exception)
        {
            // voľba nie je dôležitá
        }

        base.OnClosed(e);
    }
}
