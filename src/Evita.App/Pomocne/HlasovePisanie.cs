using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;
using Evita.Services;

namespace Evita.Pomocne;

/// <summary>
/// Hlasové písanie Windows (Win+H): textové pole dostane fokus a slovenskú klávesnicu (ak je v systéme), potom sa
/// stlačí Win+H. Windows prepisuje reč priamo do poľa – rozpoznávanie robí Microsoft cez internet.
/// </summary>
public static class HlasovePisanie
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vstup
    {
        public int Typ;
        public KlavesovyVstup Klaves;
        private readonly long _rezerva; // INPUT má 40 bajtov (únia s MOUSEINPUT)
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KlavesovyVstup
    {
        public ushort Kod;
        public ushort Sken;
        public uint Priznaky;
        public uint Cas;
        public IntPtr Doplnok;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint pocet, Vstup[] vstupy, int velkost);

    private const int TypKlavesnica = 1;
    private const uint Pustit = 0x0002;
    private const ushort Win = 0x5B;
    private const ushort H = 0x48;

    /// <summary>Slovenská klávesnica je v systéme (hlasové písanie potom rozpoznáva po slovensky).</summary>
    public static CultureInfo? Slovencina =>
        InputLanguageManager.Current.AvailableInputLanguages?.OfType<CultureInfo>().FirstOrDefault(c => c.TwoLetterISOLanguageName == "sk");

    /// <summary>Spustí hlasové písanie do poľa. Vráti false, ak v systéme nie je slovenčina (diktovať sa dá aj tak).</summary>
    public static bool Spusti(TextBox pole)
    {
        var slovencina = Slovencina;
        if (slovencina is not null)
        {
            InputLanguageManager.SetInputLanguage(pole, slovencina);
            InputLanguageManager.SetRestoreInputLanguage(pole, true);
        }

        pole.Focus();
        Keyboard.Focus(pole);
        if (Dialogy.TichyRezim)
        {
            Dialogy.TicheHlasenia.Add("[Hlasové písanie] Win+H");
            return slovencina is not null;
        }

        var vstupy = new[]
        {
            new Vstup { Typ = TypKlavesnica, Klaves = new KlavesovyVstup { Kod = Win } },
            new Vstup { Typ = TypKlavesnica, Klaves = new KlavesovyVstup { Kod = H } },
            new Vstup { Typ = TypKlavesnica, Klaves = new KlavesovyVstup { Kod = H, Priznaky = Pustit } },
            new Vstup { Typ = TypKlavesnica, Klaves = new KlavesovyVstup { Kod = Win, Priznaky = Pustit } },
        };
        SendInput((uint)vstupy.Length, vstupy, Marshal.SizeOf<Vstup>());
        return slovencina is not null;
    }
}
