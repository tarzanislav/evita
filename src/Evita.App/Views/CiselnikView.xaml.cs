using System.Windows;
using System.Windows.Controls;
using Evita.ViewModels;

namespace Evita.Views;

public partial class CiselnikView : UserControl
{
    public CiselnikView()
    {
        InitializeComponent();

        // stĺpec s ikonou len pri kategóriách
        DataContextChanged += (_, _) =>
            StlpecIkony.Visibility = DataContext is CiselnikViewModel { SuKategorie: true } ? Visibility.Visible : Visibility.Collapsed;
    }
}
