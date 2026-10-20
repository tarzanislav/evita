using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Evita.Services;

namespace Evita.Pomocne;

/// <summary>
/// Čítačka čiarových a QR kódov pripojená cez USB sa správa ako klávesnica: znaky kódu „napíše“ veľmi rýchlo
/// za sebou a na konci stlačí Enter. Tu sa takýto rýchly sled znakov rozpozná v ktoromkoľvek okne programu:
/// <list type="bullet">
/// <item>v poli na skenovanie (vlastnosť <see cref="SkenovaciePoleProperty"/>) sa kód odošle na spracovanie,</item>
/// <item>v bežnom textovom poli (napr. sériové číslo v karte položky) kód zostane v poli – opravia sa len znaky
/// z iného rozloženia klávesnice – a Enter presunie kurzor na ďalšie pole,</item>
/// <item>mimo textového poľa sa kód odošle na spracovanie (napr. otvorí sa karta položky).</item>
/// </list>
/// </summary>
public static class CitackaKodov
{
    /// <summary>Najväčší odstup medzi znakmi z čítačky (čítačky píšu za 1 až 20 ms, človek pomalšie).</summary>
    private const double NajviacMedziZnakmi = 50;

    /// <summary>Priemerný odstup medzi znakmi, pod ktorým ide určite o čítačku.</summary>
    private const double PriemerMedziZnakmi = 28;

    private const int NajmenejZnakov = 4;

    /// <summary>Pole, do ktorého sa skenuje: Enter (z čítačky aj ručne) odošle obsah na spracovanie a pole vyprázdni.</summary>
    public static readonly DependencyProperty SkenovaciePoleProperty =
        DependencyProperty.RegisterAttached("SkenovaciePole", typeof(bool), typeof(CitackaKodov), new PropertyMetadata(false));

    public static bool GetSkenovaciePole(DependencyObject prvok) => (bool)prvok.GetValue(SkenovaciePoleProperty);

    public static void SetSkenovaciePole(DependencyObject prvok, bool hodnota) => prvok.SetValue(SkenovaciePoleProperty, hodnota);

    private static readonly Stopwatch Hodiny = Stopwatch.StartNew();
    private static readonly List<double> Casy = new();
    private static readonly StringBuilder Napisane = new();
    private static readonly StringBuilder Americke = new();
    private static TextBox? _pole;
    private static string _textPred = "";
    private static int _zaciatok;
    private static double _poslednyZnak = -1000;
    private static bool _zapnute;

    /// <summary>Rozpoznávanie čítačky mimo polí na skenovanie (dá sa vypnúť v nastaveniach skenovania).</summary>
    public static bool VCelomPrograme { get; set; } = true;

    public static void Zapni()
    {
        if (_zapnute)
        {
            return;
        }

        _zapnute = true;
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(PriKlavese), true);
        EventManager.RegisterClassHandler(typeof(Window), TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(PriTexte), true);
    }

    private static void PriKlavese(object sender, KeyEventArgs e)
    {
        var klaves = e.Key == Key.System ? e.SystemKey : e.Key;
        var teraz = Hodiny.Elapsed.TotalMilliseconds;

        if (klaves is Key.Enter or Key.Tab)
        {
            if (Keyboard.FocusedElement is TextBox { } pole && GetSkenovaciePole(pole) && (klaves == Key.Enter || JeVybuch(teraz)))
            {
                // pole na skenovanie: Enter odošle obsah (z čítačky aj napísaný ručne), Tab len kód z čítačky
                var jeSken = JeVybuch(teraz);
                var varianty = jeSken && _pole == pole ? new[] { Americke.ToString() } : Array.Empty<string>();
                var text = pole.Text;
                Vynuluj();
                e.Handled = true;
                pole.Clear();
                if (!string.IsNullOrWhiteSpace(text) || varianty.Length > 0)
                {
                    SkenerCentrum.Odosli(Sken.Z(text, jeSken ? ZdrojSkenu.Citacka : ZdrojSkenu.Klavesnica, varianty));
                }

                return;
            }

            if (JeVybuch(teraz))
            {
                DokonciVybuch(e, klaves);
            }

            Vynuluj();
            return;
        }

        if (JeModifikator(klaves))
        {
            return;
        }

        var znak = AmerickyZnak(klaves);
        if (znak is null || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
        {
            Vynuluj();
            return;
        }

        if (teraz - _poslednyZnak > NajviacMedziZnakmi * 2)
        {
            // začiatok nového sledu znakov
            Vynuluj();
            if (Keyboard.FocusedElement is TextBox textove && !textove.IsReadOnly)
            {
                _pole = textove;
                _textPred = textove.Text;
                _zaciatok = textove.SelectionStart;
                _textPred = _textPred.Remove(_zaciatok, Math.Min(textove.SelectionLength, _textPred.Length - _zaciatok));
            }
        }

        Casy.Add(teraz);
        Americke.Append(znak.Value);
        _poslednyZnak = teraz;
    }

    private static void PriTexte(object sender, TextCompositionEventArgs e)
    {
        if (Casy.Count > 0 && Hodiny.Elapsed.TotalMilliseconds - _poslednyZnak < NajviacMedziZnakmi * 2)
        {
            Napisane.Append(e.Text);
        }
    }

    /// <summary>Posledné znaky prišli tak rýchlo, že ich napísala čítačka.</summary>
    private static bool JeVybuch(double teraz)
    {
        if (Casy.Count < NajmenejZnakov || teraz - _poslednyZnak > NajviacMedziZnakmi * 3)
        {
            return false;
        }

        double najviac = 0;
        for (var i = 1; i < Casy.Count; i++)
        {
            najviac = Math.Max(najviac, Casy[i] - Casy[i - 1]);
        }

        var priemer = (Casy[^1] - Casy[0]) / (Casy.Count - 1);
        return najviac <= NajviacMedziZnakmi && priemer <= PriemerMedziZnakmi;
    }

    private static void DokonciVybuch(KeyEventArgs e, Key klaves)
    {
        var napisane = Napisane.ToString();
        var americke = Americke.ToString();
        var fokus = Keyboard.FocusedElement;

        if (fokus is TextBox pole && !pole.IsReadOnly)
        {
            // bežné textové pole: kód zostane v poli, opravia sa len znaky z iného rozloženia klávesnice
            if (pole == _pole && OpravaJeLepsia(napisane, americke) && pole.Text == _textPred.Insert(_zaciatok, napisane))
            {
                pole.Text = _textPred.Insert(_zaciatok, americke);
                pole.SelectionStart = _zaciatok + americke.Length;
            }

            if (klaves == Key.Enter && !pole.AcceptsReturn)
            {
                e.Handled = true;
                pole.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }

            return;
        }

        if (fokus is PasswordBox || (fokus is ComboBox { IsEditable: true }) || !VCelomPrograme)
        {
            return;
        }

        e.Handled = true;
        var text = napisane.Length > 0 ? napisane : americke;
        SkenerCentrum.Odosli(Sken.Z(text, ZdrojSkenu.Citacka, new[] { americke }));
    }

    /// <summary>
    /// Text z čítačky v americkom režime na slovenskej klávesnici obsahuje namiesto číslic písmená s diakritikou;
    /// oprava sa použije, keď americký výklad je čitateľnejší (má číslice tam, kde napísaný text diakritiku).
    /// </summary>
    private static bool OpravaJeLepsia(string napisane, string americke)
    {
        if (napisane.Length == 0 || americke.Length != napisane.Length || napisane == americke)
        {
            return false;
        }

        var diakritika = napisane.Count(z => z > 127);
        var cisliceNavyse = americke.Count(char.IsAsciiDigit) - napisane.Count(char.IsAsciiDigit);
        return diakritika > 0 && cisliceNavyse > 0;
    }

    private static void Vynuluj()
    {
        Casy.Clear();
        Napisane.Clear();
        Americke.Clear();
        _pole = null;
        _textPred = "";
        _zaciatok = 0;
    }

    private static bool JeModifikator(Key klaves) => klaves is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.CapsLock or Key.NumLock or Key.ImeProcessed or Key.DeadCharProcessed;

    // ------------------------------------------------------------------ znak podľa americkej klávesnice

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint kod, uint typ, IntPtr rozlozenie);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint vlakno);

    /// <summary>Kláves podľa polohy (scan kód) na americkej klávesnici: znak bez Shift a so Shift.</summary>
    private static readonly Dictionary<uint, (char Bez, char So)> AmerickaKlavesnica = VytvorAmerickuKlavesnicu();

    private static Dictionary<uint, (char, char)> VytvorAmerickuKlavesnicu()
    {
        var mapa = new Dictionary<uint, (char, char)>();
        void Rad(uint prvy, string bez, string so)
        {
            for (var i = 0; i < bez.Length; i++)
            {
                mapa[prvy + (uint)i] = (bez[i], so[i]);
            }
        }

        Rad(0x02, "1234567890-=", "!@#$%^&*()_+");
        Rad(0x10, "qwertyuiop[]", "QWERTYUIOP{}");
        Rad(0x1E, "asdfghjkl;'`", "ASDFGHJKL:\"~");
        Rad(0x2B, "\\zxcvbnm,./", "|ZXCVBNM<>?");
        mapa[0x39] = (' ', ' ');
        return mapa;
    }

    private static char? AmerickyZnak(Key klaves)
    {
        if (klaves is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return (char)('0' + (klaves - Key.NumPad0));
        }

        switch (klaves)
        {
            case Key.Add: return '+';
            case Key.Subtract: return '-';
            case Key.Multiply: return '*';
            case Key.Divide: return '/';
            case Key.Decimal: return '.';
        }

        try
        {
            var vk = (uint)KeyInterop.VirtualKeyFromKey(klaves);
            var scan = MapVirtualKeyEx(vk, 0, GetKeyboardLayout(0));
            if (!AmerickaKlavesnica.TryGetValue(scan, out var znaky))
            {
                return null;
            }

            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var znak = shift ? znaky.So : znaky.Bez;
            if (char.IsLetter(znak) && Keyboard.IsKeyToggled(Key.CapsLock))
            {
                znak = char.IsUpper(znak) ? char.ToLowerInvariant(znak) : char.ToUpperInvariant(znak);
            }

            return znak;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
