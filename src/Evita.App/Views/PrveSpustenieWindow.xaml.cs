using System.Windows;

namespace Evita.Views;

public partial class PrveSpustenieWindow : Window
{
    public PrveSpustenieWindow()
    {
        InitializeComponent();
    }

    public bool UkazkoveData { get; private set; }

    /// <summary>Názov organizácie zadaný pri prvom spustení (môže byť prázdny).</summary>
    public string NazovOrganizacie => Organizacia.Text.Trim();

    /// <summary>Po otvorení programu spustiť sprievodcu importom.</summary>
    public bool Import { get; private set; }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        Import = true;
        Close();
    }

    private void Ukazkove_Click(object sender, RoutedEventArgs e)
    {
        UkazkoveData = true;
        Close();
    }

    private void Prazdna_Click(object sender, RoutedEventArgs e)
    {
        UkazkoveData = false;
        Close();
    }
}
