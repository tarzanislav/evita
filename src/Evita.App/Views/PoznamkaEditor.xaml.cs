using System.Windows.Controls;

namespace Evita.Views;

public partial class PoznamkaEditor : UserControl
{
    public PoznamkaEditor()
    {
        InitializeComponent();
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
}
