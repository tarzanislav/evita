using System.Windows;
using System.Windows.Threading;
using Evita.Pomocne;
using Evita.Services;
using Evita.ViewModels;

namespace Evita.Views;

/// <summary>Okno inventúry. Kým je otvorené, všetky naskenované kódy (čítačka, kamera, mobil) idú do inventúry.</summary>
public partial class InventuraWindow : Window
{
    private static InventuraWindow? _otvorene;
    private readonly Action? _poZatvoreni;

    public InventuraWindow(InventuraViewModel model, Action? poZatvoreni = null)
    {
        Model = model;
        _poZatvoreni = poZatvoreni;
        DataContext = model;
        InitializeComponent();
        SkenerCentrum.Zaregistruj(model);
        Activated += (_, _) =>
        {
            SkenerCentrum.Zaregistruj(model);
            Dispatcher.InvokeAsync(() => Pole.Focus(), DispatcherPriority.Input);
        };
        Closed += (_, _) =>
        {
            SkenerCentrum.Odregistruj(model);
            if (_otvorene == this)
            {
                _otvorene = null;
            }

            _poZatvoreni?.Invoke();
        };
    }

    public InventuraViewModel Model { get; }

    /// <summary>Otvorí inventúru (ak je už otvorená, len ju zobrazí).</summary>
    public static InventuraWindow Otvor(int inventuraId, Action? poZatvoreni = null)
    {
        if (_otvorene is { } otvorene)
        {
            if (otvorene.Model.InventuraId == inventuraId)
            {
                otvorene.Activate();
                return otvorene;
            }

            otvorene.Close();
        }

        var okno = new InventuraWindow(new InventuraViewModel(inventuraId), poZatvoreni)
        {
            Owner = Application.Current?.MainWindow is { IsVisible: true } hlavne ? hlavne : null,
        };
        _otvorene = okno;
        okno.Show();
        return okno;
    }

    private void Zavriet_Click(object sender, RoutedEventArgs e) => Close();
}
