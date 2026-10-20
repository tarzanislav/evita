using System.Windows;
using Evita.Pomocne;

namespace Evita.Views;

/// <summary>Zadanie hesla – nového (dvakrát) alebo existujúceho (raz).</summary>
public partial class HesloWindow : Window
{
    private const int MinimalnaDlzka = 6;
    private readonly bool _nove;

    public HesloWindow(bool nove, string? popis)
    {
        InitializeComponent();
        _nove = nove;
        if (nove)
        {
            Nadpis.Text = "Nové heslo zálohy";
            Popis.Text = popis ?? $"Záloha bude zašifrovaná (AES-256). Heslo musí mať aspoň {MinimalnaDlzka} znakov. " +
                         "Bez hesla sa zo zálohy nedá nič obnoviť – heslo si dobre uschovajte.";
        }
        else
        {
            Nadpis.Text = "Záloha je chránená heslom";
            Popis.Text = popis ?? "Zadajte heslo, ktoré ste použili pri vytváraní zálohy.";
            PanelOpakovanie.Visibility = Visibility.Collapsed;
        }

        Loaded += (_, _) => Heslo1.Focus();
    }

    public string Heslo => Heslo1.Password;

    /// <summary>Vypýta nové heslo (dvakrát). Vráti null, ak používateľ zrušil.</summary>
    public static string? Nove(string? popis = null)
    {
        if (Dialogy.TichyRezim)
        {
            return null;
        }

        var okno = new HesloWindow(true, popis) { Owner = Dialogy.Vlastnik };
        return okno.ShowDialog() == true ? okno.Heslo : null;
    }

    /// <summary>Vypýta existujúce heslo. Vráti null, ak používateľ zrušil.</summary>
    public static string? Zadaj(string? popis = null)
    {
        if (Dialogy.TichyRezim)
        {
            return null;
        }

        var okno = new HesloWindow(false, popis) { Owner = Dialogy.Vlastnik };
        return okno.ShowDialog() == true ? okno.Heslo : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string? chyba = null;
        if (Heslo1.Password.Length == 0)
        {
            chyba = "Zadajte heslo.";
        }
        else if (_nove && Heslo1.Password.Length < MinimalnaDlzka)
        {
            chyba = $"Heslo musí mať aspoň {MinimalnaDlzka} znakov.";
        }
        else if (_nove && Heslo1.Password != Heslo2.Password)
        {
            chyba = "Heslá sa nezhodujú.";
        }

        if (chyba is not null)
        {
            Chyba.Text = chyba;
            Chyba.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
