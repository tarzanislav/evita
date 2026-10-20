using System.Windows;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.Views;

/// <summary>Ponuka novej verzie: novinky, stiahnutie, záloha a spustenie inštalácie.</summary>
public partial class AktualizaciaWindow : Window
{
    private readonly DostupnaVerzia _verzia;
    private readonly AktualizaciaService _sluzba = new();
    private CancellationTokenSource? _zrusenie;
    private bool _prebieha;

    public AktualizaciaWindow(DostupnaVerzia verzia)
    {
        InitializeComponent();
        _verzia = verzia;
        Nadpis.Text = Preklad.F($"Je dostupná nová verzia EviTA {verzia.Verzia}");
        Podnadpis.Text = Preklad.F($"Máte nainštalovanú verziu {AktualizaciaService.AktualnaVerzia}") +
                         (verzia.Zverejnene is { } datum ? Preklad.F($". Nová verzia je zverejnená {Sk.DatumCas(datum)}.") : ".");
        Novinky.Text = UpravNovinky(verzia.Novinky);
        Closing += (_, e) =>
        {
            if (_prebieha)
            {
                _zrusenie?.Cancel();
            }
        };
    }

    /// <summary>Novinky z GitHubu sú vo formáte Markdown – pre zobrazenie odstránime značky.</summary>
    public static string UpravNovinky(string text)
    {
        var riadky = text.Replace("\r", "").Split('\n')
            .Select(r => r.TrimStart('#', ' ').Replace("**", ""))
            .Where(r => !r.StartsWith("Co-Authored-By", StringComparison.OrdinalIgnoreCase));
        var vysledok = string.Join(Environment.NewLine, riadky).Trim();
        return vysledok.Length == 0 ? Preklad.T("Bez popisu zmien.") : vysledok;
    }

    private async void Aktualizovat_Click(object sender, RoutedEventArgs e)
    {
        _prebieha = true;
        _zrusenie = new CancellationTokenSource();
        Aktualizovat.IsEnabled = false;
        Neskor.Content = Preklad.T("Zrušiť");
        PanelPriebeh.Visibility = Visibility.Visible;

        try
        {
            Stav.Text = Preklad.T("Sťahujem novú verziu…");
            var priebeh = new Progress<double>(p => Priebeh.Value = p * 100);
            var instalator = await _sluzba.StiahniAsync(_verzia, priebeh, _zrusenie.Token);

            Stav.Text = Preklad.T("Zálohujem databázu…");
            Priebeh.IsIndeterminate = true;
            try
            {
                await Task.Run(() => new ZalohaService().Zalohuj(
                    TypZalohy.PredAktualizaciou, poznamka: Preklad.F($"Automatická záloha pred aktualizáciou na verziu {_verzia.Verzia}")));
            }
            catch (EvitaException chyba)
            {
                if (!Dialogy.Potvrd(Preklad.F($"{chyba.Message}\n\nPokračovať v aktualizácii aj bez zálohy?")))
                {
                    Obnov();
                    return;
                }
            }

            Stav.Text = Preklad.T("Spúšťam inštaláciu…");
            if (!_sluzba.SpustiInstalator(instalator))
            {
                Dialogy.Upozornenie(Preklad.T("Aktualizácia sa nespustila – Windows nedostal povolenie na inštaláciu. Môžete to skúsiť znova."));
                Obnov();
                return;
            }

            _prebieha = false;
            DialogResult = true;
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            Obnov();
        }
        catch (EvitaException chyba)
        {
            Dialogy.Upozornenie(chyba.Message);
            Obnov();
        }
    }

    private void Neskor_Click(object sender, RoutedEventArgs e)
    {
        if (_prebieha)
        {
            _zrusenie?.Cancel();
            return;
        }

        DialogResult = false;
    }

    private void Obnov()
    {
        _prebieha = false;
        Aktualizovat.IsEnabled = true;
        Neskor.Content = Preklad.T("Neskôr");
        Priebeh.IsIndeterminate = false;
        Priebeh.Value = 0;
        PanelPriebeh.Visibility = Visibility.Collapsed;
    }
}
