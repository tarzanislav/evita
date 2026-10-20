using System.Windows;
using System.Windows.Threading;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.Views;

/// <summary>Malé okienko vpravo dole s pripomienkou udalosti, ktorá čoskoro začne.</summary>
public partial class UpozornenieWindow : Window
{
    private readonly DispatcherTimer _zatvorenie = new() { Interval = TimeSpan.FromMinutes(2) };

    public UpozornenieWindow(VyskytUdalosti vyskyt)
    {
        InitializeComponent();
        var minuty = Math.Max(0, (int)Math.Round((vyskyt.Zaciatok - DateTime.Now).TotalMinutes));
        Kedy.Text = minuty == 0 ? Preklad.F($"Teraz · {vyskyt.Zaciatok:H:mm}") : Preklad.F($"O {minuty} min · {vyskyt.Zaciatok:H:mm}");
        Nazov.Text = vyskyt.Nazov;
        Detail.Text = string.Join(" · ", new[] { vyskyt.Typ, vyskyt.Naviazanie }.Where(t => !string.IsNullOrWhiteSpace(t)));
        Pruh.BorderBrush = (System.Windows.Media.Brush)new FarbaNaStetec().Convert(vyskyt.Farba, typeof(System.Windows.Media.Brush), null!, Sk.Kultura);
        _zatvorenie.Tick += (_, _) => Close();
        Loaded += (_, _) =>
        {
            var plocha = SystemParameters.WorkArea;
            Left = plocha.Right - ActualWidth - 16;
            Top = plocha.Bottom - ActualHeight - 16;
            _zatvorenie.Start();
            System.Media.SystemSounds.Asterisk.Play();
        };
    }

    public event Action? OtvoritKalendar;

    private void Kalendar_Click(object sender, RoutedEventArgs e)
    {
        OtvoritKalendar?.Invoke();
        Close();
    }

    private void Zavriet_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _zatvorenie.Stop();
        base.OnClosed(e);
    }
}
