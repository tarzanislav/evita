using System.Windows.Controls;
using System.Windows.Shapes;
using Evita.Services;

namespace Evita.Pomocne;

/// <summary>
/// Diktovanie do textového poľa (poznámka, nové položky z dokladu): hlasové písanie Windows alebo nahrávka z mikrofónu
/// a prepis v počítači. Tlačidlo prepína Diktovať / Zastaviť a prepísať, stav a hlasitosť sa ukazujú pri ňom.
/// </summary>
public sealed class DiktovanieDoPola
{
    private readonly TextBox _pole;
    private readonly Button _tlacidlo;
    private readonly TextBlock _ikona;
    private readonly TextBlock _text;
    private readonly TextBlock _stav;
    private readonly Rectangle _hlasitost;
    private Mikrofon? _mikrofon;
    private bool _prepisujem;

    public DiktovanieDoPola(TextBox pole, Button tlacidlo, TextBlock ikona, TextBlock text, TextBlock stav, Rectangle hlasitost)
    {
        _pole = pole;
        _tlacidlo = tlacidlo;
        _ikona = ikona;
        _text = text;
        _stav = stav;
        _hlasitost = hlasitost;
    }

    /// <summary>Volá sa po prepise a vložení textu (napr. na rozpoznanie položiek).</summary>
    public event Action? Vlozene;

    public string Stav => _stav.Text;

    /// <summary>Vloží text na miesto kurzora (s medzerou alebo novým riadkom podľa okolia).</summary>
    public void VlozText(string text, bool novyRiadok = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var pozicia = Math.Clamp(_pole.CaretIndex, 0, _pole.Text.Length);
        var pred = pozicia > 0 ? _pole.Text[pozicia - 1] : '\n';
        var oddelovac = char.IsWhiteSpace(pred) ? "" : novyRiadok ? Environment.NewLine : " ";
        var vlozit = oddelovac + text.Trim();
        _pole.Text = _pole.Text.Insert(pozicia, vlozit);
        _pole.CaretIndex = pozicia + vlozit.Length;
        _pole.Focus();
        Vlozene?.Invoke();
    }

    public void UkazStav(string text, bool? uspech = null)
    {
        _stav.Text = text;
        _stav.SetResourceReference(TextBlock.ForegroundProperty, uspech switch
        {
            true => "SystemFillColorSuccessBrush",
            false => "SystemFillColorCautionBrush",
            null => "TextFillColorSecondaryBrush",
        });
    }

    /// <summary>Klik na tlačidlo Diktovať.</summary>
    public async Task KlikAsync(bool novyRiadok = false)
    {
        if (_prepisujem)
        {
            return;
        }

        if (Diktovanie.Sposob == SposobDiktovania.Windows)
        {
            var slovencina = HlasovePisanie.Spusti(_pole);
            UkazStav(slovencina
                    ? Preklad.T("Hovorte – Windows píše text na miesto kurzora. Ukončíte znova klávesmi Win+H alebo kliknutím mimo.")
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
            var zvuk = _mikrofon.Stop();
            ZrusNahravanie();
            await PrepisAVlozAsync(zvuk, novyRiadok);
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
            _mikrofon.Uroven += uroven => _hlasitost.Dispatcher.BeginInvoke(() => _hlasitost.Width = 4 + uroven * 90);
            _mikrofon.Start();
            _ikona.Text = "";
            _text.Text = Preklad.T("Zastaviť a prepísať");
            UkazStav(Preklad.T("Nahrávam… hovorte, potom kliknite Zastaviť a prepísať."));
        }
        catch (EvitaException ex)
        {
            ZrusNahravanie();
            UkazStav(ex.Message, false);
        }
    }

    /// <summary>Prepíše zvuk modelom v počítači a vloží text (aj samotest so súborom WAV).</summary>
    public async Task PrepisAVlozAsync(float[] zvuk, bool novyRiadok = false)
    {
        _prepisujem = true;
        _tlacidlo.IsEnabled = false;
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

            VlozText(text, novyRiadok);
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
            _tlacidlo.IsEnabled = true;
        }
    }

    public void ZrusNahravanie()
    {
        _mikrofon?.Dispose();
        _mikrofon = null;
        _hlasitost.Width = 0;
        _ikona.Text = "";
        _text.Text = Preklad.T("Diktovať");
    }
}
