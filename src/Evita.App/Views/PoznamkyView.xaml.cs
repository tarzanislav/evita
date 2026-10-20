using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Evita.ViewModels;

namespace Evita.Views;

public partial class PoznamkyView : UserControl
{
    public PoznamkyView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is PoznamkyViewModel stary)
            {
                stary.ZiadostOZameranie -= Zameraj;
            }

            if (e.NewValue is PoznamkyViewModel novy)
            {
                novy.ZiadostOZameranie += Zameraj;
            }
        };
        Unloaded += (_, _) => (DataContext as PoznamkyViewModel)?.UlozAkTreba();
    }

    private void Zameraj(bool nazov) =>
        Dispatcher.InvokeAsync(() => Editor.Zameraj(nazov), DispatcherPriority.Input);
}
