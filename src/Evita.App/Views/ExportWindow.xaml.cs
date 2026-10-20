using System.Windows;
using Evita.ViewModels;

namespace Evita.Views;

public partial class ExportWindow : Window
{
    public ExportWindow(ExportViewModel model)
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
                // okno nie je otvorené ako dialóg (napr. pri automatickom teste)
                Close();
            }
        };
    }

    public ExportViewModel Model { get; }
}
