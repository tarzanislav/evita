using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.ViewModels;

/// <summary>Udalosť v zozname na prepojenie poznámky.</summary>
public sealed record VolbaUdalosti(int Id, string Nazov);

/// <summary>Úprava jednej poznámky – v časti Poznámky aj v samostatnom okne (z kalendára, karty položky, udalosti).</summary>
public partial class PoznamkaEditViewModel : ObservableObject
{
    private readonly PoznamkyService _sluzba = new();
    private bool _nacitavam;

    /// <param name="id">Id upravovanej poznámky alebo null pre novú.</param>
    /// <param name="datum">Deň v kalendári pre novú poznámku.</param>
    /// <param name="polozkaId">Položka pre novú poznámku.</param>
    /// <param name="udalostId">Udalosť pre novú poznámku.</param>
    public PoznamkaEditViewModel(int? id, DateTime? datum = null, int? polozkaId = null, int? udalostId = null)
    {
        _nacitavam = true;
        Poznamka? p = null;
        if (id is int upravovana)
        {
            p = _sluzba.Nacitaj(upravovana) ?? throw new EvitaException(Preklad.T("Poznámka už neexistuje."));
            Id = p.Id;
        }

        Udalosti = NacitajUdalosti(p?.UdalostId ?? udalostId);
        if (p is null)
        {
            JeNova = true;
            _farba = PoznamkyService.Farby[0];
            _datum = datum?.Date;
            _udalostId = udalostId ?? 0;
            _polozkaCislo = CisloPolozky(polozkaId);
        }
        else
        {
            _nazov = p.Nazov;
            _text = p.Text;
            _farba = p.Farba;
            _pripnuta = p.Pripnuta;
            _datum = p.Datum;
            _udalostId = p.UdalostId ?? 0;
            _polozkaCislo = p.Polozka?.PrirastkoveCislo.ToString() ?? "";
            Vytvorene = Sk.DatumCas(p.Vytvorene);
            ZmeneneText = Sk.DatumCas(p.Zmenene);
        }

        AktualizujPolozku();
        _nacitavam = false;
        Zmenena = false;
    }

    public int Id { get; private set; }
    public bool JeNova { get; private set; }
    public string Vytvorene { get; } = "";
    [ObservableProperty] private string _zmeneneText = "";

    public string Titulok => JeNova ? Preklad.T("Nová poznámka") : Preklad.F($"Poznámka – {Nazov}");

    public IReadOnlyList<string> Farby => PoznamkyService.Farby;
    public List<VolbaUdalosti> Udalosti { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulok))]
    private string _nazov = "";

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _farba = PoznamkyService.Farby[0];
    [ObservableProperty] private bool _pripnuta;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaDatum))]
    private DateTime? _datum;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaUdalost))]
    private int _udalostId;

    [ObservableProperty] private string _polozkaCislo = "";
    [ObservableProperty] private string _polozkaText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaPolozku))]
    private int? _polozkaId;

    /// <summary>Zmenila sa poznámka od načítania alebo uloženia?</summary>
    [ObservableProperty] private bool _zmenena;

    public bool MaDatum => Datum is not null;
    public bool MaUdalost => UdalostId > 0;
    public bool MaPolozku => PolozkaId is not null;

    private static readonly HashSet<string> Upravitelne = new()
    {
        nameof(Nazov), nameof(Text), nameof(Farba), nameof(Pripnuta), nameof(Datum), nameof(UdalostId), nameof(PolozkaCislo),
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_nacitavam && e.PropertyName is { } nazov && Upravitelne.Contains(nazov))
        {
            Zmenena = true;
        }
    }

    partial void OnPolozkaCisloChanged(string value) => AktualizujPolozku();

    private static string CisloPolozky(int? polozkaId)
    {
        if (polozkaId is not int id)
        {
            return "";
        }

        using var db = Databaza.Otvor();
        return db.Polozky.Where(x => x.Id == id).Select(x => (int?)x.PrirastkoveCislo).FirstOrDefault()?.ToString() ?? "";
    }

    private static List<VolbaUdalosti> NacitajUdalosti(int? aktualna)
    {
        using var db = Databaza.Otvor();
        var od = DateTime.Today.AddYears(-1);
        var udalosti = db.Udalosti
            .Where(u => u.Zaciatok >= od || u.Opakovanie != OpakovanieUdalosti.Ziadne || u.Id == aktualna)
            .OrderBy(u => u.Zaciatok)
            .Select(u => new { u.Id, u.Nazov, u.Zaciatok })
            .ToList()
            .Select(u => new VolbaUdalosti(u.Id, $"{Sk.Datum(u.Zaciatok)} – {u.Nazov}"))
            .ToList();
        udalosti.Insert(0, new VolbaUdalosti(0, Preklad.T("(žiadna)")));
        return udalosti;
    }

    private void AktualizujPolozku()
    {
        PolozkaId = null;
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

        PolozkaId = p.Id;
        PolozkaText = p.Nazov;
    }

    /// <summary>Po uložení alebo vymazaní (true = vymazaná).</summary>
    public event Action<bool>? Hotovo;

    /// <summary>Uloží poznámku. Vráti false pri chybe (hlásenie už zobrazil).</summary>
    public bool Uloz()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(PolozkaCislo) && PolozkaId is null)
            {
                throw new EvitaException(Preklad.T("Položka s takým prírastkovým číslom neexistuje. Opravte číslo alebo ho vymažte."));
            }

            Id = _sluzba.Uloz(new Poznamka
            {
                Id = Id,
                Nazov = Nazov,
                Text = Text,
                Farba = Farba,
                Pripnuta = Pripnuta,
                Datum = Datum,
                PolozkaId = PolozkaId,
                UdalostId = UdalostId > 0 ? UdalostId : null,
            });
            if (string.IsNullOrWhiteSpace(Nazov))
            {
                _nacitavam = true;
                Nazov = PoznamkyService.NazovZTextu(Text);
                _nacitavam = false;
            }

            JeNova = false;
            OnPropertyChanged(nameof(JeNova));
            OnPropertyChanged(nameof(Titulok));
            ZmeneneText = Sk.DatumCas(DateTime.Now);
            Zmenena = false;
            return true;
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Poznámku sa nepodarilo uložiť."), ex);
        }

        return false;
    }

    [RelayCommand]
    private void Ulozit()
    {
        if (Uloz())
        {
            Hotovo?.Invoke(false);
        }
    }

    [RelayCommand]
    private void Vymazat()
    {
        if (JeNova)
        {
            Zmenena = false;
            Hotovo?.Invoke(true);
            return;
        }

        if (!Dialogy.Potvrd(Preklad.F($"Vymazať poznámku „{Nazov}“?")))
        {
            return;
        }

        _sluzba.Vymaz(Id);
        Zmenena = false;
        Hotovo?.Invoke(true);
    }

    [RelayCommand]
    private void ZvolFarbu(string? farba)
    {
        if (farba is not null)
        {
            Farba = farba;
        }
    }

    [RelayCommand]
    private void ZrusitDatum() => Datum = null;

    [RelayCommand]
    private void Dnes() => Datum = DateTime.Today;

    [RelayCommand]
    private void OtvorPolozku()
    {
        if (PolozkaId is not int id)
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
    private void OtvorUdalost()
    {
        if (UdalostId <= 0)
        {
            return;
        }

        try
        {
            new Views.UdalostWindow(new UdalostEditViewModel(UdalostId, null, null)) { Owner = Dialogy.Vlastnik }.ShowDialog();
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
    }
}
