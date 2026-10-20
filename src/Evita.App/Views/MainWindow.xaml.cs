using System.Windows;
using System.Windows.Controls;
using Evita.ViewModels;

namespace Evita.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model = new();

    public MainWindow()
    {
        DataContext = _model;
        InitializeComponent();
        _model.ZiadostOSekciu += OznacSekciu;
        Loaded += (_, _) => Navigacia.SelectedIndex = 0;
    }

    private void Navigacia_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Navigacia.SelectedItem is ListBoxItem { Tag: string sekcia })
        {
            _model.Prejdi(sekcia);
        }
    }

    /// <summary>Prepne menu na danú sekciu (napr. z dashboardu do evidencie).</summary>
    public void OznacSekciu(string sekcia)
    {
        foreach (var polozka in Navigacia.Items.OfType<ListBoxItem>())
        {
            if (polozka.Tag as string == sekcia)
            {
                Navigacia.SelectedItem = polozka;
                return;
            }
        }
    }
}
