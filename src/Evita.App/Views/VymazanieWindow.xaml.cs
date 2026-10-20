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
        Vymazat.IsEnabled = Sk.BezDiakritiky(Potvrdenie.Text.Trim()) == "vymazat";
    }

    private void Vymazat_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
