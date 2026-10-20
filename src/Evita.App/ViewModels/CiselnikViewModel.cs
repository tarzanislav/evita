using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.ViewModels;

/// <summary>Jeden číselník (kategórie, miestnosti alebo osoby) – zoznam a formulár.</summary>
public partial class CiselnikViewModel : ObservableObject, IObnovitelny
{
    private readonly MainViewModel _hlavne;
    private readonly CiselnikService _sluzba = new();

    public CiselnikViewModel(TypCiselnika typ, MainViewModel hlavne)
    {
        Typ = typ;
        _hlavne = hlavne;
        switch (typ)
        {
            case TypCiselnika.Kategorie:
                Nadpis = "Kategórie";
                NovaText = "Nová kategória";
                Pole1Popis = "Názov kategórie";
                StlpecDetail = "";
                break;
            case TypCiselnika.Miestnosti:
                Nadpis = "Miestnosti";
                NovaText = "Nová miestnosť";
                Pole1Popis = "Názov miestnosti";
                Pole2Popis = "Budova alebo poschodie (nepovinné)";
                Pole3Popis = "Poznámka (nepovinné)";
                StlpecDetail = "Budova";
                break;
            default:
                Nadpis = "Osoby";
                NovaText = "Nová osoba";
                Pole1Popis = "Meno";
                Pole2Popis = "Priezvisko (alebo názov útvaru)";
                Pole3Popis = "Funkcia alebo útvar (nepovinné)";
                StlpecDetail = "Funkcia";
                break;
        }
    }

    public TypCiselnika Typ { get; }
    public string Nadpis { get; }
    public string NovaText { get; }
    public string Pole1Popis { get; }
    public string? Pole2Popis { get; }
    public string? Pole3Popis { get; }
    public string StlpecDetail { get; }
    public bool MaPole2 => Pole2Popis is not null;
    public bool MaPole3 => Pole3Popis is not null;
    public bool MaDetail => StlpecDetail.Length > 0;

    public string Napoveda => Typ == TypCiselnika.Kategorie
        ? "Zoznam je vždy zoradený abecedne. Kategóriu, ktorú používajú položky, nemožno vymazať, len deaktivovať – potom sa nebude ponúkať pri nových položkách."
        : "Položku, ktorú používajú záznamy v evidencii, nemožno vymazať, len deaktivovať – potom sa nebude ponúkať pri nových položkách.";

    [ObservableProperty] private List<CiselnikRiadok> _polozky = new();
    [ObservableProperty] private CiselnikRiadok? _vybrana;
    [ObservableProperty] private string _pole1 = "";
    [ObservableProperty] private string _pole2 = "";
    [ObservableProperty] private string _pole3 = "";
    [ObservableProperty] private string _formularNadpis = "";
    [ObservableProperty] private bool _upravujem;

    public void Obnov()
    {
        var vybraneId = Vybrana?.Id;
        Polozky = _sluzba.Nacitaj(Typ);
        Vybrana = Polozky.FirstOrDefault(p => p.Id == vybraneId);
        if (Vybrana is null)
        {
            Nova();
        }
    }

    partial void OnVybranaChanged(CiselnikRiadok? value)
    {
        if (value is null)
        {
            return;
        }

        Pole1 = value.Pole1;
        Pole2 = value.Pole2 ?? "";
        Pole3 = value.Pole3 ?? "";
        FormularNadpis = $"Úprava: {value.Nazov}";
        Upravujem = true;
    }

    [RelayCommand]
    private void Nova()
    {
        Vybrana = null;
        Pole1 = "";
        Pole2 = "";
        Pole3 = "";
        FormularNadpis = NovaText;
        Upravujem = false;
    }

    [RelayCommand]
    private void Ulozit()
    {
        var id = _sluzba.Uloz(Typ, Upravujem ? Vybrana?.Id ?? 0 : 0, Pole1, Pole2, Pole3);
        _hlavne.DataZmenene();
        Polozky = _sluzba.Nacitaj(Typ);
        Vybrana = Polozky.FirstOrDefault(p => p.Id == id);
    }

    [RelayCommand]
    private void PrepnutAktivnu()
    {
        if (Vybrana is not { } v)
        {
            return;
        }

        _sluzba.PrepniAktivnu(Typ, v.Id);
        _hlavne.DataZmenene();
        Obnov();
    }

    [RelayCommand]
    private void Vymazat()
    {
        if (Vybrana is not { } v)
        {
            return;
        }

        if (v.Pocet > 0)
        {
            Dialogy.Upozornenie($"„{v.Nazov}“ používa {v.Pocet} položiek v evidencii, preto sa nedá vymazať. Môžete ju deaktivovať.");
            return;
        }

        if (!Dialogy.Potvrd($"Vymazať „{v.Nazov}“?"))
        {
            return;
        }

        _sluzba.Vymaz(Typ, v.Id);
        _hlavne.DataZmenene();
        Vybrana = null;
        Obnov();
    }
}
