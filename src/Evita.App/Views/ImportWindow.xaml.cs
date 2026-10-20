using System.ComponentModel;
using System.Windows;
using Evita.Models;
using Evita.Services;
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
        model.PropertyChanged += PriZmene;
    }

    public ImportViewModel Model { get; }

    /// <summary>Stĺpce tabuľky kontroly podľa importovaného číselníka.</summary>
    private void PriZmene(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ImportViewModel.TypRiadkovCiselnika) || Model.TypRiadkovCiselnika is not { } typ)
        {
            return;
        }

        (string Pole1, string? Pole2, string? Pole3) stlpce = typ switch
        {
            TypCiselnika.Kategorie => ("Názov", null, null),
            TypCiselnika.Miestnosti => ("Názov", "Budova", "Poznámka"),
            _ => ("Meno", "Priezvisko", "Funkcia"),
        };
        var (pole1, pole2, pole3) = stlpce;

        StlpecPole1.Header = Preklad.T(pole1);
        StlpecPole2.Header = pole2 is null ? "" : Preklad.T(pole2);
        StlpecPole3.Header = pole3 is null ? "" : Preklad.T(pole3);
        StlpecPole2.Visibility = pole2 is null ? Visibility.Collapsed : Visibility.Visible;
        StlpecPole3.Visibility = pole3 is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
