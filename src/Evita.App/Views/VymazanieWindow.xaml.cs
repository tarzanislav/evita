using System.Windows;
using System.Windows.Controls;
using Evita.Services;

namespace Evita.Views;

public partial class VymazanieWindow : Window
{
    public VymazanieWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Potvrdenie.Focus();
    }

    public bool PonechatMiestnosti => PonechatMiestnostiPole.IsChecked == true;

    public bool PonechatOsoby => PonechatOsobyPole.IsChecked == true;

    private void Potvrdenie_TextChanged(object sender, TextChangedEventArgs e)
    {
        // slovo na potvrdenie v jazyku programu (po slovensky platí vždy)
        var text = Sk.BezDiakritiky(Potvrdenie.Text.Trim());
        Vymazat.IsEnabled = text == "vymazat" || text == Sk.BezDiakritiky(Preklad.T("VYMAZAŤ"));
    }

    private void Vymazat_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
