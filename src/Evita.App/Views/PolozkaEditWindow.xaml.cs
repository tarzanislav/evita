using System.Windows;
using System.Windows.Input;
using Evita.ViewModels;

namespace Evita.Views;

public partial class PolozkaEditWindow : Window
{
    public PolozkaEditWindow(PolozkaEditViewModel model)
    {
        Model = model;
        DataContext = model;
        InitializeComponent();
        model.Zatvorit += vysledok => DialogResult = vysledok;
        Loaded += (_, _) => NazovPole.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Model.UlozitCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    public PolozkaEditViewModel Model { get; }
}
