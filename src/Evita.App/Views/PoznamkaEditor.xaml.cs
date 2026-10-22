using System.Windows;
using System.Windows.Controls;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.Views;

public partial class PoznamkaEditor : UserControl
{
    private Mikrofon? _mikrofon;
    private bool _prepisujem;

    public PoznamkaEditor()
    {
        InitializeComponent();
        Unloaded += (_, _) => ZrusNahravanie();
    }

    /// <summary>Kurzor do názvu (nová poznámka) alebo na koniec textu.</summary>
    public void Zameraj(bool nazov)
    {
        if (nazov)
        {
            PoleNazov.Focus();
            PoleNazov.SelectAll();
        }
        else
        {
            PoleText.Focus();
            PoleText.CaretIndex = PoleText.Text.Length;
        }
    }

    /// <summary>Stav diktovania (pre samotest).</summary>
    public string StavDiktovaniaText => StavDiktovania.Text;

    /// <summary>Vloží nadiktovaný text na miesto kurzora (s medzerou podľa okolia).</summary>
    public void VlozText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var pozicia = Math.Clamp(PoleText.CaretIndex, 0, PoleText.Text.Length);
        var pred = pozicia > 0 ? PoleText.Text[pozicia - 1] : '\n';
        var vlozit = (char.IsWhiteSpace(pred) ? "" : " ") + text.Trim();
        PoleText.Text = PoleText.Text.Insert(pozicia, vlozit);
        PoleText.CaretIndex = pozicia + vlozit.Length;
        PoleText.Focus();
    }

    private void UkazStav(string text, bool? uspech = null)
    {
        StavDiktovania.Text = text;
        StavDiktovania.SetResourceReference(TextBlock.ForegroundProperty, uspech switch
        {
            true => "SystemFillColorSuccessBrush",
            false => "SystemFillColorCautionBrush",
            null => "TextFillColorSecondaryBrush",
        });
    }

    private async void Diktovat_Click(object sender, RoutedEventArgs e)
    {
        if (_prepisujem)
        {
            return;
        }

        if (Diktovanie.Sposob == SposobDiktovania.Windows)
        {
            var slovencina = HlasovePisanie.Spusti(PoleText);
            UkazStav(slovencina
                ? Preklad.T("Hovorte – Windows píše text do poznámky. Ukončíte znova klávesmi Win+H alebo kliknutím mimo.")
                : Preklad.T("Hovorte… Pre slovenčinu pridajte v Nastaveniach Windows slovenskú klávesnicu (Čas a jazyk → Jazyk a oblasť)."),
                slovencina ? null : false);
            return;
        }

        if (_mikrofon is null)
        {
            ZacniNahravat();
        }
        else
        {
            await DokonciAsync();
        }
    }

    private void ZacniNahravat()
    {
        if (PrepisHlasu.Prekazka() is { } prekazka)
        {
            UkazStav(prekazka, false);
            return;
        }

        if (!Diktovanie.Model.JeStiahnuty)
        {
            UkazStav(Preklad.T("Model na prepis nie je stiahnutý – otvorte nastavenie diktovania (ozubené koliesko)."), false);
            return;
        }

        try
        {
            _mikrofon = new Mikrofon();
            _mikrofon.Uroven += uroven => Dispatcher.BeginInvoke(() => Hlasitost.Width = 4 + uroven * 90);
            _mikrofon.Start();
            IkonaDiktovania.Text = "\uE71A";
            TextDiktovania.Text = Preklad.T("Zastaviť a prepísať");
            UkazStav(Preklad.T("Nahrávam… hovorte, potom kliknite Zastaviť a prepísať."));
        }
        catch (EvitaException ex)
        {
            ZrusNahravanie();
            UkazStav(ex.Message, false);
        }
    }

    private async Task DokonciAsync()
    {
        var zvuk = _mikrofon!.Stop();
        ZrusNahravanie();
        await PrepisAVlozAsync(zvuk);
    }

    /// <summary>Prepíše zvuk modelom v počítači a vloží text (aj samotest so súborom WAV).</summary>
    public async Task PrepisAVlozAsync(float[] zvuk)
    {
        _prepisujem = true;
        Diktovat.IsEnabled = false;
        UkazStav(Preklad.F($"Prepisujem {zvuk.Length / (double)PrepisHlasu.VzorkovaFrekvencia:0} s nahrávky…"));
        try
        {
            var cas = System.Diagnostics.Stopwatch.StartNew();
            var text = await PrepisHlasu.PrepisAsync(zvuk, Diktovanie.Model);
            if (text.Length == 0)
            {
                UkazStav(Preklad.T("Nič som nezachytil – hovorte bližšie k mikrofónu alebo skontrolujte, či nie je stlmený."), false);
                return;
            }

            VlozText(text);
            UkazStav(Preklad.F($"Hotovo za {cas.Elapsed.TotalSeconds:0} s – text je vložený na miesto kurzora."), true);
        }
        catch (EvitaException ex)
        {
            UkazStav(ex.Message, false);
        }
        catch (Exception ex)
        {
            UkazStav(Preklad.F($"Prepis sa nepodaril: {ex.GetBaseException().Message}"), false);
        }
        finally
        {
            _prepisujem = false;
            Diktovat.IsEnabled = true;
        }
    }

    private void ZrusNahravanie()
    {
        _mikrofon?.Dispose();
        _mikrofon = null;
        Hlasitost.Width = 0;
        IkonaDiktovania.Text = "\uE720";
        TextDiktovania.Text = Preklad.T("Diktovať");
    }

    private void NastavenieDiktovania_Click(object sender, RoutedEventArgs e)
    {
        ZrusNahravanie();
        new DiktovanieWindow { Owner = Window.GetWindow(this) }.ShowDialog();
        UkazStav("");
    }
}
