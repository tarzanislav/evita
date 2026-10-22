using System.Windows;
using System.Windows.Controls;
using Evita.Pomocne;

namespace Evita.Views;

public partial class PoznamkaEditor : UserControl
{
    private readonly DiktovanieDoPola _diktovanie;

    public PoznamkaEditor()
    {
        InitializeComponent();
        _diktovanie = new DiktovanieDoPola(PoleText, Diktovat, IkonaDiktovania, TextDiktovania, StavDiktovania, Hlasitost);
        Unloaded += (_, _) => _diktovanie.ZrusNahravanie();
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
    public string StavDiktovaniaText => _diktovanie.Stav;

    /// <summary>Vloží nadiktovaný text na miesto kurzora.</summary>
    public void VlozText(string text) => _diktovanie.VlozText(text);

    /// <summary>Prepíše zvuk a vloží text (samotest so súborom WAV).</summary>
    public Task PrepisAVlozAsync(float[] zvuk) => _diktovanie.PrepisAVlozAsync(zvuk);

    private async void Diktovat_Click(object sender, RoutedEventArgs e) => await _diktovanie.KlikAsync();

    private void NastavenieDiktovania_Click(object sender, RoutedEventArgs e)
    {
        _diktovanie.ZrusNahravanie();
        new DiktovanieWindow { Owner = Window.GetWindow(this) }.ShowDialog();
        _diktovanie.UkazStav("");
    }
}
