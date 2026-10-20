using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Evita.Views;

public partial class SkenovanieView : UserControl
{
    public SkenovanieView()
    {
        InitializeComponent();

        // kurzor hneď v poli na skenovanie
        Loaded += (_, _) => Dispatcher.InvokeAsync(() => Pole.Focus(), DispatcherPriority.Input);
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Dispatcher.InvokeAsync(() => Pole.Focus(), DispatcherPriority.Input);
            }
        };
    }
}
