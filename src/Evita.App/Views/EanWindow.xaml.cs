using System.Windows;
using System.Windows.Controls;
using Evita.Models;
using Evita.Services;

namespace Evita.Views;

/// <summary>Výber údajov o výrobku nájdených podľa EAN (vlastná evidencia, Open Icecat, UPCitemdb).</summary>
public partial class EanWindow : Window
{
    private readonly IReadOnlyList<Volba> _kategorie;
    private int? _kategoriaId;

    public EanWindow(VysledokEan vysledok, IReadOnlyList<Volba> kategorie)
    {
        _kategorie = kategorie;
        InitializeComponent();
        Nadpis.Text = Preklad.F($"EAN {vysledok.Ean}");
        Spravy.Text = string.Join("\n", vysledok.Spravy);
        Spravy.Visibility = vysledok.Spravy.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Zdroje.ItemsSource = vysledok.Najdene;
        Zdroje.SelectedIndex = 0;
    }

    public string? VybranyNazov { get; private set; }
    public string? VybranyVyrobca { get; private set; }
    public string? VybranyModel { get; private set; }
    public int? VybranaKategoria { get; private set; }

    private void Zdroje_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Zdroje.SelectedItem is not UdajeProduktu udaje)
        {
            return;
        }

        Nazov.Text = udaje.Nazov ?? "";
        Vyrobca.Text = udaje.Vyrobca ?? "";
        Model.Text = udaje.Model ?? "";
        Popis.Text = udaje.Popis ?? "";

        // kategória: z vlastnej evidencie priamo, z internetu len ak rovnaká kategória v evidencii už je
        _kategoriaId = udaje.KategoriaId
                       ?? (udaje.Kategoria is { } nazov
                           ? _kategorie.FirstOrDefault(k => k.Id > 0 && Sk.BezDiakritiky(k.Nazov) == Sk.BezDiakritiky(nazov))?.Id
                           : null);
        if (_kategoriaId is int id && _kategorie.FirstOrDefault(k => k.Id == id) is { } kategoria)
        {
            Kategoria.Text = kategoria.Nazov;
            PrevziatKategoriu.IsEnabled = true;
            PrevziatKategoriu.IsChecked = true;
        }
        else
        {
            Kategoria.Text = udaje.Kategoria is { } navrh
                ? Preklad.F($"{navrh} – takú kategóriu v evidencii nemáte, vyberte ju v karte položky")
                : Preklad.T("(bez kategórie)");
            PrevziatKategoriu.IsEnabled = false;
            PrevziatKategoriu.IsChecked = false;
        }
    }

    private void Prevziat_Click(object sender, RoutedEventArgs e)
    {
        VybranyNazov = PrevziatNazov.IsChecked == true ? Sk.Prazdne(Nazov.Text) : null;
        VybranyVyrobca = PrevziatVyrobca.IsChecked == true ? Sk.Prazdne(Vyrobca.Text) : null;
        VybranyModel = PrevziatModel.IsChecked == true ? Sk.Prazdne(Model.Text) : null;
        VybranaKategoria = PrevziatKategoriu.IsChecked == true ? _kategoriaId : null;
        DialogResult = true;
    }
}
