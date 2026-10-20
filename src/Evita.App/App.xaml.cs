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

        var prveSpustenie = !Databaza.Existuje;
        try
        {
            Databaza.Inicializuj();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Databázu sa nepodarilo otvoriť ani vytvoriť.\n\n{Databaza.Cesta}\n\n{ex.Message}",
                "EviTA", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (prveSpustenie)
        {
            var uvod = new PrveSpustenieWindow();
            uvod.ShowDialog();
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
