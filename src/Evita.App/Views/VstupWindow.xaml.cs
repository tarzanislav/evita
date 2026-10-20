using System.Windows;
using System.Windows.Controls;

namespace Evita.Views;

public partial class VstupWindow : Window
{
    public VstupWindow(string titulok, string otazka, string predvolene, bool viacRiadkov)
    {
        InitializeComponent();
        Title = titulok;
        Otazka.Text = otazka;
        Pole.Text = predvolene;

        if (viacRiadkov)
        {
            Pole.AcceptsReturn = true;
            Pole.TextWrapping = TextWrapping.Wrap;
            Pole.Height = 140;
            Pole.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Pole.VerticalContentAlignment = VerticalAlignment.Top;
        }

        Loaded += (_, _) =>
        {
            Pole.Focus();
            Pole.SelectAll();
        };
    }

    public string Text => Pole.Text;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
