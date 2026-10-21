using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.Views;

/// <summary>Nastavenie posielania PDF e-mailom: poštový program počítača alebo priamo cez SMTP (s heslom šifrovaným pre Windows).</summary>
public partial class EmailNastaveniaWindow : Window
{
    private readonly EmailNastavenia _povodne;
    private bool _nacitavam = true;

    public EmailNastaveniaWindow()
    {
        InitializeComponent();
        _povodne = EmailNastavenia.Nacitaj();
        Vypln(_povodne);
        _nacitavam = false;
    }

    /// <summary>Vyplní okno (aj samotest).</summary>
    public void Vypln(EmailNastavenia n)
    {
        _nacitavam = true;
        (n.Sposob == SposobOdoslania.Smtp ? Smtp : PostovyProgram).IsChecked = true;
        Server.Text = n.Server;
        Port.Text = n.Port.ToString(CultureInfo.InvariantCulture);
        Zabezpecenie.SelectedItem = Zabezpecenie.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == n.Zabezpecenie.ToString());
        Odosielatel.Text = n.Odosielatel;
        MenoOdosielatela.Text = n.MenoOdosielatela.Length > 0 ? n.MenoOdosielatela : NastaveniaTlace.Nacitaj().Nazov ?? "";
        Pouzivatel.Text = n.Pouzivatel;
        KopiaSebe.IsChecked = n.KopiaSebe;
        Sluzba.SelectedItem = Sluzba.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag switch
        {
            "gmail" => n.Server.Contains("gmail", StringComparison.OrdinalIgnoreCase),
            "m365" => n.Server.Contains("office365", StringComparison.OrdinalIgnoreCase),
            _ => !n.Server.Contains("gmail", StringComparison.OrdinalIgnoreCase) && !n.Server.Contains("office365", StringComparison.OrdinalIgnoreCase),
        });
        HesloInfo.Text = EmailNastavenia.Heslo() is { Length: > 0 }
            ? Preklad.T("Heslo je uložené – zašifrované pre váš účet Windows. Nové zadajte len vtedy, keď ho chcete zmeniť.")
            : "";
        _nacitavam = false;
        Aktualizuj();
    }

    /// <summary>Nastavenia z okna.</summary>
    public EmailNastavenia Nastavenia => _povodne with
    {
        Sposob = Smtp.IsChecked == true ? SposobOdoslania.Smtp : SposobOdoslania.PostovyProgram,
        Server = Server.Text.Trim(),
        Port = int.TryParse(Port.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : 0,
        Zabezpecenie = Enum.Parse<ZabezpecenieSmtp>((string)((ComboBoxItem)Zabezpecenie.SelectedItem).Tag),
        Odosielatel = Odosielatel.Text.Trim(),
        MenoOdosielatela = MenoOdosielatela.Text.Trim(),
        Pouzivatel = Pouzivatel.Text.Trim(),
        KopiaSebe = KopiaSebe.IsChecked == true,
    };

    /// <summary>Samotest: heslo do poľa.</summary>
    public void NastavHeslo(string heslo) => Heslo.Password = heslo;

    private void Aktualizuj()
    {
        SekciaSmtp.IsEnabled = Smtp.IsChecked == true;
        Skusobny.IsEnabled = Smtp.IsChecked == true;
        Rada.Text = Smtp.IsChecked != true
            ? ""
            : (string?)((ComboBoxItem?)Sluzba.SelectedItem)?.Tag switch
            {
                "gmail" => Preklad.T("Gmail: zapnite dvojstupňové overenie a vytvorte heslo aplikácie (Účet Google → Zabezpečenie → Heslá aplikácií). Bežné heslo k Gmailu nefunguje."),
                "m365" => Preklad.T("Microsoft 365: prihlásenie heslom (SMTP AUTH) musí pre schránku povoliť správca. Microsoft ho postupne vypína – ak odosielanie nefunguje, použite poštový program."),
                _ => Preklad.T("Server odchádzajúcej pošty (SMTP), port a zabezpečenie nájdete v návode vášho poskytovateľa pošty."),
            };
    }

    private void Sposob_Click(object sender, RoutedEventArgs e) => Aktualizuj();

    private void Sluzba_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_nacitavam)
        {
            return;
        }

        switch ((string?)((ComboBoxItem?)Sluzba.SelectedItem)?.Tag)
        {
            case "gmail":
                NastavServer("smtp.gmail.com", 465, ZabezpecenieSmtp.SslTls);
                break;
            case "m365":
                NastavServer("smtp.office365.com", 587, ZabezpecenieSmtp.StartTls);
                break;
        }

        Aktualizuj();
    }

    private void NastavServer(string server, int port, ZabezpecenieSmtp zabezpecenie)
    {
        Server.Text = server;
        Port.Text = port.ToString(CultureInfo.InvariantCulture);
        Zabezpecenie.SelectedItem = Zabezpecenie.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == zabezpecenie.ToString());
    }

    private void Odosielatel_LostFocus(object sender, RoutedEventArgs e)
    {
        var adresa = Odosielatel.Text.Trim();
        if (!EmailNastavenia.JeAdresa(adresa))
        {
            return;
        }

        if (Pouzivatel.Text.Trim().Length == 0)
        {
            Pouzivatel.Text = adresa;
        }

        if (Server.Text.Trim().Length == 0 && EmailNastavenia.Sluzba(adresa) is { } sluzba)
        {
            _nacitavam = true;
            NastavServer(sluzba.Server, sluzba.Port, sluzba.Zabezpecenie);
            Sluzba.SelectedItem = Sluzba.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "gmail");
            _nacitavam = false;
            Aktualizuj();
        }
    }

    /// <summary>Chyba v nastavení SMTP alebo null.</summary>
    private string? Chyba(EmailNastavenia n)
    {
        if (n.Sposob != SposobOdoslania.Smtp)
        {
            return null;
        }

        if (n.Server.Length == 0)
        {
            return Preklad.T("Zadajte server odchádzajúcej pošty (SMTP).");
        }

        if (n.Port is <= 0 or > 65535)
        {
            return Preklad.T("Port musí byť číslo od 1 do 65535 (zvyčajne 465 alebo 587).");
        }

        return EmailNastavenia.JeAdresa(n.Odosielatel) ? null : Preklad.T("Zadajte platnú e-mailovú adresu odosielateľa.");
    }

    private void Ulozit_Click(object sender, RoutedEventArgs e)
    {
        var n = Nastavenia;
        if (Chyba(n) is { } chyba)
        {
            UkazStav(chyba, false);
            return;
        }

        n.Uloz();
        if (Heslo.Password.Length > 0)
        {
            EmailNastavenia.UlozHeslo(Heslo.Password);
        }

        DialogResult = true;
    }

    private async void Skusobny_Click(object sender, RoutedEventArgs e) => await OdoslatSkusobnyAsync();

    /// <summary>Pošle skúšobný e-mail na adresu odosielateľa. Vráti true pri úspechu (výsledok je aj v okne).</summary>
    public async Task<bool> OdoslatSkusobnyAsync()
    {
        var n = Nastavenia with { Sposob = SposobOdoslania.Smtp };
        if (Chyba(n) is { } chyba)
        {
            UkazStav(chyba, false);
            return false;
        }

        Skusobny.IsEnabled = false;
        UkazStav(Preklad.T("Odosiela sa…"), null);
        try
        {
            var heslo = Heslo.Password.Length > 0 ? Heslo.Password : EmailNastavenia.Heslo();
            await SmtpKlient.PosliAsync(n, heslo, new SpravaEmailu(n.Odosielatel, n.MenoOdosielatela, new[] { n.Odosielatel }, Array.Empty<string>(),
                Preklad.T("EviTA – skúšobný e-mail"),
                Preklad.T("Toto je skúšobný e-mail z programu EviTA. Odosielanie PDF e-mailom funguje."),
                Array.Empty<SuborVystupu>()));
            UkazStav(Preklad.F($"Skúšobný e-mail je odoslaný na {n.Odosielatel}. Ak nepríde, pozrite aj priečinok nevyžiadanej pošty."), true);
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
            Skusobny.IsEnabled = Smtp.IsChecked == true;
        }
    }

    /// <summary>Text výsledku v okne (pre samotest).</summary>
    public string StavText => Stav.Text;

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
