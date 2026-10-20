using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;
using Evita.ViewModels;

namespace Evita.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model = new();
    private readonly DispatcherTimer _pripomienky = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly HashSet<string> _oznamene = new();

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

    /// <summary>Pri spustení ukáže prehľad dňa (ak je čo ukázať a používateľ to nevypol).</summary>
    public void ZobrazPrehladDna()
    {
        try
        {
            var sluzba = new KalendarService();
            if (!sluzba.NacitajNastavenia().PrehladPriSpusteni || sluzba.Prehlad(DateTime.Today).JePrazdny || !IsVisible)
            {
                return;
            }

            var okno = new PrehladDnaWindow { Owner = this };
            okno.ShowDialog();
            if (okno.OtvoritKalendar)
            {
                OznacSekciu("kalendar");
            }
        }
        catch (Exception)
        {
            // prehľad nie je nevyhnutný
        }
    }

    /// <summary>Kým je program otvorený, každú minútu skontroluje udalosti, ktoré začnú do 15 minút.</summary>
    public void SpustiPripomienky()
    {
        _pripomienky.Tick += (_, _) => SkontrolujPripomienky();
        _pripomienky.Start();
        SkontrolujPripomienky();
    }

    private void SkontrolujPripomienky()
    {
        try
        {
            var sluzba = new KalendarService();
            if (!sluzba.NacitajNastavenia().Upozornenia)
            {
                return;
            }

            var teraz = DateTime.Now;
            var blizke = sluzba.Vyskyty(teraz.Date, teraz.Date, automaticke: false)
                .Where(v => !v.CelyDen && !v.ViacDni && v.Stav == StavUdalosti.Naplanovane)
                .Where(v => v.Zaciatok > teraz.AddMinutes(-1) && v.Zaciatok <= teraz.AddMinutes(15));
            foreach (var vyskyt in blizke)
            {
                if (_oznamene.Add(vyskyt.Kluc))
                {
                    var okno = new UpozornenieWindow(vyskyt);
                    okno.OtvoritKalendar += () =>
                    {
                        Activate();
                        OznacSekciu("kalendar");
                    };
                    okno.Show();
                }
            }
        }
        catch (Exception)
        {
            // pripomienky nesmú prerušiť prácu
        }
    }

    /// <summary>Po pravidelnej zálohe na pozadí obnoví stav záloh na obrazovke.</summary>
    public void PoAutomatickejZalohe() => _model.PoZmeneZaloh();

    private void PriZatvarani(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            _model.Poznamky.UlozAkTreba();
        }
        catch (Exception)
        {
            // poznámka sa neuloží – program sa aj tak zatvorí
        }

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
