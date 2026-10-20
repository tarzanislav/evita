using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.Views;

/// <summary>Mobil ako skener: QR kód s adresou stránky pre mobil a zoznam kódov prijatých z mobilu.</summary>
public partial class MobilWindow : Window
{
    private static MobilWindow? _otvorene;

    private readonly MobilnySkener _server;
    private readonly ObservableCollection<PrijatyKod> _prijate = new();

    public sealed record PrijatyKod(string Text, string Popis);

    public MobilWindow()
    {
        InitializeComponent();
        _server = new MobilnySkener(Dispatcher);
        Prijate.ItemsSource = _prijate;
        _server.Pripojene += () => Stav.Text = Preklad.T("Mobil je pripojený. Odfoťte kód na zariadení.");
        _server.Prijate += (kod, vysledok) =>
        {
            _prijate.Insert(0, new PrijatyKod(vysledok.Text, $"{Sk.Cas(DateTime.Now)} · {kod}"));
            while (_prijate.Count > 30)
            {
                _prijate.RemoveAt(_prijate.Count - 1);
            }
        };
        Loaded += (_, _) => Spusti();
        Closed += (_, _) =>
        {
            _server.Dispose();
            if (_otvorene == this)
            {
                _otvorene = null;
            }
        };
    }

    /// <summary>Otvorí okno (ak je už otvorené, len ho zobrazí).</summary>
    public static void Otvor()
    {
        if (Dialogy.TichyRezim)
        {
            Dialogy.TicheHlasenia.Add("[Mobil]");
            return;
        }

        if (_otvorene is { } otvorene)
        {
            otvorene.Activate();
            return;
        }

        _otvorene = new MobilWindow { Owner = Dialogy.Vlastnik };
        _otvorene.Show();
    }

    private void Spusti()
    {
        try
        {
            _server.Spusti(SkenovanieService.PortMobilu());
            var adresy = MobilnySkener.AdresyPocitaca();
            if (adresy.Count == 0)
            {
                Stav.Text = Preklad.T("Počítač nie je pripojený k sieti. Pripojte ho k Wi-Fi alebo sieťovým káblom.");
                return;
            }

            Adresy.ItemsSource = adresy;
            Adresy.Visibility = adresy.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            Adresy.SelectedIndex = 0;
            Stav.Text = Preklad.T("Čakám na mobil…");
        }
        catch (Exception ex)
        {
            Stav.Text = ex is EvitaException ? ex.Message : Preklad.F($"Spojenie pre mobil sa nepodarilo spustiť: {ex.Message}");
        }
    }

    private void Adresy_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Adresy.SelectedItem is string ip && _server.Bezi)
        {
            var adresa = _server.Adresa(ip);
            Adresa.Text = adresa;
            Qr.Source = CiarovyKod.ObrazokQr(adresa, 240);
        }
    }

    private void Zavriet_Click(object sender, RoutedEventArgs e) => Close();
}
