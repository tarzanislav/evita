using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.ViewModels;

public enum FilterUloh
{
    Otvorene,
    Hotove,
    Vsetky,
}

/// <summary>Časť Úlohy (To Do): samostatný zoznam úloh s termínom, prioritou a voliteľným naviazaním na položku.</summary>
public partial class UlohyViewModel : ObservableObject, IObnovitelny
{
    private readonly UlohyService _sluzba = new();
    private bool _nacitavam;

    public IReadOnlyList<VolbaHodnoty<PrioritaUlohy>> Priority { get; } = new VolbaHodnoty<PrioritaUlohy>[]
    {
        new(PrioritaUlohy.Vysoka, Preklad.T("vysoká")),
        new(PrioritaUlohy.Normalna, Preklad.T("normálna")),
        new(PrioritaUlohy.Nizka, Preklad.T("nízka")),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JeOtvorene), nameof(JeHotove), nameof(JeVsetky))]
    private FilterUloh _filter = FilterUloh.Otvorene;

    public bool JeOtvorene { get => Filter == FilterUloh.Otvorene; set { if (value) Filter = FilterUloh.Otvorene; } }
    public bool JeHotove { get => Filter == FilterUloh.Hotove; set { if (value) Filter = FilterUloh.Hotove; } }
    public bool JeVsetky { get => Filter == FilterUloh.Vsetky; set { if (value) Filter = FilterUloh.Vsetky; } }

    partial void OnFilterChanged(FilterUloh value) => Obnov();

    [ObservableProperty] private List<UlohaRiadok> _zoznam = new();
    [ObservableProperty] private string _suhrn = "";
    [ObservableProperty] private bool _suHotove;

    // rýchle pridanie
    [ObservableProperty] private string _novyText = "";
    [ObservableProperty] private DateTime? _novyTermin;
    [ObservableProperty] private PrioritaUlohy _novaPriorita = PrioritaUlohy.Normalna;

    // vybraná úloha
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JeVybrana))]
    private UlohaRiadok? _vybrana;

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _popis = "";
    [ObservableProperty] private DateTime? _termin;
    [ObservableProperty] private PrioritaUlohy _priorita = PrioritaUlohy.Normalna;
    [ObservableProperty] private bool _hotovo;
    [ObservableProperty] private string _polozkaCislo = "";
    [ObservableProperty] private string _polozkaText = "";
    [ObservableProperty] private string _infoText = "";
    private int? _polozkaId;

    public bool JeVybrana => Vybrana is not null;

    public void Obnov()
    {
        var vybrana = Vybrana?.Id;
        try
        {
            var vsetky = _sluzba.Nacitaj(ajHotove: true);
            Zoznam = Filter switch
            {
                FilterUloh.Otvorene => vsetky.Where(u => !u.Hotovo).ToList(),
                FilterUloh.Hotove => vsetky.Where(u => u.Hotovo).ToList(),
                _ => vsetky,
            };
            var otvorene = vsetky.Count(u => !u.Hotovo);
            var poTermine = vsetky.Count(u => u.PoTermine);
            var hotove = vsetky.Count(u => u.Hotovo);
            SuHotove = hotove > 0;
            Suhrn = Preklad.F($"Otvorené: {otvorene} · po termíne: {poTermine} · hotové: {hotove}");
        }
        catch (Exception ex)
        {
            Suhrn = Preklad.F($"Úlohy sa nepodarilo načítať: {ex.GetBaseException().Message}");
        }

        Vybrana = vybrana is int id ? Zoznam.FirstOrDefault(u => u.Id == id) : null;
    }

    partial void OnVybranaChanged(UlohaRiadok? value)
    {
        _nacitavam = true;
        Text = value?.Text ?? "";
        Popis = value?.Popis ?? "";
        Termin = value?.Termin;
        Priorita = value?.Priorita ?? PrioritaUlohy.Normalna;
        Hotovo = value?.Hotovo ?? false;
        PolozkaCislo = value?.PolozkaCislo?.ToString() ?? "";
        InfoText = value is null
            ? ""
            : Preklad.F($"Vytvorená {Sk.DatumCas(value.Vytvorene)}") +
              (value.DatumHotovo is { } h ? " · " + Preklad.F($"dokončená {Sk.DatumCas(h)}") : "");
        _nacitavam = false;
        AktualizujPolozku();
    }

    partial void OnPolozkaCisloChanged(string value)
    {
        if (!_nacitavam)
        {
            AktualizujPolozku();
        }
    }

    private void AktualizujPolozku()
    {
        _polozkaId = null;
        if (string.IsNullOrWhiteSpace(PolozkaCislo))
        {
            PolozkaText = "";
            return;
        }

        if (!int.TryParse(PolozkaCislo.Trim(), out var cislo))
        {
            PolozkaText = Preklad.T("zadajte prírastkové číslo položky");
            return;
        }

        using var db = Databaza.Otvor();
        var p = db.Polozky.Where(x => x.PrirastkoveCislo == cislo).Select(x => new { x.Id, x.Nazov }).FirstOrDefault();
        if (p is null)
        {
            PolozkaText = Preklad.T("položka s týmto číslom neexistuje");
            return;
        }

        _polozkaId = p.Id;
        PolozkaText = p.Nazov;
    }

    /// <summary>Pridá úlohu z riadku rýchleho pridania.</summary>
    [RelayCommand]
    private void Pridat()
    {
        if (string.IsNullOrWhiteSpace(NovyText))
        {
            return;
        }

        try
        {
            var id = _sluzba.Pridaj(NovyText, NovyTermin, NovaPriorita);
            NovyText = "";
            NovyTermin = null;
            NovaPriorita = PrioritaUlohy.Normalna;
            if (Filter == FilterUloh.Hotove)
            {
                Filter = FilterUloh.Otvorene;
            }

            Obnov();
            Vybrana = Zoznam.FirstOrDefault(u => u.Id == id);
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
    }

    [RelayCommand]
    private void Ulozit()
    {
        if (Vybrana is not { } u)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(PolozkaCislo) && _polozkaId is null)
            {
                throw new EvitaException(Preklad.T("Položka s takým prírastkovým číslom neexistuje. Opravte číslo alebo ho vymažte."));
            }

            _sluzba.Uloz(new Uloha
            {
                Id = u.Id,
                Text = Text,
                Popis = Popis,
                Termin = Termin,
                Priorita = Priorita,
                Hotovo = Hotovo,
                PolozkaId = _polozkaId,
            });
            Obnov();
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
    }

    [RelayCommand]
    private void Vymazat()
    {
        if (Vybrana is not { } u || !Dialogy.Potvrd(Preklad.F($"Vymazať úlohu „{u.Text}“?")))
        {
            return;
        }

        _sluzba.Vymaz(u.Id);
        Vybrana = null;
        Obnov();
    }

    [RelayCommand]
    private void PrepnutHotovo(UlohaRiadok? uloha)
    {
        if (uloha is null)
        {
            return;
        }

        _sluzba.NastavHotovo(uloha.Id, !uloha.Hotovo);
        Obnov();
    }

    [RelayCommand]
    private void VymazatHotove()
    {
        if (!SuHotove || !Dialogy.Potvrd(Preklad.T("Vymazať všetky dokončené úlohy?")))
        {
            return;
        }

        var pocet = _sluzba.VymazHotove();
        Obnov();
        Dialogy.Info(Preklad.Pocet(pocet, "Vymazaná {0} dokončená úloha.", "Vymazané {0} dokončené úlohy.", "Vymazaných {0} dokončených úloh."));
    }

    [RelayCommand]
    private void OtvorPolozku()
    {
        if (_polozkaId is not int id)
        {
            return;
        }

        try
        {
            new Views.PolozkaEditWindow(new PolozkaEditViewModel(id, null)) { Owner = Dialogy.Vlastnik }.ShowDialog();
            AktualizujPolozku();
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
    }

    [RelayCommand]
    private void TerminDnes() => Termin = DateTime.Today;

    [RelayCommand]
    private void TerminZrusit() => Termin = null;
}
