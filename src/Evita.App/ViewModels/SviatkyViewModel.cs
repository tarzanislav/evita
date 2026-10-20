using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Pomocne;
using Evita.Services;
using Kalendarium;

namespace Evita.ViewModels;

/// <summary>Jeden sviatok v tabuľke – deň pracovného pokoja sa dá prepnúť pre zvolený rok.</summary>
public sealed partial class SviatokRiadok : ObservableObject
{
    private readonly Action<SviatokRiadok> _priZmene;

    public SviatokRiadok(SviatokDna sviatok, Action<SviatokRiadok> priZmene)
    {
        Sviatok = sviatok;
        _priZmene = priZmene;
        _denPracovnehoPokoja = sviatok.DenPracovnehoPokoja;
        _poznamka = sviatok.ZmenenePouzivatelom ? "zmenené ručne" : "";
    }

    public SviatokDna Sviatok { get; }
    public string Datum => Sviatok.Datum.ToString("d. M. yyyy", Sk.Kultura);
    public string DenVTyzdni => Sviatok.Datum.ToString("dddd", Sk.Kultura);
    public string Nazov => Sviatok.Nazov;
    public string Druh => Sviatok.DruhText;
    public bool Vikend => Sviatok.Datum.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    [ObservableProperty] private bool _denPracovnehoPokoja;
    [ObservableProperty] private string _poznamka;

    partial void OnDenPracovnehoPokojaChanged(bool value)
    {
        Poznamka = "zmenené ručne";
        _priZmene(this);
    }
}

/// <summary>
/// Prehľad sviatkov v roku. Zákon sa často mení, preto sa pri každom sviatku dá pre konkrétny rok
/// prepnúť, či je dňom pracovného pokoja – bez novej verzie programu.
/// </summary>
public partial class SviatkyViewModel : ObservableObject, IObnovitelny
{
    public List<int> Roky { get; } = Enumerable.Range(DateTime.Today.Year - 2, 8).ToList();

    [ObservableProperty] private int _rok = DateTime.Today.Year;
    [ObservableProperty] private bool _ukazPamatne;
    [ObservableProperty] private List<SviatokRiadok> _riadky = new();
    [ObservableProperty] private string _suhrn = "";

    partial void OnRokChanged(int value) => Obnov();

    partial void OnUkazPamatneChanged(bool value) => Obnov();

    public void Obnov()
    {
        try
        {
            var sviatky = KalendariumDb.Spolocna.Sviatky(Rok);
            Riadky = sviatky
                .Where(s => UkazPamatne || s.Druh != DruhSviatku.PamatnyDen)
                .Select(s => new SviatokRiadok(s, PriZmene))
                .ToList();
            AktualizujSuhrn(sviatky);
        }
        catch (Exception ex)
        {
            Riadky = new List<SviatokRiadok>();
            Suhrn = $"Kalendárium nie je dostupné: {ex.GetBaseException().Message}";
        }
    }

    [RelayCommand]
    private void VratitPodlaZakona()
    {
        var zmenene = Riadky.Where(r => r.Poznamka.Length > 0).ToList();
        if (zmenene.Count == 0)
        {
            Dialogy.Info($"V roku {Rok} nie sú žiadne ručné zmeny.");
            return;
        }

        if (!Dialogy.Potvrd($"Zrušiť ručné zmeny pri {zmenene.Count} sviatkoch v roku {Rok} a vrátiť pravidlá zo zákona?"))
        {
            return;
        }

        foreach (var riadok in zmenene)
        {
            KalendariumDb.Spolocna.ZrusZmenu(riadok.Sviatok.Id, Rok);
        }

        Obnov();
    }

    private void PriZmene(SviatokRiadok riadok)
    {
        KalendariumDb.Spolocna.NastavDenPracovnehoPokoja(riadok.Sviatok.Id, Rok, riadok.DenPracovnehoPokoja);
        AktualizujSuhrn(KalendariumDb.Spolocna.Sviatky(Rok));
    }

    private void AktualizujSuhrn(IReadOnlyList<SviatokDna> sviatky)
    {
        var volne = sviatky.Where(s => s.DenPracovnehoPokoja).Select(s => s.Datum).Distinct().ToList();
        var vPracovneDni = volne.Count(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
        Suhrn = $"V roku {Rok} je {volne.Count} dní pracovného pokoja zo sviatkov, z toho {vPracovneDni} pripadá na pracovný deň.";
    }
}
