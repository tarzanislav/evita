using System.Windows;
using Evita.ViewModels;

namespace Evita.Views;

public partial class HromadnaZmenaWindow : Window
{
    public HromadnaZmenaWindow(HromadnaZmenaViewModel model)
    {
        Model = model;
        DataContext = model;
        InitializeComponent();
        model.Zatvorit += vysledok => DialogResult = vysledok;
    }

    public HromadnaZmenaViewModel Model { get; }
}
