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
                Nadpis = Preklad.T("Kategórie");
                NovaText = Preklad.T("Nová kategória");
                Pole1Popis = Preklad.T("Názov kategórie");
                StlpecDetail = "";
                break;
            case TypCiselnika.Miestnosti:
                Nadpis = Preklad.T("Miestnosti");
                NovaText = Preklad.T("Nová miestnosť");
                Pole1Popis = Preklad.T("Názov miestnosti");
                Pole2Popis = Preklad.T("Budova alebo poschodie (nepovinné)");
                Pole3Popis = Preklad.T("Poznámka (nepovinné)");
                StlpecDetail = Preklad.T("Budova");
                break;
            default:
                Nadpis = Preklad.T("Osoby");
                NovaText = Preklad.T("Nová osoba");
                Pole1Popis = Preklad.T("Meno");
                Pole2Popis = Preklad.T("Priezvisko (alebo názov útvaru)");
                Pole3Popis = Preklad.T("Funkcia alebo útvar (nepovinné)");
                StlpecDetail = Preklad.T("Funkcia");
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
        ? Preklad.T("Zoznam je vždy zoradený abecedne. Kategóriu, ktorú používajú položky, nemožno vymazať, len deaktivovať – potom sa nebude ponúkať pri nových položkách.")
        : Preklad.T("Položku, ktorú používajú záznamy v evidencii, nemožno vymazať, len deaktivovať – potom sa nebude ponúkať pri nových položkách.");

    [ObservableProperty] private List<CiselnikRiadok> _polozky = new();
    [ObservableProperty] private CiselnikRiadok? _vybrana;
    [ObservableProperty] private string _pole1 = "";
    [ObservableProperty] private string _pole2 = "";
    [ObservableProperty] private string _pole3 = "";
    [ObservableProperty] private string _formularNadpis = "";
    [ObservableProperty] private bool _upravujem;

    // ------------------------------------------------------------------ ikona a farba kategórie

    public bool SuKategorie => Typ == TypCiselnika.Kategorie;

    /// <summary>Všetky ikony na výber (každá v predvolenej farbe).</summary>
    public List<FarebnaIkona> IkonyNaVyber { get; } = IkonyKategorii.Vsetky.Select(i => i.SFarbou(null)).ToList();

    public IReadOnlyList<string> FarbyNaVyber => IkonyKategorii.Farby;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NahladIkony), nameof(PopisIkony))]
    private bool _ikonaAutomaticky = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NahladIkony), nameof(PopisIkony))]
    private FarebnaIkona? _vybranaIkona;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NahladIkony))]
    private bool _farbaPredvolena = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NahladIkony))]
    private string? _vybranaFarba;

    /// <summary>Ikona tak, ako sa uloží (automatická podľa názvu alebo zvolená, vo zvolenej farbe).</summary>
    public FarebnaIkona NahladIkony => IkonyKategorii.Pre(
        IkonaAutomaticky ? null : VybranaIkona?.Ikona.Kluc, FarbaPredvolena ? null : VybranaFarba, Pole1);

    public string PopisIkony => IkonaAutomaticky
        ? Preklad.F($"Ikona: {NahladIkony.Nazov} (podľa názvu)")
        : Preklad.F($"Ikona: {NahladIkony.Nazov}");

    partial void OnPole1Changed(string value)
    {
        OnPropertyChanged(nameof(NahladIkony));
        OnPropertyChanged(nameof(PopisIkony));
    }

    partial void OnVybranaIkonaChanged(FarebnaIkona? value)
    {
        if (value is not null)
        {
            IkonaAutomaticky = false;
        }
    }

    partial void OnVybranaFarbaChanged(string? value)
    {
        if (value is not null)
        {
            FarbaPredvolena = false;
        }
    }

    private void NastavIkonuFormulara(string? kluc, string? farba)
    {
        VybranaIkona = kluc is null ? null : IkonyNaVyber.FirstOrDefault(i => i.Ikona.Kluc == kluc);
        IkonaAutomaticky = VybranaIkona is null;
        VybranaFarba = IkonyKategorii.PlatnaFarba(farba) is { } f ? FarbyNaVyber.FirstOrDefault(x => x == f) ?? f : null;
        FarbaPredvolena = VybranaFarba is null;
    }

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
        NastavIkonuFormulara(value.IkonaKluc, value.IkonaFarba);
        FormularNadpis = Preklad.F($"Úprava: {value.Nazov}");
        Upravujem = true;
    }

    [RelayCommand]
    private void Nova()
    {
        Vybrana = null;
        Pole1 = "";
        Pole2 = "";
        Pole3 = "";
        NastavIkonuFormulara(null, null);
        FormularNadpis = NovaText;
        Upravujem = false;
    }

    [RelayCommand]
    private void Ulozit()
    {
        var id = _sluzba.Uloz(Typ, Upravujem ? Vybrana?.Id ?? 0 : 0, Pole1, Pole2, Pole3);
        if (SuKategorie)
        {
            _sluzba.NastavIkonu(id, IkonaAutomaticky ? null : VybranaIkona?.Ikona.Kluc, FarbaPredvolena ? null : VybranaFarba);
        }
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
            Dialogy.Upozornenie(Preklad.F($"„{v.Nazov}“ používa {v.Pocet} položiek v evidencii, preto sa nedá vymazať. Môžete ju deaktivovať."));
            return;
        }

        if (!Dialogy.Potvrd(Preklad.F($"Vymazať „{v.Nazov}“?")))
        {
            return;
        }

        _sluzba.Vymaz(Typ, v.Id);
        _hlavne.DataZmenene();
        Vybrana = null;
        Obnov();
    }
}
