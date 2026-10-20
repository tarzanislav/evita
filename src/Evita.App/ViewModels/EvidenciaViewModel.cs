using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;

namespace Evita.ViewModels;

public partial class EvidenciaViewModel : ObservableObject, IObnovitelny
{
    private const int FilterStavBezVyradenych = 0;
    private const int FilterStavVsetky = 1;

    private readonly MainViewModel _hlavne;
    private readonly PolozkaService _sluzba = new();
    private readonly CiselnikService _ciselniky = new();
    private readonly DispatcherTimer _oneskorenieHladania;
    private List<PolozkaRiadok> _vsetky = new();
    private string[] _hladaneSlova = Array.Empty<string>();
    private bool _trebaObnovit = true;
    private bool _nacitavam;

    public EvidenciaViewModel(MainViewModel hlavne)
    {
        _hlavne = hlavne;
        _oneskorenieHladania = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _oneskorenieHladania.Tick += (_, _) =>
        {
            _oneskorenieHladania.Stop();
            _hladaneSlova = Sk.BezDiakritiky(HladanyText)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Aplikuj();
        };

        FilterStavy = new List<Volba>
        {
            new(FilterStavBezVyradenych, "bez vyradených"),
            new(FilterStavVsetky, "všetky vrátane vyradených"),
        };
        FilterStavy.AddRange(StavyPoloziek.Vsetky.Select(s => new Volba((int)s.Stav + 2, s.Nazov)));
    }

    public ListCollectionView? Pohlad { get; private set; }

    public List<Volba> FilterStavy { get; }

    [ObservableProperty] private List<Volba> _filterKategorie = new();
    [ObservableProperty] private List<Volba> _filterMiestnosti = new();
    [ObservableProperty] private List<Volba> _filterOsoby = new();

    [ObservableProperty] private int _filterKategoria;
    [ObservableProperty] private int _filterMiestnost;
    [ObservableProperty] private int _filterOsoba;
    [ObservableProperty] private int _filterStav = FilterStavBezVyradenych;
    [ObservableProperty] private DateTime? _pridaneOd;
    [ObservableProperty] private DateTime? _pridaneDo;
    [ObservableProperty] private string _hladanyText = "";

    [ObservableProperty] private string? _rychlyFilter;
    [ObservableProperty] private string _rychlyFilterPopis = "";

    [ObservableProperty] private string _stavText = "";
    [ObservableProperty] private PolozkaRiadok? _vybrana;

    /// <summary>Označené riadky (nastavuje tabuľka pri zmene výberu).</summary>
    public List<PolozkaRiadok> Oznacene { get; set; } = new();

    /// <summary>Žiadosť pre zobrazenie: nastaviť kurzor do vyhľadávania.</summary>
    public event Action? ZameratHladanie;

    /// <summary>Žiadosť pre zobrazenie: posunúť tabuľku na riadok.</summary>
    public event Action<PolozkaRiadok>? ZobrazitRiadok;

    public void OznacNaObnovenie() => _trebaObnovit = true;

    public void Obnov()
    {
        if (!_trebaObnovit && Pohlad is not null)
        {
            return;
        }

        NacitajZnova();
    }

    partial void OnFilterKategoriaChanged(int value) => Aplikuj();
    partial void OnFilterMiestnostChanged(int value) => Aplikuj();
    partial void OnFilterOsobaChanged(int value) => Aplikuj();
    partial void OnFilterStavChanged(int value) => Aplikuj();
    partial void OnPridaneOdChanged(DateTime? value) => Aplikuj();
    partial void OnPridaneDoChanged(DateTime? value) => Aplikuj();

    partial void OnHladanyTextChanged(string value)
    {
        _oneskorenieHladania.Stop();
        _oneskorenieHladania.Start();
    }

    /// <summary>Filter z dashboardu (napr. „záruky do 30 dní“).</summary>
    public void PouziRychlyFilter(string? filter)
    {
        _nacitavam = true;
        FilterKategoria = 0;
        FilterMiestnost = 0;
        FilterOsoba = 0;
        PridaneOd = null;
        PridaneDo = null;
        HladanyText = "";
        _hladaneSlova = Array.Empty<string>();
        FilterStav = filter == "vyradene" ? (int)StavPolozky.Vyradene + 2 : FilterStavBezVyradenych;
        RychlyFilter = filter is "vyradene" or "vsetky" ? null : filter;
        RychlyFilterPopis = RychlyFilter switch
        {
            "bezMiestnosti" => "položky bez miestnosti",
            "bezPridelenia" => "položky bez pridelenia",
            "zaruky" => "záruka končí do 30 dní",
            "revizie" => "revízia do 30 dní alebo po termíne",
            "oprava" => "položky v oprave",
            "mesiac" => "pridané tento mesiac",
            _ => "",
        };
        _nacitavam = false;
        Aplikuj();
    }

    /// <summary>Zobrazí položky jednej kategórie (z grafu na dashboarde).</summary>
    public void ZobrazKategoriu(int kategoriaId)
    {
        PouziRychlyFilter(null);
        FilterKategoria = FilterKategorie.Any(v => v.Id == kategoriaId) ? kategoriaId : 0;
    }

    [RelayCommand]
    private void ZrusRychlyFilter()
    {
        RychlyFilter = null;
        RychlyFilterPopis = "";
        Aplikuj();
    }

    [RelayCommand]
    private void VycistitFiltre() => PouziRychlyFilter(null);

    [RelayCommand]
    private void ObnovZoznam() => NacitajZnova();

    [RelayCommand]
    private void Nova()
    {
        var okno = new PolozkaEditWindow(new PolozkaEditViewModel(null, null)) { Owner = Dialogy.Vlastnik };
        if (okno.ShowDialog() == true)
        {
            PoZmene(okno.Model.UlozeneId.FirstOrDefault());
        }
    }

    [RelayCommand]
    private void Upravit()
    {
        if (Vybrana is not { } riadok)
        {
            Dialogy.Info("Najprv vyberte položku v zozname.");
            return;
        }

        var okno = new PolozkaEditWindow(new PolozkaEditViewModel(riadok.Id, null)) { Owner = Dialogy.Vlastnik };
        if (okno.ShowDialog() == true)
        {
            PoZmene(riadok.Id);
        }
    }

    [RelayCommand]
    private void Kopia()
    {
        if (Vybrana is not { } riadok)
        {
            Dialogy.Info("Najprv vyberte položku, ktorú chcete skopírovať.");
            return;
        }

        var okno = new PolozkaEditWindow(new PolozkaEditViewModel(null, riadok.Id)) { Owner = Dialogy.Vlastnik };
        if (okno.ShowDialog() == true)
        {
            PoZmene(okno.Model.UlozeneId.FirstOrDefault());
        }
    }

    [RelayCommand]
    private void HromadnaZmena()
    {
        var oznacene = OznaceneAleboVybrana();
        if (oznacene.Count == 0)
        {
            Dialogy.Info("Označte položky, ktorým chcete naraz zmeniť miestnosť, pridelenie alebo stav. Viac položiek označíte so stlačeným Ctrl alebo Shift.");
            return;
        }

        var okno = new HromadnaZmenaWindow(new HromadnaZmenaViewModel(oznacene.Select(r => r.Id).ToList()))
        {
            Owner = Dialogy.Vlastnik,
        };
        if (okno.ShowDialog() == true)
        {
            PoZmene(Vybrana?.Id ?? 0);
        }
    }

    [RelayCommand]
    private void Vyradit()
    {
        var oznacene = OznaceneAleboVybrana().Where(r => !r.Vyradena).ToList();
        if (oznacene.Count == 0)
        {
            Dialogy.Info("Vyberte položky, ktoré chcete vyradiť (už vyradené sa preskočia).");
            return;
        }

        var popis = oznacene.Count == 1
            ? $"položku {oznacene[0].PrirastkoveCislo} – {oznacene[0].Nazov}"
            : $"{oznacene.Count} položiek";
        var dovod = Dialogy.Vstup("Vyradenie", $"Vyradiť {popis}?\n\nDôvod vyradenia (nepovinné):");
        if (dovod is null)
        {
            return;
        }

        Dialogy.Cakaj(() => _sluzba.Vyrad(oznacene.Select(r => r.Id), DateTime.Today, dovod));
        PoZmene(Vybrana?.Id ?? 0);
    }

    [RelayCommand]
    private void Obnovit()
    {
        var oznacene = OznaceneAleboVybrana().Where(r => r.Vyradena).ToList();
        if (oznacene.Count == 0)
        {
            Dialogy.Info("Vyberte vyradené položky, ktoré chcete vrátiť do prevádzky. Vyradené zobrazíte filtrom Stav.");
            return;
        }

        if (!Dialogy.Potvrd($"Vrátiť {oznacene.Count} vyradených položiek do stavu „v prevádzke“?"))
        {
            return;
        }

        Dialogy.Cakaj(() => _sluzba.Obnov(oznacene.Select(r => r.Id)));
        PoZmene(Vybrana?.Id ?? 0);
    }

    [RelayCommand]
    private void Vymazat()
    {
        var oznacene = OznaceneAleboVybrana();
        if (oznacene.Count == 0)
        {
            Dialogy.Info("Vyberte položky, ktoré chcete vymazať.");
            return;
        }

        var popis = oznacene.Count == 1
            ? $"položku {oznacene[0].PrirastkoveCislo} – {oznacene[0].Nazov}"
            : $"{oznacene.Count} položiek";
        if (!Dialogy.Potvrd(
                $"Naozaj natrvalo vymazať {popis}?\n\nPri evidencii majetku sa položky zvyčajne nemažú, ale vyraďujú. " +
                "Záznam o vymazaní zostane v histórii zmien a prírastkové čísla sa už nepoužijú."))
        {
            return;
        }

        Dialogy.Cakaj(() => _sluzba.Vymaz(oznacene.Select(r => r.Id)));
        PoZmene(0);
    }

    [RelayCommand]
    private void Exportovat()
    {
        var vyfiltrovane = Pohlad?.Cast<PolozkaRiadok>().Select(r => r.Id).ToList() ?? new List<int>();
        var oznacene = Oznacene.Select(r => r.Id).ToList();
        var okno = new ExportWindow(new ExportViewModel(vyfiltrovane, oznacene, PopisFiltra())) { Owner = Dialogy.Vlastnik };
        okno.ShowDialog();
    }

    [RelayCommand]
    private void Tlacit()
    {
        try
        {
            var okno = VytvorNahladTlace();
            okno.Owner = Dialogy.Vlastnik;
            okno.ShowDialog();
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba("Zostavu sa nepodarilo vytvoriť.", ex);
        }
    }

    /// <summary>Náhľad zostavy „Vyfiltrované položky“ podľa aktuálneho filtra.</summary>
    public NahladWindow VytvorNahladTlace()
    {
        var popis = ZostavyService.Popis(DruhZostavy.Vyfiltrovane);
        var moznosti = new MoznostiZostavy
        {
            Druh = DruhZostavy.Vyfiltrovane,
            Stlpce = popis.PredvoleneStlpce,
            NaSirku = true,
            IdPoloziek = Pohlad?.Cast<PolozkaRiadok>().Select(r => r.Id).ToList() ?? new List<int>(),
            PopisFiltra = PopisFiltra(),
        };
        var sluzba = new ZostavyService();
        return new NahladWindow(() => sluzba.Vytvor(moznosti), true, popis.Nazov, ZostavyService.NazovSuboru(DruhZostavy.Vyfiltrovane));
    }

    /// <summary>Čitateľný popis zapnutých filtrov (do hlavičky exportu).</summary>
    public string PopisFiltra()
    {
        var casti = new List<string>();
        if (FilterKategoria != 0 && FilterKategorie.FirstOrDefault(v => v.Id == FilterKategoria) is { } k)
        {
            casti.Add("kategória " + k.Nazov);
        }

        if (FilterMiestnost != 0 && FilterMiestnosti.FirstOrDefault(v => v.Id == FilterMiestnost) is { } m)
        {
            casti.Add("miestnosť " + m.Nazov);
        }

        if (FilterOsoba != 0 && FilterOsoby.FirstOrDefault(v => v.Id == FilterOsoba) is { } o)
        {
            casti.Add("pridelené " + o.Nazov);
        }

        if (FilterStav != FilterStavBezVyradenych && FilterStavy.FirstOrDefault(v => v.Id == FilterStav) is { } s)
        {
            casti.Add("stav " + s.Nazov);
        }

        if (PridaneOd is { } od)
        {
            casti.Add("pridané od " + Sk.Datum(od));
        }

        if (PridaneDo is { } po)
        {
            casti.Add("pridané do " + Sk.Datum(po));
        }

        if (!string.IsNullOrWhiteSpace(RychlyFilterPopis) && RychlyFilter is not null)
        {
            casti.Add(RychlyFilterPopis);
        }

        if (!string.IsNullOrWhiteSpace(HladanyText))
        {
            casti.Add($"hľadanie „{HladanyText.Trim()}“");
        }

        return string.Join(", ", casti);
    }

    private List<PolozkaRiadok> OznaceneAleboVybrana()
    {
        if (Oznacene.Count > 0)
        {
            return Oznacene.ToList();
        }

        return Vybrana is null ? new List<PolozkaRiadok>() : new List<PolozkaRiadok> { Vybrana };
    }

    private void PoZmene(int idNaVybratie)
    {
        _hlavne.DataZmenene();
        NacitajZnova();
        if (idNaVybratie != 0 && Pohlad is not null)
        {
            var riadok = _vsetky.FirstOrDefault(r => r.Id == idNaVybratie);
            if (riadok is not null && Pohlad.Contains(riadok))
            {
                Vybrana = riadok;
                ZobrazitRiadok?.Invoke(riadok);
            }
        }
    }

    private void NacitajZnova()
    {
        _trebaObnovit = false;
        _nacitavam = true;

        var zoradenie = Pohlad?.SortDescriptions.ToList()
            ?? new List<SortDescription> { new(nameof(PolozkaRiadok.PrirastkoveCislo), ListSortDirection.Descending) };

        _vsetky = _sluzba.NacitajRiadky();

        var kategoria = FilterKategoria;
        var miestnost = FilterMiestnost;
        var osoba = FilterOsoba;

        FilterKategorie = new List<Volba> { new(0, "(všetky)") }
            .Concat(_ciselniky.Volby(TypCiselnika.Kategorie, null, null)).ToList();
        FilterMiestnosti = new List<Volba> { new(0, "(všetky)"), new(-1, "(nezadaná)") }
            .Concat(_ciselniky.Volby(TypCiselnika.Miestnosti, null, null)).ToList();
        FilterOsoby = new List<Volba> { new(0, "(všetci)"), new(-1, "(nikto)") }
            .Concat(_ciselniky.Volby(TypCiselnika.Osoby, null, null)).ToList();

        FilterKategoria = FilterKategorie.Any(v => v.Id == kategoria) ? kategoria : 0;
        FilterMiestnost = FilterMiestnosti.Any(v => v.Id == miestnost) ? miestnost : 0;
        FilterOsoba = FilterOsoby.Any(v => v.Id == osoba) ? osoba : 0;
        OnPropertyChanged(nameof(FilterKategoria));
        OnPropertyChanged(nameof(FilterMiestnost));
        OnPropertyChanged(nameof(FilterOsoba));

        var pohlad = new ListCollectionView(_vsetky)
        {
            Filter = Filtruj,
            Culture = Sk.Kultura,
        };
        using (pohlad.DeferRefresh())
        {
            foreach (var z in zoradenie)
            {
                pohlad.SortDescriptions.Add(z);
            }
        }

        Pohlad = pohlad;
        OnPropertyChanged(nameof(Pohlad));
        _nacitavam = false;
        AktualizujStavText();
    }

    private void Aplikuj()
    {
        if (_nacitavam || Pohlad is null)
        {
            return;
        }

        Pohlad.Refresh();
        AktualizujStavText();
    }

    private void AktualizujStavText()
    {
        var zobrazene = Pohlad?.Count ?? 0;
        StavText = $"Zobrazené: {zobrazene} z {_vsetky.Count} položiek";
    }

    private bool Filtruj(object objekt)
    {
        if (objekt is not PolozkaRiadok r)
        {
            return false;
        }

        if (FilterKategoria > 0 && r.KategoriaId != FilterKategoria)
        {
            return false;
        }

        if (FilterMiestnost == -1 ? r.MiestnostId is not null : FilterMiestnost > 0 && r.MiestnostId != FilterMiestnost)
        {
            return false;
        }

        if (FilterOsoba == -1 ? r.OsobaId is not null : FilterOsoba > 0 && r.OsobaId != FilterOsoba)
        {
            return false;
        }

        var stavOk = FilterStav switch
        {
            FilterStavBezVyradenych => !r.Vyradena,
            FilterStavVsetky => true,
            _ => (int)r.StavHodnota == FilterStav - 2,
        };
        if (!stavOk)
        {
            return false;
        }

        if (PridaneOd is { } od && r.DatumPridania.Date < od.Date)
        {
            return false;
        }

        if (PridaneDo is { } po && r.DatumPridania.Date > po.Date)
        {
            return false;
        }

        if (!SplnaRychlyFilter(r))
        {
            return false;
        }

        foreach (var slovo in _hladaneSlova)
        {
            if (!r.Hladanie.Contains(slovo, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private bool SplnaRychlyFilter(PolozkaRiadok r)
    {
        var dnes = DateTime.Today;
        return RychlyFilter switch
        {
            "bezMiestnosti" => r.MiestnostId is null,
            "bezPridelenia" => r.OsobaId is null,
            "zaruky" => r.ZarukaDo is { } z && z >= dnes && z <= dnes.AddDays(30),
            "revizie" => r.DalsiaRevizia is { } rev && rev <= dnes.AddDays(30),
            "oprava" => r.StavHodnota == StavPolozky.VOprave,
            "mesiac" => r.DatumPridania >= new DateTime(dnes.Year, dnes.Month, 1),
            _ => true,
        };
    }

    public void PoziadajOZameranieHladania() => ZameratHladanie?.Invoke();
}
