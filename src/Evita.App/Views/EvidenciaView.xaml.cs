using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Evita.Models;
using Evita.ViewModels;

namespace Evita.Views;

public partial class EvidenciaView : UserControl
{
    private EvidenciaViewModel? _model;

    public EvidenciaView()
    {
        InitializeComponent();
        Loaded += (_, _) => PripojModel();
        Unloaded += (_, _) => OdpojModel();
        DataContextChanged += (_, _) =>
        {
            OdpojModel();
            PripojModel();
        };
        PreviewKeyDown += View_PreviewKeyDown;
    }

    private void PripojModel()
    {
        OdpojModel();
        _model = DataContext as EvidenciaViewModel;
        if (_model is not null)
        {
            _model.ZobrazitRiadok += ZobrazRiadok;
        }
    }

    private void OdpojModel()
    {
        if (_model is not null)
        {
            _model.ZobrazitRiadok -= ZobrazRiadok;
            _model = null;
        }
    }

    private void ZobrazRiadok(PolozkaRiadok riadok)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Tabulka.SelectedItem = riadok;
            Tabulka.ScrollIntoView(riadok);
            Tabulka.Focus();
        });
    }

    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Hladanie.Focus();
            Hladanie.SelectAll();
            e.Handled = true;
        }
    }

    private void Tabulka_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // dvojklik len na riadok, nie na hlavičku stĺpca
        if (e.OriginalSource is DependencyObject zdroj && NajdiRodica<DataGridRow>(zdroj) is not null)
        {
            _model?.UpravitCommand.Execute(null);
        }
    }

    private void Tabulka_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                _model.UpravitCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete:
                _model.VyraditCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void Tabulka_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_model is not null)
        {
            _model.Oznacene = Tabulka.SelectedItems.OfType<PolozkaRiadok>().ToList();
        }
    }

    private static T? NajdiRodica<T>(DependencyObject? prvok) where T : DependencyObject
    {
        while (prvok is not null)
        {
            if (prvok is T hladany)
            {
                return hladany;
            }

            prvok = prvok is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(prvok)
                : LogicalTreeHelper.GetParent(prvok);
        }

        return null;
    }
}
