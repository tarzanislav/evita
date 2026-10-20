using System.Windows;
using System.Windows.Controls;
using Evita.Pomocne;
using Evita.Services;
using Evita.ViewModels;

namespace Evita.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model = new();

    public MainWindow()
    {
        DataContext = _model;
        InitializeComponent();
        _model.ZiadostOSekciu += OznacSekciu;
        _model.ZiadostOAktualizaciu += verzia => new AktualizaciaWindow(verzia) { Owner = this }.ShowDialog();
        Loaded += (_, _) => Navigacia.SelectedIndex = 0;
        Closing += PriZatvarani;
    }

    /// <summary>Pri spustení zistí, či je na GitHube nová verzia, a ak áno, ponúkne aktualizáciu.</summary>
    public async Task SkontrolujAktualizaciuAsync()
    {
        try
        {
            var sluzba = new AktualizaciaService();
            if (!sluzba.KontrolovatPriSpusteni())
            {
                return;
            }

            var verzia = await sluzba.SkontrolujAsync();
            if (verzia is not null && IsVisible)
            {
                _model.ZobrazitAktualizaciu(verzia);
            }
        }
        catch (Exception)
        {
            // bez internetu alebo pri chybe GitHubu sa pri spustení nič nehlási
        }
    }

    /// <summary>Po pravidelnej zálohe na pozadí obnoví stav záloh na obrazovke.</summary>
    public void PoAutomatickejZalohe() => _model.PoZmeneZaloh();

    private void PriZatvarani(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            var zalohy = new ZalohaService();
            if (zalohy.JeCasNaZalohu(zalohy.NacitajNastavenia()))
            {
                Dialogy.Cakaj(() => zalohy.AutomatickaAkTreba());
            }

            new DiagnostikaService().Optimalizuj();
        }
        catch (Exception)
        {
            // pri zatváraní programu chyby len ignorujeme – záloha sa skúsi pri ďalšom spustení
        }
    }

    private void Navigacia_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Navigacia.SelectedItem is ListBoxItem { Tag: string sekcia })
        {
            _model.Prejdi(sekcia);
        }
    }

    /// <summary>Prepne menu na danú sekciu (napr. z dashboardu do evidencie).</summary>
    public void OznacSekciu(string sekcia)
    {
        foreach (var polozka in Navigacia.Items.OfType<ListBoxItem>())
        {
            if (polozka.Tag as string == sekcia)
            {
                Navigacia.SelectedItem = polozka;
                return;
            }
        }
    }
}
