using System.Windows;

namespace Evita.Views;

public partial class PrveSpustenieWindow : Window
{
    public PrveSpustenieWindow()
    {
        InitializeComponent();
    }

    public bool UkazkoveData { get; private set; }

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
