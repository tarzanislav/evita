using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;

namespace Evita;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        NastavSlovencinu();
        DispatcherUnhandledException += NeosetrenaChyba;

        if (e.Args.Contains("--samotest", StringComparer.OrdinalIgnoreCase))
        {
            Dialogy.TichyRezim = true;
            Shutdown(Samotest.Spusti(this));
            return;
        }

        if (e.Args.Contains("--zaloha", StringComparer.OrdinalIgnoreCase))
        {
            // spustené Plánovačom úloh Windows – bez okien, len pravidelná záloha
            Shutdown(ZalohaBezOkien());
            return;
        }

        var prveSpustenie = !Databaza.Existuje;
        try
        {
            Databaza.Inicializuj();
        }
        catch (Exception ex)
        {
            if (!ObnovaPriChybe(ex))
            {
                Shutdown(1);
                return;
            }
        }

        if (!prveSpustenie)
        {
            SkontrolujDatabazu();
        }

        PripravKalendarium();

        var spustitImport = false;
        if (prveSpustenie)
        {
            var uvod = new PrveSpustenieWindow();
            uvod.ShowDialog();
            spustitImport = uvod.Import;
            if (uvod.NazovOrganizacie.Length > 0)
            {
                try
                {
                    NastaveniaTlace.UlozNazov(uvod.NazovOrganizacie);
                }
                catch (Exception)
                {
                    // názov sa dá zadať aj neskôr v časti Tlač a zostavy
                }
            }
            if (uvod.UkazkoveData)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    new ServisService().NacitatUkazkoveData();
                }
                catch (Exception ex)
                {
                    Dialogy.Chyba("Ukážkové dáta sa nepodarilo vytvoriť.", ex);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        var okno = new MainWindow();
        MainWindow = okno;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        okno.Show();

        if (spustitImport && okno.DataContext is ViewModels.MainViewModel hlavny)
        {
            okno.Dispatcher.InvokeAsync(() =>
            {
                hlavny.PrejdiNaSekciu("import");
                hlavny.ImportExport.Importovat();
            });
        }

        // vzorové súbory pre import (Dokumenty\EviTA\Vzory), ak ešte nie sú
        Task.Run(VzoryService.VytvorAkChybaju);

        // prečíslovanie záloh zo starších verzií a pravidelná záloha na pozadí, ak už je čas
        Task.Run(() =>
            {
                var zalohy = new ZalohaService();
                try
                {
                    zalohy.PrecislujStareZalohy();
                    zalohy.OpravTypyStarychZaznamov();
                }
                catch (Exception)
                {
                    // prečíslovanie nie je nevyhnutné
                }

                return zalohy.AutomatickaAkTreba();
            })
            .ContinueWith(_ => okno.Dispatcher.InvokeAsync(okno.PoAutomatickejZalohe), TaskScheduler.Default);

        // kontrola novej verzie programu
        _ = okno.SkontrolujAktualizaciuAsync();
    }

    /// <summary>Databáza sa nedá otvoriť – ponúkne obnovu zo zálohy. Vráti true, ak sa dá pokračovať.</summary>
    private static bool ObnovaPriChybe(Exception chyba)
    {
        var otazka = MessageBox.Show(
            $"Databázu sa nepodarilo otvoriť.\n\n{Databaza.Cesta}\n\n{chyba.GetBaseException().Message}\n\n" +
            "Chcete ju obnoviť zo zálohy?",
            "EviTA", MessageBoxButton.YesNo, MessageBoxImage.Error);
        if (otazka != MessageBoxResult.Yes)
        {
            return false;
        }

        var vyber = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Vyberte zálohu EviTA",
            Filter = "Zálohy EviTA (*.zip)|*.zip",
            InitialDirectory = Databaza.PriecinokZaloh,
        };
        if (vyber.ShowDialog() != true)
        {
            return false;
        }

        try
        {
            var sluzba = new ZalohaService();
            string? heslo = null;
            if (sluzba.JeZasifrovana(vyber.FileName))
            {
                heslo = HesloWindow.Zadaj(null);
                if (heslo is null)
                {
                    return false;
                }
            }

            sluzba.Obnov(vyber.FileName, heslo);
            MessageBox.Show("Databáza je obnovená zo zálohy.", "EviTA", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Obnova sa nepodarila.\n\n{ex.GetBaseException().Message}", "EviTA",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Kalendárium (meniny, sviatky) – jeho chyba program nezastaví, dashboard ukáže, že nie je dostupné.</summary>
    private static void PripravKalendarium()
    {
        try
        {
            Kalendarium.KalendariumDb.Spolocna.Inicializuj();
        }
        catch (Exception)
        {
            // bez Kalendária program funguje ďalej
        }
    }

    /// <summary>Rýchla kontrola pri štarte – pri probléme upozorní a ponúkne riešenie v časti Servis.</summary>
    private static void SkontrolujDatabazu()
    {
        try
        {
            var vysledok = new DiagnostikaService().RychlaKontrola();
            if (!vysledok.Ok)
            {
                Dialogy.Upozornenie(
                    "Rýchla kontrola zistila, že databáza môže byť poškodená.\n\n" +
                    "Program sa otvorí, ale odporúčame v časti Servis → Kontrola a oprava spustiť úplnú kontrolu " +
                    "a podľa výsledku obnoviť databázu zo zálohy alebo použiť záchranu dát.\n\n" +
                    string.Join("\n", vysledok.Riadky.Take(5)));
            }
        }
        catch (Exception ex)
        {
            Dialogy.Chyba("Kontrolu databázy sa nepodarilo vykonať. Odporúčame obnovu zo zálohy (Servis → Obnova).", ex);
        }
    }

    private static int ZalohaBezOkien()
    {
        try
        {
            if (!Databaza.Existuje)
            {
                return 0;
            }

            Databaza.Inicializuj();
            PripravKalendarium();
            new ZalohaService().AutomatickaAkTreba();
            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    private static void NastavSlovencinu()
    {
        var kultura = Sk.Kultura;
        CultureInfo.DefaultThreadCurrentCulture = kultura;
        CultureInfo.DefaultThreadCurrentUICulture = kultura;
        Thread.CurrentThread.CurrentCulture = kultura;
        Thread.CurrentThread.CurrentUICulture = kultura;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(kultura.IetfLanguageTag)));
    }

    private void NeosetrenaChyba(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (Dialogy.TichyRezim)
        {
            Samotest.Chyby.Add(e.Exception.ToString());
            e.Handled = true;
            return;
        }

        Mouse.OverrideCursor = null;
        if (e.Exception is EvitaException chyba)
        {
            Dialogy.Upozornenie(chyba.Message);
        }
        else
        {
            Dialogy.Chyba("Nastala neočakávaná chyba. Program pokračuje ďalej.", e.Exception);
        }

        e.Handled = true;
    }
}
