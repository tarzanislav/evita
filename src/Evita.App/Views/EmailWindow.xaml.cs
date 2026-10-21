using System.Windows;
using System.Windows.Controls;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.Views;

/// <summary>Odoslanie zostavy e-mailom priamo z EviTA (SMTP): adresát, predmet, text a prílohy z pamäte.</summary>
public partial class EmailWindow : Window
{
    private readonly IReadOnlyList<SuborVystupu> _subory;
    private EmailNastavenia _nastavenia;

    public EmailWindow(IReadOnlyList<SuborVystupu> subory, string nazov)
    {
        _subory = subory;
        _nastavenia = EmailNastavenia.Nacitaj();
        InitializeComponent();
        Komu.Text = _nastavenia.PoslednyAdresat;
        Predmet.Text = nazov;
        Text.Text = EmailService.PredvolenyText(nazov);
        KopiaSebe.IsChecked = _nastavenia.KopiaSebe;
        Prilohy.ItemsSource = subory.Select(s => $"{s.Nazov} ({Velkost(s.Data.Length)})").ToList();
        UkazOdkial();
        Loaded += (_, _) => (Komu.Text.Length == 0 ? Komu : Odoslat as Control).Focus();
    }

    private static string Velkost(long bajty) => bajty < 1024 * 1024 ? $"{Math.Max(1, bajty / 1024)} kB" : $"{bajty / 1024.0 / 1024.0:0.0} MB";

    private void UkazOdkial() =>
        Odkial.Text = Preklad.F($"Odosielateľ: {_nastavenia.Odosielatel} · server {_nastavenia.Server}:{_nastavenia.Port}");

    /// <summary>Samotest: adresát.</summary>
    public void NastavKomu(string komu) => Komu.Text = komu;

    public string StavText => Stav.Text;

    private void Nastavenie_Click(object sender, RoutedEventArgs e)
    {
        if (new EmailNastaveniaWindow { Owner = this }.ShowDialog() == true)
        {
            _nastavenia = EmailNastavenia.Nacitaj();
            UkazOdkial();
        }
    }

    private async void Odoslat_Click(object sender, RoutedEventArgs e)
    {
        if (await OdoslatAsync())
        {
            DialogResult = true;
        }
    }

    /// <summary>Odošle e-mail; pri chybe ju ukáže v okne a vráti false.</summary>
    public async Task<bool> OdoslatAsync()
    {
        var (platne, neplatne) = EmailNastavenia.Adresy(Komu.Text);
        if (neplatne.Count > 0 || platne.Count == 0)
        {
            UkazStav(neplatne.Count > 0
                ? Preklad.F($"Neplatná e-mailová adresa: {string.Join(", ", neplatne)}")
                : Preklad.T("Zadajte, komu sa má e-mail poslať."), false);
            Komu.Focus();
            return false;
        }

        IsEnabled = false;
        UkazStav(Preklad.T("Odosiela sa…"), null);
        try
        {
            var kopia = KopiaSebe.IsChecked == true && !platne.Contains(_nastavenia.Odosielatel, StringComparer.OrdinalIgnoreCase)
                ? new[] { _nastavenia.Odosielatel }
                : Array.Empty<string>();
            await SmtpKlient.PosliAsync(_nastavenia, EmailNastavenia.Heslo(), new SpravaEmailu(_nastavenia.Odosielatel, _nastavenia.MenoOdosielatela,
                platne, kopia, Predmet.Text.Trim(), Text.Text, _subory));
            (_nastavenia with { PoslednyAdresat = string.Join(", ", platne) }).Uloz();
            UkazStav(Preklad.F($"E-mail je odoslaný: {string.Join(", ", platne)}"), true);
            return true;
        }
        catch (EvitaException ex)
        {
            UkazStav(ex.Message, false);
            return false;
        }
        catch (Exception ex)
        {
            UkazStav(ex.GetBaseException().Message, false);
            return false;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void UkazStav(string text, bool? uspech)
    {
        Stav.Text = text;
        Stav.SetResourceReference(TextBlock.ForegroundProperty, uspech switch
        {
            true => "SystemFillColorSuccessBrush",
            false => "SystemFillColorCriticalBrush",
            null => "TextFillColorSecondaryBrush",
        });
    }
}
