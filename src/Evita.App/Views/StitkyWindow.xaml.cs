using System.Windows;
using Evita.ViewModels;

namespace Evita.Views;

/// <summary>Tlač štítkov s QR alebo čiarovým kódom: nastavenia vľavo, náhľad strán vpravo.</summary>
public partial class StitkyWindow : Window
{
    public StitkyWindow(StitkyViewModel model)
    {
        Model = model;
        DataContext = model;
        InitializeComponent();
        Closing += (_, _) => model.PriZatvoreni();
    }

    public StitkyViewModel Model { get; }
}
