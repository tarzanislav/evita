using System.Windows;
using Evita.ViewModels;

namespace Evita.Views;

public partial class UdalostWindow : Window
{
    public UdalostWindow(UdalostEditViewModel model)
    {
        Model = model;
        DataContext = model;
        InitializeComponent();
        model.Zatvorit += vysledok =>
        {
            try
            {
                DialogResult = vysledok;
            }
            catch (InvalidOperationException)
            {
                Close();
            }
        };
        Loaded += (_, _) =>
        {
            PoleNazov.Focus();
            PoleNazov.SelectAll();
        };
    }

    public UdalostEditViewModel Model { get; }
}
