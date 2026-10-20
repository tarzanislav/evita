using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.ViewModels;

public sealed record VolbaHodnoty<T>(T Hodnota, string Nazov);

/// <summary>Okno udalosti alebo úlohy kalendára – nová aj úprava.</summary>
public partial class UdalostEditViewModel : ObservableObject
{
    private readonly KalendarService _sluzba = new();
    private readonly int _id;

    /// <param name="id">Id upravovanej udalosti alebo null pre novú.</param>
    /// <param name="den">Deň novej udalosti.</param>
    /// <param name="polozkaId">Položka, ku ktorej sa nová udalosť naviaže.</param>
    public UdalostEditViewModel(int? id, DateTime? den, int? polozkaId)
    {
        var ciselniky = new CiselnikService();
        Udalost? u = null;
        if (id is int upravovana)
        {
            u = _sluzba.Nacitaj(upravovana) ?? throw new EvitaException(Preklad.T("Udalosť už neexistuje."));
            _id = u.Id;
        }

        Typy = _sluzba.Typy().Where(t => t.Aktivny || t.Id == u?.TypUdalostiId).ToList();
        Miestnosti = ciselniky.Volby(TypCiselnika.Miestnosti, u?.MiestnostId, Preklad.T("(žiadna)"));
        Osoby = ciselniky.Volby(TypCiselnika.Osoby, u?.OsobaId, Preklad.T("(nikto)"));

        if (u is null)
        {
            JeNova = true;
            Titulok = Preklad.T("Nová udalosť");
            var zaciatok = (den ?? DateTime.Today).Date;
            _datumOd = zaciatok;
            _datumDo = zaciatok;
            _casOd = "8:00";
            _casDo = "9:00";
            _typId = KalendarService.NajdiTyp(Typy, "iné")?.Id ?? Typy.FirstOrDefault()?.Id ?? 0;
            if (polozkaId is int p)
            {
                using var db = Databaza.Otvor();
                _polozkaCislo = db.Polozky.Where(x => x.Id == p).Select(x => (int?)x.PrirastkoveCislo).FirstOrDefault()?.ToString() ?? "";
                _typId = KalendarService.NajdiTyp(Typy, "servis")?.Id ?? _typId;
            }
        }
        else
        {
            Titulok = Preklad.F($"Udalosť – {u.Nazov}");
            _nazov = u.Nazov;
            _popis = u.Popis ?? "";
            _typId = u.TypUdalostiId;
            _jeUloha = u.Druh == DruhUdalosti.Uloha;
            _celyDen = u.CelyDen;
            _datumOd = u.Zaciatok.Date;
            _casOd = u.CelyDen ? "8:00" : u.Zaciatok.ToString("H:mm", CultureInfo.InvariantCulture);
            _datumDo = (u.Koniec ?? u.Zaciatok).Date;
            _casDo = u.Koniec is { } k && !u.CelyDen ? k.ToString("H:mm", CultureInfo.InvariantCulture) : u.Zaciatok.AddHours(1).ToString("H:mm", CultureInfo.InvariantCulture);
            _opakovanie = u.Opakovanie;
            _opakovanieDo = u.OpakovanieDo;
            _pripomienka = u.Pripomienka;
            _miestnostId = u.MiestnostId ?? 0;
            _osobaId = u.OsobaId ?? 0;
            _stav = u.Stav;
            if (u.PolozkaId is int pid)
            {
                using var db = Databaza.Otvor();
                _polozkaCislo = db.Polozky.Where(x => x.Id == pid).Select(x => (int?)x.PrirastkoveCislo).FirstOrDefault()?.ToString() ?? "";
            }
        }

        AktualizujPolozku();
        NacitajPoznamky();
    }

    public bool JeNova { get; }

    /// <summary>Poznámky prepojené s udalosťou.</summary>
    [ObservableProperty] private List<PoznamkaRiadok> _poznamky = new();

    private void NacitajPoznamky()
    {
        if (JeNova)
        {
            return;
        }

        try
        {
            Poznamky = new PoznamkyService().PreUdalost(_id);
        }
        catch (Exception)
        {
            Poznamky = new List<PoznamkaRiadok>();
        }
    }

    [RelayCommand]
    private void NovaPoznamka()
    {
        if (JeNova)
        {
            return;
        }

        if (Views.PoznamkaWindow.Otvor(new PoznamkaEditViewModel(null, DatumOd, udalostId: _id)))
        {
            NacitajPoznamky();
        }
    }

    [RelayCommand]
    private void OtvorPoznamku(PoznamkaRiadok? poznamka)
    {
        if (poznamka is null)
        {
            return;
        }

        try
        {
            Views.PoznamkaWindow.Otvor(new PoznamkaEditViewModel(poznamka.Id));
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }

        NacitajPoznamky();
    }
    public string Titulok { get; }

    public List<TypUdalosti> Typy { get; }
    public List<Volba> Miestnosti { get; }
    public List<Volba> Osoby { get; }

    public IReadOnlyList<VolbaHodnoty<OpakovanieUdalosti>> Opakovania { get; } = new VolbaHodnoty<OpakovanieUdalosti>[]
    {
        new(OpakovanieUdalosti.Ziadne, Preklad.T("neopakuje sa")),
        new(OpakovanieUdalosti.Denne, Preklad.T("denne")),
        new(OpakovanieUdalosti.Tyzdenne, Preklad.T("týždenne")),
        new(OpakovanieUdalosti.Mesacne, Preklad.T("mesačne")),
        new(OpakovanieUdalosti.Rocne, Preklad.T("ročne")),
    };

    public IReadOnlyList<VolbaHodnoty<PripomienkaUdalosti>> Pripomienky { get; } = new VolbaHodnoty<PripomienkaUdalosti>[]
    {
        new(PripomienkaUdalosti.Ziadna, Preklad.T("bez pripomienky")),
        new(PripomienkaUdalosti.VDen, Preklad.T("v deň udalosti")),
        new(PripomienkaUdalosti.DenVopred, Preklad.T("deň vopred")),
        new(PripomienkaUdalosti.TyzdenVopred, Preklad.T("týždeň vopred")),
    };

    public IReadOnlyList<VolbaHodnoty<StavUdalosti>> Stavy { get; } = new VolbaHodnoty<StavUdalosti>[]
    {
        new(StavUdalosti.Naplanovane, Preklad.T("naplánované")),
        new(StavUdalosti.Hotovo, Preklad.T("hotovo")),
        new(StavUdalosti.Zrusene, Preklad.T("zrušené")),
    };

    [ObservableProperty] private string _nazov = "";
    [ObservableProperty] private string _popis = "";
    [ObservableProperty] private int _typId;
    [ObservableProperty] private bool _jeUloha;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UkazCas))]
    private bool _celyDen;

    [ObservableProperty] private DateTime? _datumOd;
    [ObservableProperty] private string _casOd = "8:00";
    [ObservableProperty] private DateTime? _datumDo;
    [ObservableProperty] private string _casDo = "9:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Opakuje))]
    private OpakovanieUdalosti _opakovanie;

    [ObservableProperty] private DateTime? _opakovanieDo;
    [ObservableProperty] private PripomienkaUdalosti _pripomienka;
    [ObservableProperty] private string _polozkaCislo = "";
    [ObservableProperty] private string _polozkaText = "";
    [ObservableProperty] private int _miestnostId;
    [ObservableProperty] private int _osobaId;
    [ObservableProperty] private StavUdalosti _stav;

    public bool UkazCas => !CelyDen;
    public bool Opakuje => Opakovanie != OpakovanieUdalosti.Ziadne;
    public bool JeUdalost
    {
        get => !JeUloha;
        set => JeUloha = !value;
    }

    partial void OnJeUlohaChanged(bool value) => OnPropertyChanged(nameof(JeUdalost));

    partial void OnPolozkaCisloChanged(string value) => AktualizujPolozku();

    partial void OnDatumOdChanged(DateTime? value)
    {
        // koniec sa posúva spolu so začiatkom, ak bol v ten istý deň
        if (value is { } od && (DatumDo is null || DatumDo < od))
        {
            DatumDo = od;
        }
    }

    private int? _polozkaId;

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

    public event Action<bool>? Zatvorit;

    private static bool SkusCas(string text, out TimeSpan cas)
    {
        cas = TimeSpan.Zero;
        var t = (text ?? "").Trim().Replace('.', ':').Replace(',', ':');
        if (t.Length == 0)
        {
            return false;
        }

        if (!t.Contains(':'))
        {
            t += ":00";
        }

        return TimeSpan.TryParseExact(t, new[] { @"h\:mm", @"hh\:mm", @"h\:m" }, CultureInfo.InvariantCulture, out cas)
               && cas >= TimeSpan.Zero && cas < TimeSpan.FromDays(1);
    }

    /// <summary>Udalosť z hodnôt v okne (verejné kvôli automatickému testu).</summary>
    public Udalost Vytvor()
    {
        if (DatumOd is not { } od)
        {
            throw new EvitaException(Preklad.T("Zadajte dátum udalosti."));
        }

        var zaciatok = od.Date;
        DateTime? koniec = (DatumDo ?? od).Date;
        if (!CelyDen)
        {
            if (!SkusCas(CasOd, out var casOd))
            {
                throw new EvitaException(Preklad.T("Čas začiatku zadajte v tvare 8:00."));
            }

            zaciatok += casOd;
            if (string.IsNullOrWhiteSpace(CasDo))
            {
                koniec = koniec == od.Date ? null : koniec;
            }
            else if (SkusCas(CasDo, out var casDo))
            {
                koniec = koniec.Value + casDo;
            }
            else
            {
                throw new EvitaException(Preklad.T("Čas konca zadajte v tvare 9:30."));
            }
        }
        else if (koniec == zaciatok)
        {
            koniec = null;
        }

        if (!string.IsNullOrWhiteSpace(PolozkaCislo) && _polozkaId is null)
        {
            throw new EvitaException(Preklad.T("Položka s takým prírastkovým číslom neexistuje. Opravte číslo alebo ho vymažte."));
        }

        return new Udalost
        {
            Id = _id,
            Nazov = Nazov,
            Popis = Popis,
            TypUdalostiId = TypId,
            Druh = JeUloha ? DruhUdalosti.Uloha : DruhUdalosti.Udalost,
            Zaciatok = zaciatok,
            Koniec = koniec,
            CelyDen = CelyDen,
            Opakovanie = Opakovanie,
            OpakovanieDo = Opakuje ? OpakovanieDo : null,
            Pripomienka = Pripomienka,
            PolozkaId = _polozkaId,
            MiestnostId = MiestnostId > 0 ? MiestnostId : null,
            OsobaId = OsobaId > 0 ? OsobaId : null,
            Stav = Stav,
        };
    }

    /// <summary>Id uloženej udalosti.</summary>
    public int UlozeneId { get; private set; }

    [RelayCommand]
    private void Ulozit()
    {
        try
        {
            UlozeneId = _sluzba.Uloz(Vytvor());
            Zatvorit?.Invoke(true);
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Udalosť sa nepodarilo uložiť."), ex);
        }
    }

    [RelayCommand]
    private void Vymazat()
    {
        if (JeNova)
        {
            return;
        }

        var opakovana = Opakuje ? Preklad.T("\n\nUdalosť sa opakuje – vymažú sa všetky jej opakovania.") : "";
        if (!Dialogy.Potvrd(Preklad.F($"Vymazať udalosť „{Nazov}“?{opakovana}")))
        {
            return;
        }

        _sluzba.Vymaz(_id);
        Zatvorit?.Invoke(true);
    }
}
