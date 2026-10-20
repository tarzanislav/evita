using System.Windows.Controls;

namespace Evita.Views;

public partial class UlohyView : UserControl
{
    public UlohyView()
    {
        InitializeComponent();
        Loaded += (_, _) => PoleNova.Focus();
    }
}
