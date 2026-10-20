using System.Windows;
using Evita.ViewModels;

namespace Evita.Views;

public partial class ImportWindow : Window
{
    public ImportWindow(ImportViewModel model)
    {
        Model = model;
        DataContext = model;
        InitializeComponent();
        model.Zatvorit += Close;
    }

    public ImportViewModel Model { get; }
}
