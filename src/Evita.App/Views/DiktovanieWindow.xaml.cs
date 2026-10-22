using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.Views;

/// <summary>Nastavenie diktovania: hlasové písanie Windows alebo prepis v počítači (stiahnutie a výber modelu).</summary>
public partial class DiktovanieWindow : Window
{
    /// <summary>Riadok zoznamu modelov.</summary>
    public sealed class VolbaModelu(ModelHlasu model) : INotifyPropertyChanged
    {
        private bool _vybrany;

        public ModelHlasu Model { get; } = model;
        public string Nazov => Model.Nazov;
        public string Velkost => $"· {Model.VelkostMb} MB";
        public string Popis => Model.Popis;
        public string Stav => Model.JeStiahnuty ? Preklad.T("stiahnutý") : "";

        public bool Vybrany
        {
            get => _vybrany;
            set
            {
                _vybrany = value;
                Zmena();
            }
        }

        public void Obnov() => Zmena(nameof(Stav));

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Zmena([CallerMemberName] string? nazov = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nazov));
    }

    private readonly List<VolbaModelu> _modely;
    private CancellationTokenSource? _stahovanie;

    public DiktovanieWindow()
    {
        InitializeComponent();
        var vybrany = Diktovanie.Model;
        _modely = Diktovanie.Modely.Select(m => new VolbaModelu(m) { Vybrany = m == vybrany }).ToList();
        Modely.ItemsSource = _modely;
        foreach (var m in _modely)
        {
            m.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(VolbaModelu.Vybrany))
                {
                    Aktualizuj();
                }
            };
        }

        (Diktovanie.Sposob == SposobDiktovania.Offline ? Offline : Windows).IsChecked = true;
        Prekazka.Text = PrepisHlasu.Prekazka() ?? "";
        SlovencinaInfo.Text = HlasovePisanie.Slovencina is null
            ? Preklad.T("Slovenská klávesnica v systéme nie je – hlasové písanie by rozpoznávalo iný jazyk.")
            : Preklad.T("Slovenčina je v systéme nastavená.");
        SlovencinaInfo.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            HlasovePisanie.Slovencina is null ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush");
        Closing += (_, _) => _stahovanie?.Cancel();
        Aktualizuj();
    }

    public ModelHlasu VybranyModel => _modely.FirstOrDefault(m => m.Vybrany)?.Model ?? Diktovanie.Modely[0];

    private void Aktualizuj()
    {
        SekciaModelov.IsEnabled = Offline.IsChecked == true && PrepisHlasu.Prekazka() is null;
        var model = VybranyModel;
        Stiahnut.IsEnabled = _stahovanie is null && !model.JeStiahnuty;
        Vymazat.IsEnabled = _stahovanie is null && model.JeStiahnuty;
    }

    private void Sposob_Click(object sender, RoutedEventArgs e) => Aktualizuj();

    private async void Stiahnut_Click(object sender, RoutedEventArgs e) => await StiahnutAsync();

    /// <summary>Stiahne vybraný model (s priebehom, dá sa zrušiť zatvorením okna).</summary>
    public async Task StiahnutAsync()
    {
        var model = VybranyModel;
        _stahovanie = new CancellationTokenSource();
        Priebeh.Visibility = Visibility.Visible;
        Priebeh.Value = 0;
        StavStahovania.Text = Preklad.F($"Sťahuje sa {model.Nazov} ({model.VelkostMb} MB)…");
        Aktualizuj();
        try
        {
            var priebeh = new Progress<double>(p =>
            {
                Priebeh.Value = p;
                StavStahovania.Text = Preklad.F($"Sťahuje sa {model.Nazov}: {p * 100:0} % z {model.VelkostMb} MB");
            });
            await Diktovanie.StiahniAsync(model, priebeh, _stahovanie.Token);
            StavStahovania.Text = Preklad.T("Model je stiahnutý a overený (SHA-256). Diktovanie funguje aj bez internetu.");
        }
        catch (OperationCanceledException)
        {
            StavStahovania.Text = Preklad.T("Sťahovanie je zrušené.");
        }
        catch (EvitaException ex)
        {
            StavStahovania.Text = ex.Message;
        }
        catch (Exception ex)
        {
            StavStahovania.Text = ex.GetBaseException().Message;
        }
        finally
        {
            _stahovanie = null;
            Priebeh.Visibility = Visibility.Collapsed;
            _modely.ForEach(m => m.Obnov());
            Aktualizuj();
        }
    }

    private void Vymazat_Click(object sender, RoutedEventArgs e)
    {
        var model = VybranyModel;
        if (!Dialogy.Potvrd(Preklad.F($"Vymazať model {model.Nazov} ({model.VelkostMb} MB) z počítača?")))
        {
            return;
        }

        try
        {
            Diktovanie.Vymaz(model);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Model sa nepodarilo vymazať."), ex);
        }

        _modely.ForEach(m => m.Obnov());
        Aktualizuj();
    }

    private void Ulozit_Click(object sender, RoutedEventArgs e)
    {
        var offline = Offline.IsChecked == true;
        if (offline && PrepisHlasu.Prekazka() is { } prekazka)
        {
            Dialogy.Upozornenie(prekazka);
            return;
        }

        Diktovanie.Sposob = offline ? SposobDiktovania.Offline : SposobDiktovania.Windows;
        if (Diktovanie.Model != VybranyModel)
        {
            PrepisHlasu.Zavri();
            Diktovanie.Model = VybranyModel;
        }

        if (offline && !VybranyModel.JeStiahnuty)
        {
            Dialogy.Info(Preklad.T("Vybraný model ešte nie je stiahnutý – stiahnite ho tlačidlom Stiahnuť vybraný model, inak diktovanie nebude fungovať."));
        }

        DialogResult = true;
    }
}
