using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.Views;

/// <summary>Skenovanie kódov kamerou počítača. Prečítaný kód sa spracuje ako kód z čítačky.</summary>
public partial class KameraWindow : Window
{
    private static KameraWindow? _otvorene;

    private readonly KameraSkener _skener = new();
    private WriteableBitmap? _bitmapa;
    private string? _poslednyKod;
    private DateTime _poslednyCas = DateTime.MinValue;
    private bool _nacitavam = true;

    public KameraWindow()
    {
        InitializeComponent();
        _skener.Snimka += PriSnimke;
        _skener.Kod += kod => Dispatcher.InvokeAsync(() => PriKode(kod));
        Loaded += async (_, _) => await NacitajKameryAsync();
        Closed += async (_, _) =>
        {
            if (_otvorene == this)
            {
                _otvorene = null;
            }

            await _skener.ZastavAsync();
        };
    }

    /// <summary>Otvorí okno kamery (ak je už otvorené, len ho zobrazí).</summary>
    public static void Otvor()
    {
        if (Dialogy.TichyRezim)
        {
            Dialogy.TicheHlasenia.Add("[Kamera]");
            return;
        }

        if (_otvorene is { } otvorene)
        {
            otvorene.Activate();
            return;
        }

        _otvorene = new KameraWindow { Owner = Dialogy.Vlastnik };
        _otvorene.Show();
    }

    private async Task NacitajKameryAsync()
    {
        try
        {
            var kamery = await KameraSkener.ZoznamAsync();
            if (kamery.Count == 0)
            {
                Stav.Text = Preklad.T("Počítač nemá kameru (alebo je vypnutá).");
                Kamery.Visibility = Visibility.Collapsed;
                return;
            }

            Kamery.ItemsSource = kamery;
            string? ulozena = null;
            try
            {
                using var db = Data.Databaza.Otvor();
                ulozena = Data.Databaza.Citaj(db, SkenovanieService.KlucKamera);
            }
            catch (Exception)
            {
                // predvolená kamera
            }

            // kamera zozadu (tablet) je na kódy lepšia ako predná
            Kamery.SelectedItem = kamery.FirstOrDefault(k => k.Id == ulozena)
                                  ?? kamery.FirstOrDefault(k => k.Nazov.Contains("back", StringComparison.OrdinalIgnoreCase)
                                                                || k.Nazov.Contains("rear", StringComparison.OrdinalIgnoreCase))
                                  ?? kamery[0];
            Kamery.Visibility = kamery.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            _nacitavam = false;
            await SpustiAsync();
        }
        catch (Exception ex)
        {
            Stav.Text = ex is EvitaException ? ex.Message : Preklad.F($"Kameru sa nepodarilo zapnúť: {ex.Message}");
        }
    }

    private async Task SpustiAsync()
    {
        if (Kamery.SelectedItem is not KameraInfo kamera)
        {
            return;
        }

        Stav.Text = Preklad.T("Zapínam kameru…");
        Stav.Visibility = Visibility.Visible;
        try
        {
            await _skener.SpustiAsync(kamera.Id);
            SkenovanieService.UlozNastavenie(SkenovanieService.KlucKamera, kamera.Id);
        }
        catch (Exception ex)
        {
            Stav.Text = ex is EvitaException ? ex.Message : Preklad.F($"Kameru sa nepodarilo zapnúť: {ex.Message}");
        }
    }

    private async void Kamery_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_nacitavam)
        {
            await SpustiAsync();
        }
    }

    private void PriSnimke(SnimkaKamery snimka)
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (_bitmapa is null || _bitmapa.PixelWidth != snimka.Sirka || _bitmapa.PixelHeight != snimka.Vyska)
                {
                    _bitmapa = new WriteableBitmap(snimka.Sirka, snimka.Vyska, 96, 96, PixelFormats.Bgr32, null);
                    Obraz.Source = _bitmapa;
                }

                _bitmapa.WritePixels(new Int32Rect(0, 0, snimka.Sirka, snimka.Vyska), snimka.Data, snimka.Sirka * 4, 0);
                Stav.Visibility = Visibility.Collapsed;
            }
            catch (Exception)
            {
                // snímka sa nezobrazí
            }
            finally
            {
                _skener.SnimkaZobrazena();
            }
        });
    }

    private void PriKode(string kod)
    {
        // rovnaký kód pred kamerou sa nespracuje znova hneď za sebou
        if (kod == _poslednyKod && (DateTime.Now - _poslednyCas).TotalSeconds < 3)
        {
            _poslednyCas = DateTime.Now;
            return;
        }

        _poslednyKod = kod;
        _poslednyCas = DateTime.Now;
        var vysledok = SkenerCentrum.Odosli(Sken.Z(kod, ZdrojSkenu.Kamera));
        VysledokText.Text = vysledok.Text;
        Vysledok.Background = (Brush)FindResource(vysledok.Uspech ? "SystemFillColorSuccessBackgroundBrush" : "SystemFillColorCautionBackgroundBrush");
    }

    private void Zavriet_Click(object sender, RoutedEventArgs e) => Close();
}
