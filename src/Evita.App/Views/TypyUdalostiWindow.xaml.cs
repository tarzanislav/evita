using System.Windows;
using System.Windows.Controls;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.Views;

/// <summary>Správa typov udalostí kalendára (názov a farba).</summary>
public partial class TypyUdalostiWindow : Window
{
    private readonly KalendarService _sluzba = new();
    private string _farba = KalendarService.Paleta[^1];
    private int _id;

    public TypyUdalostiWindow()
    {
        InitializeComponent();
        Paleta.ItemsSource = KalendarService.Paleta;
        Nacitaj(null);
    }

    private void Nacitaj(int? vybrat)
    {
        var typy = _sluzba.Typy();
        Zoznam.ItemsSource = typy;
        Zoznam.SelectedItem = typy.FirstOrDefault(t => t.Id == vybrat) ?? typy.FirstOrDefault();
    }

    private void NastavFarbu(string farba)
    {
        _farba = farba;
        Vzorka.Background = (System.Windows.Media.Brush)new FarbaNaStetec().Convert(farba, typeof(System.Windows.Media.Brush), null!, Sk.Kultura);
    }

    private void Zoznam_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Zoznam.SelectedItem is TypUdalosti typ)
        {
            _id = typ.Id;
            PoleNazov.Text = typ.Nazov;
            NastavFarbu(typ.Farba);
        }
    }

    private void Farba_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string farba })
        {
            NastavFarbu(farba);
        }
    }

    private void Novy_Click(object sender, RoutedEventArgs e)
    {
        _id = 0;
        Zoznam.SelectedItem = null;
        PoleNazov.Text = "";
        NastavFarbu(KalendarService.Paleta[_sluzba.Typy().Count % KalendarService.Paleta.Length]);
        PoleNazov.Focus();
    }

    private void Ulozit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Nacitaj(_sluzba.UlozTyp(_id, PoleNazov.Text, _farba));
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
    }

    private void Vymazat_Click(object sender, RoutedEventArgs e)
    {
        if (_id == 0 || !Dialogy.Potvrd($"Vymazať typ „{PoleNazov.Text}“?"))
        {
            return;
        }

        try
        {
            if (!_sluzba.VymazTyp(_id))
            {
                Dialogy.Info("Typ používajú niektoré udalosti, preto sa nevymazal, len sa skryl z ponuky pri nových udalostiach.");
            }

            Nacitaj(null);
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
    }

    private void Zavriet_Click(object sender, RoutedEventArgs e) => Close();
}
