using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;
using Microsoft.Win32;

namespace Evita.ViewModels;

public sealed record VolbaZoskupenia(Zoskupenie Hodnota, string Nazov);

/// <summary>Obrazovka Tlač a zostavy: výber zostavy, jej voľby, náhľad, tlač a PDF; hlavička zostáv.</summary>
public partial class TlacViewModel : ObservableObject, IObnovitelny
{
    private readonly MainViewModel _hlavne;
    private readonly ZostavyService _zostavy = new();
    private readonly CiselnikService _ciselniky = new();

    public TlacViewModel(MainViewModel hlavne)
    {
        _hlavne = hlavne;
        var dnes = DateTime.Today;
        _od = new DateTime(dnes.Year, 1, 1);
        _do = dnes;
        _vybrana = Zostavy[0];
        NastavPodlaZostavy();
    }

    public IReadOnlyList<PopisZostavy> Zostavy => ZostavyService.Zoznam;

    public IReadOnlyList<VolbaZoskupenia> Zoskupenia { get; } = new VolbaZoskupenia[]
    {
        new(Zoskupenie.Kategoria, Preklad.T("kategórie")),
        new(Zoskupenie.Miestnost, Preklad.T("miestnosti")),
        new(Zoskupenie.Pridelenie, Preklad.T("pridelenia (osoby)")),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UkazObdobie), nameof(UkazVyradene), nameof(UkazPodpis), nameof(UkazNovuStranu),
        nameof(UkazStlpce), nameof(UkazZoskupenie), nameof(UkazMiestnost), nameof(UkazOsobu), nameof(UkazKategoriu),
        nameof(UkazPolozku), nameof(Dostupna), nameof(Poznamka), nameof(TextPodpisu), nameof(PopisVyfiltrovanych), nameof(UkazKalendar), nameof(UkazIkonu))]
    private PopisZostavy _vybrana;

    partial void OnVybranaChanged(PopisZostavy value)
    {
        NastavPodlaZostavy();
        if (!_pouzivamUlozenu)
        {
            // iná zostava zo zoznamu – uložená už nie je vybraná
            _pouzivamUlozenu = true;
            VybranaUlozena = null;
            _pouzivamUlozenu = false;
            ObdobieUlozenia = ObdobieZostavy.PevneDatumy;
        }
    }

    // ------------------------------------------------------------------ uložené zostavy

    private bool _pouzivamUlozenu;

    public ObservableCollection<UlozenaZostava> UlozeneZostavy { get; } = new();

    public bool ZiadneUlozene => UlozeneZostavy.Count == 0;

    public IReadOnlyList<VolbaObdobia> Obdobia { get; } = Services.UlozeneZostavy.Obdobia;

    [ObservableProperty] private UlozenaZostava? _vybranaUlozena;

    /// <summary>Ako sa uloží obdobie: pevné dátumy alebo obdobie počítané podľa dňa tlače.</summary>
    [ObservableProperty] private ObdobieZostavy _obdobieUlozenia = ObdobieZostavy.PevneDatumy;

    /// <summary>Pre automatický test: názov bez okna na zadanie.</summary>
    public string? NazovBezDialogu { get; set; }

    partial void OnVybranaUlozenaChanged(UlozenaZostava? value)
    {
        if (value is not null && !_pouzivamUlozenu)
        {
            PouzitUlozenu(value);
        }
    }

    partial void OnObdobieUlozeniaChanged(ObdobieZostavy value)
    {
        if (!_pouzivamUlozenu && Services.UlozeneZostavy.Rozsah(value, DateTime.Today) is { } rozsah)
        {
            Od = rozsah.Od;
            Do = rozsah.Do;
        }
    }

    private void NacitajUlozene()
    {
        var vybrana = VybranaUlozena?.Nazov;
        _pouzivamUlozenu = true;
        try
        {
            UlozeneZostavy.Clear();
            foreach (var z in Services.UlozeneZostavy.Nacitaj())
            {
                UlozeneZostavy.Add(z);
            }

            VybranaUlozena = UlozeneZostavy.FirstOrDefault(z => z.Nazov == vybrana);
        }
        finally
        {
            _pouzivamUlozenu = false;
        }

        OnPropertyChanged(nameof(ZiadneUlozene));
    }

    /// <summary>Nastaví obrazovku podľa uloženej zostavy.</summary>
    public void PouzitUlozenu(UlozenaZostava zostava)
    {
        var popis = Zostavy.FirstOrDefault(z => z.Druh.ToString() == zostava.Druh);
        if (popis is null)
        {
            return;
        }

        _pouzivamUlozenu = true;
        try
        {
            Vybrana = popis;
            NastavPodlaZostavy();

            var vybrane = zostava.Stlpce.Where(k => StlpcePrenosu.Vsetky.Any(s => s.Kluc == k)).Distinct().ToList();
            if (vybrane.Count > 0)
            {
                Stlpce.Clear();
                foreach (var kluc in vybrane)
                {
                    Stlpce.Add(new VolbaStlpca(StlpcePrenosu.Podla(kluc), true));
                }

                foreach (var stlpec in StlpcePrenosu.Vsetky.Where(s => !vybrane.Contains(s.Kluc)))
                {
                    Stlpce.Add(new VolbaStlpca(stlpec, false));
                }
            }

            NaSirku = zostava.NaSirku;
            VrataneVyradenych = zostava.VrataneVyradenych;
            Podpis = zostava.Podpis;
            KazdaNaNovejStrane = zostava.KazdaNaNovejStrane;
            IkonaKategorie = zostava.IkonaKategorie;
            if (Enum.TryParse<Zoskupenie>(zostava.Zoskupenie, out var zoskupenie))
            {
                Zoskupenie = zoskupenie;
            }

            KalendarZoznam = zostava.KalendarZoznam;
            AutomatickeTerminy = zostava.AutomatickeTerminy;
            PrirastkoveCislo = zostava.PrirastkoveCislo ?? "";
            KategoriaId = Kategorie.FirstOrDefault(v => v.Id != 0 && v.Nazov == zostava.Kategoria)?.Id ?? 0;
            MiestnostId = Miestnosti.FirstOrDefault(v => v.Id != 0 && v.Nazov == zostava.Miestnost)?.Id ?? 0;
            OsobaId = Osoby.FirstOrDefault(v => v.Id != 0 && v.Nazov == zostava.Osoba)?.Id ?? 0;

            ObdobieUlozenia = zostava.DruhObdobia;
            if (Services.UlozeneZostavy.Rozsah(zostava.DruhObdobia, DateTime.Today) is { } rozsah)
            {
                Od = rozsah.Od;
                Do = rozsah.Do;
            }
            else if (zostava.Od is not null || zostava.Do is not null)
            {
                Od = zostava.Od;
                Do = zostava.Do;
            }

            VybranaUlozena = UlozeneZostavy.FirstOrDefault(z => z.Nazov == zostava.Nazov);
        }
        finally
        {
            _pouzivamUlozenu = false;
        }
    }

    [RelayCommand]
    private void UlozitZostavu()
    {
        try
        {
            Moznosti(); // kontrola volieb (dátumy, číslo položky)
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
            return;
        }

        var navrh = VybranaUlozena?.Druh == Vybrana.Druh.ToString() ? VybranaUlozena.Nazov : Vybrana.Nazov;
        var nazov = (NazovBezDialogu ?? Dialogy.Vstup(Preklad.T("Uložiť zostavu"), Preklad.T("Názov uloženej zostavy:"), navrh))?.Trim();
        if (string.IsNullOrEmpty(nazov))
        {
            return;
        }

        if (nazov.Length > 100)
        {
            Dialogy.Upozornenie(Preklad.T("Názov zostavy je príliš dlhý (najviac 100 znakov)."));
            return;
        }

        var existujuca = UlozeneZostavy.FirstOrDefault(z => Sk.Porovnanie.Equals(z.Nazov, nazov));
        if (existujuca is not null && !Dialogy.Potvrd(Preklad.F($"Zostava „{existujuca.Nazov}“ už je uložená. Nahradiť ju súčasnými voľbami?")))
        {
            return;
        }

        string? NazovVolby(List<Volba> volby, int id) => id == 0 ? null : volby.FirstOrDefault(v => v.Id == id)?.Nazov;
        var pevne = ObdobieUlozenia == ObdobieZostavy.PevneDatumy;
        var zostava = new UlozenaZostava
        {
            Nazov = nazov,
            Druh = Vybrana.Druh.ToString(),
            Stlpce = Stlpce.Where(st => st.Vybrany).Select(st => st.Stlpec.Kluc).ToList(),
            NaSirku = NaSirku,
            VrataneVyradenych = VrataneVyradenych,
            Podpis = Podpis,
            KazdaNaNovejStrane = KazdaNaNovejStrane,
            IkonaKategorie = IkonaKategorie,
            Zoskupenie = Zoskupenie.ToString(),
            Obdobie = UkazObdobie ? ObdobieUlozenia.ToString() : nameof(ObdobieZostavy.PevneDatumy),
            Od = UkazObdobie && pevne ? Od : null,
            Do = UkazObdobie && pevne ? Do : null,
            Kategoria = UkazKategoriu ? NazovVolby(Kategorie, KategoriaId) : null,
            Miestnost = UkazMiestnost ? NazovVolby(Miestnosti, MiestnostId) : null,
            Osoba = UkazOsobu ? NazovVolby(Osoby, OsobaId) : null,
            PrirastkoveCislo = UkazPolozku && !string.IsNullOrWhiteSpace(PrirastkoveCislo) ? PrirastkoveCislo.Trim() : null,
            KalendarZoznam = KalendarZoznam,
            AutomatickeTerminy = AutomatickeTerminy,
            Ulozene = DateTime.Now,
        };

        try
        {
            Services.UlozeneZostavy.Pridaj(zostava);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Zostavu sa nepodarilo uložiť."), ex);
            return;
        }

        NacitajUlozene();
        _pouzivamUlozenu = true;
        VybranaUlozena = UlozeneZostavy.FirstOrDefault(z => z.Nazov == nazov);
        _pouzivamUlozenu = false;
    }

    [RelayCommand]
    private void VymazatUlozenu(UlozenaZostava? zostava)
    {
        zostava ??= VybranaUlozena;
        if (zostava is null || !Dialogy.Potvrd(Preklad.F($"Vymazať uloženú zostavu „{zostava.Nazov}“?")))
        {
            return;
        }

        Services.UlozeneZostavy.Vymaz(zostava.Nazov);
        if (VybranaUlozena?.Nazov == zostava.Nazov)
        {
            _pouzivamUlozenu = true;
            VybranaUlozena = null;
            _pouzivamUlozenu = false;
        }

        NacitajUlozene();
    }

    public bool UkazObdobie => Vybrana.MaObdobie;
    public bool UkazVyradene => Vybrana.MaVyradene;
    public bool UkazPodpis => Vybrana.MaPodpis;
    public bool UkazNovuStranu => Vybrana.MaNovuStranu;

    /// <summary>Voľba ikony kategórie – pri zostavách so zoznamom položiek (nie pri histórii a kalendári).</summary>
    public bool UkazIkonu => Vybrana.Druh is not (DruhZostavy.Historia or DruhZostavy.Kalendar);
    public bool UkazStlpce => Vybrana.MaVolbuStlpcov && Vybrana.Dostupna;
    public bool UkazZoskupenie => Vybrana.Druh == DruhZostavy.SuhrnnyZoznam;
    public bool UkazMiestnost => Vybrana.Druh is DruhZostavy.PoMiestnostiach or DruhZostavy.Historia;
    public bool UkazOsobu => Vybrana.Druh is DruhZostavy.PodlaPridelenia or DruhZostavy.Historia;
    public bool UkazKategoriu => Vybrana.Druh == DruhZostavy.PoKategoriach;
    public bool UkazPolozku => Vybrana.Druh == DruhZostavy.Historia;
    public bool UkazKalendar => Vybrana.Druh == DruhZostavy.Kalendar;

    [ObservableProperty] private bool _kalendarZoznam;
    [ObservableProperty] private bool _automatickeTerminy = true;

    public bool KalendarMriezka
    {
        get => !KalendarZoznam;
        set => KalendarZoznam = !value;
    }

    partial void OnKalendarZoznamChanged(bool value) => OnPropertyChanged(nameof(KalendarMriezka));
    public bool Dostupna => Vybrana.Dostupna;
    public string Poznamka => Vybrana.Poznamka ?? "";

    public string TextPodpisu => Vybrana.Druh == DruhZostavy.PodlaPridelenia
        ? Preklad.T("Zoznam zverenej techniky s podpisom (prevzal / odovzdal)")
        : Preklad.T("Inventúrny zoznam s riadkami na podpis");

    public string PopisVyfiltrovanych
    {
        get
        {
            if (Vybrana.Druh != DruhZostavy.Vyfiltrovane)
            {
                return "";
            }

            var (id, popis) = Vyfiltrovane();
            return Preklad.F($"Vytlačí sa {ExportService.PocetPoloziek(id.Count)} zobrazených v Evidencii") +
                   (string.IsNullOrWhiteSpace(popis) ? Preklad.T(" (bez filtra).") : Preklad.F($" – filter: {popis}.")) +
                   Preklad.T(" Filter zmeníte na obrazovke Evidencia.");
        }
    }

    [ObservableProperty] private DateTime? _od;
    [ObservableProperty] private DateTime? _do;
    [ObservableProperty] private bool _vrataneVyradenych;
    [ObservableProperty] private bool _podpis = true;
    [ObservableProperty] private bool _kazdaNaNovejStrane = true;
    [ObservableProperty] private bool _ikonaKategorie = true;
    [ObservableProperty] private bool _naSirku;
    [ObservableProperty] private Zoskupenie _zoskupenie = Zoskupenie.Kategoria;
    [ObservableProperty] private int _miestnostId;
    [ObservableProperty] private int _osobaId;
    [ObservableProperty] private int _kategoriaId;
    [ObservableProperty] private string _prirastkoveCislo = "";

    [ObservableProperty] private List<Volba> _miestnosti = new();
    [ObservableProperty] private List<Volba> _osoby = new();
    [ObservableProperty] private List<Volba> _kategorie = new();

    public ObservableCollection<VolbaStlpca> Stlpce { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(HoreCommand), nameof(DoleCommand))]
    private VolbaStlpca? _vybranyStlpec;

    // ------------------------------------------------------------------ hlavička zostáv

    [ObservableProperty] private string _nazovOrganizacie = "";
    [ObservableProperty] private BitmapImage? _logo;
    [ObservableProperty] private bool _maLogo;

    public void Obnov()
    {
        Miestnosti = new List<Volba> { new(0, Preklad.T("(všetky)")) }.Concat(_ciselniky.Volby(TypCiselnika.Miestnosti, null, null)).ToList();
        Osoby = new List<Volba> { new(0, Preklad.T("(všetci)")) }.Concat(_ciselniky.Volby(TypCiselnika.Osoby, null, null)).ToList();
        Kategorie = new List<Volba> { new(0, Preklad.T("(všetky)")) }.Concat(_ciselniky.Volby(TypCiselnika.Kategorie, null, null)).ToList();
        if (!Miestnosti.Any(v => v.Id == MiestnostId))
        {
            MiestnostId = 0;
        }

        if (!Osoby.Any(v => v.Id == OsobaId))
        {
            OsobaId = 0;
        }

        if (!Kategorie.Any(v => v.Id == KategoriaId))
        {
            KategoriaId = 0;
        }

        NacitajUlozene();
        var organizacia = NastaveniaTlace.Nacitaj();
        NazovOrganizacie = organizacia.Nazov ?? "";
        NastavLogo(organizacia.Logo);
        OnPropertyChanged(nameof(PopisVyfiltrovanych));
    }

    private void NastavPodlaZostavy()
    {
        VrataneVyradenych = Vybrana.PredvoleneVyradene;
        KazdaNaNovejStrane = Vybrana.MaNovuStranu && Vybrana.Druh != DruhZostavy.PoKategoriach;
        Stlpce.Clear();
        foreach (var kluc in Vybrana.PredvoleneStlpce)
        {
            Stlpce.Add(new VolbaStlpca(StlpcePrenosu.Podla(kluc), true));
        }

        foreach (var stlpec in StlpcePrenosu.Vsetky.Where(s => !Vybrana.PredvoleneStlpce.Contains(s.Kluc)))
        {
            Stlpce.Add(new VolbaStlpca(stlpec, false));
        }

        NaSirku = ZostavyService.OdporucNaSirku(Vybrana.Druh, Vybrana.PredvoleneStlpce.Length);
        if (Vybrana.Druh == DruhZostavy.Kalendar)
        {
            var dnes = DateTime.Today;
            Od = new DateTime(dnes.Year, dnes.Month, 1);
            Do = Od.Value.AddMonths(1).AddDays(-1);
        }
    }

    [RelayCommand(CanExecute = nameof(MozeHore))]
    private void Hore()
    {
        if (VybranyStlpec is { } s && Stlpce.IndexOf(s) is > 0 and var i)
        {
            Stlpce.Move(i, i - 1);
            HoreCommand.NotifyCanExecuteChanged();
            DoleCommand.NotifyCanExecuteChanged();
        }
    }

    private bool MozeHore() => VybranyStlpec is { } s && Stlpce.IndexOf(s) > 0;

    [RelayCommand(CanExecute = nameof(MozeDole))]
    private void Dole()
    {
        if (VybranyStlpec is { } s && Stlpce.IndexOf(s) is var i && i >= 0 && i < Stlpce.Count - 1)
        {
            Stlpce.Move(i, i + 1);
            HoreCommand.NotifyCanExecuteChanged();
            DoleCommand.NotifyCanExecuteChanged();
        }
    }

    private bool MozeDole() => VybranyStlpec is { } s && Stlpce.IndexOf(s) is var i && i >= 0 && i < Stlpce.Count - 1;

    [RelayCommand]
    private void PredvoleneStlpce() => NastavPodlaZostavy();

    // ------------------------------------------------------------------ zostava

    private (List<int> Id, string Popis) Vyfiltrovane()
    {
        var evidencia = _hlavne.Evidencia;
        evidencia.Obnov();
        var id = evidencia.Pohlad?.Cast<PolozkaRiadok>().Select(r => r.Id).ToList() ?? new List<int>();
        return (id, evidencia.PopisFiltra());
    }

    /// <summary>Voľby zostavy podľa obrazovky (verejné kvôli automatickému testu).</summary>
    public MoznostiZostavy Moznosti()
    {
        if (Od is { } od && Do is { } po && od > po)
        {
            throw new EvitaException(Preklad.T("Dátum „od“ je neskôr ako dátum „do“."));
        }

        int? cislo = null;
        if (UkazPolozku && !string.IsNullOrWhiteSpace(PrirastkoveCislo))
        {
            if (!int.TryParse(PrirastkoveCislo.Trim(), out var c) || c < 1)
            {
                throw new EvitaException(Preklad.T("Prírastkové číslo položky musí byť kladné celé číslo."));
            }

            cislo = c;
        }

        IReadOnlyCollection<int>? id = null;
        string? popisFiltra = null;
        if (Vybrana.Druh == DruhZostavy.Vyfiltrovane)
        {
            (var zoznam, popisFiltra) = Vyfiltrovane();
            id = zoznam;
        }

        return new MoznostiZostavy
        {
            Druh = Vybrana.Druh,
            Stlpce = Stlpce.Where(s => s.Vybrany).Select(s => s.Stlpec.Kluc).ToList(),
            NaSirku = NaSirku,
            VrataneVyradenych = VrataneVyradenych,
            Od = UkazObdobie ? Od : null,
            Do = UkazObdobie ? Do : null,
            Zoskupenie = Zoskupenie,
            Podpis = UkazPodpis && Podpis,
            KazdaNaNovejStrane = UkazNovuStranu && KazdaNaNovejStrane,
            IkonaKategorie = UkazIkonu && IkonaKategorie,
            IdPoloziek = id,
            PopisFiltra = popisFiltra,
            KategoriaId = UkazKategoriu ? KategoriaId : null,
            MiestnostId = UkazMiestnost ? MiestnostId : null,
            OsobaId = UkazOsobu ? OsobaId : null,
            PrirastkoveCislo = cislo,
            KalendarZoznam = KalendarZoznam,
            AutomatickeTerminy = AutomatickeTerminy,
        };
    }

    /// <summary>Okno náhľadu pre aktuálnu zostavu (null pri chybe vo voľbách).</summary>
    public NahladWindow? VytvorNahlad()
    {
        try
        {
            var moznosti = Moznosti();
            return new NahladWindow(() => _zostavy.Vytvor(moznosti), moznosti.NaSirku, Vybrana.Nazov, ZostavyService.NazovSuboru(Vybrana.Druh))
            {
                Owner = Dialogy.Vlastnik,
            };
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Zostavu sa nepodarilo vytvoriť."), ex);
            return null;
        }
    }

    [RelayCommand]
    private void Nahlad() => VytvorNahlad()?.ShowDialog();

    /// <summary>Pre automatický test: cesta k PDF bez okna Uložiť ako.</summary>
    public string? CestaBezDialogu { get; set; }

    /// <summary>Nastavenia PDF (zabezpečenie, sivá, strany na list …) pre všetky PDF z EviTA.</summary>
    [RelayCommand]
    private void NastaveniaPdf() =>
        new TlacWindow(null, false, "PDF", RezimOknaTlace.NastaveniaPdf, null, null) { Owner = Dialogy.Vlastnik }.ShowDialog();

    [RelayCommand]
    private void UlozitPdf()
    {
        try
        {
            var moznosti = Moznosti();
            if (CestaBezDialogu is not null)
            {
                NahladWindow.UlozPdf(() => _zostavy.Vytvor(moznosti), ZostavyService.NazovSuboru(Vybrana.Druh), CestaBezDialogu);
            }
            else
            {
                // okno volieb PDF (šifrovanie, heslo, zákazy, strany na list, sivá …) s náhľadom
                NahladWindow.UlozitPdfSVolbami(null, () => _zostavy.Vytvor(moznosti), moznosti.NaSirku, Vybrana.Nazov, ZostavyService.NazovSuboru(Vybrana.Druh));
            }
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
    }

    [RelayCommand]
    private void Vytlacit()
    {
        try
        {
            var moznosti = Moznosti();
            NahladDokumentu.Vytlac(() => _zostavy.Vytvor(moznosti), moznosti.NaSirku, $"EviTA – {Vybrana.Nazov}");
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Tlač sa nepodarila."), ex);
        }
    }

    // ------------------------------------------------------------------ hlavička: organizácia a logo

    [RelayCommand]
    private void UlozitOrganizaciu()
    {
        try
        {
            NastaveniaTlace.UlozNazov(NazovOrganizacie);
            Dialogy.Info(Preklad.T("Názov organizácie je uložený. Zobrazí sa v hlavičke každej zostavy."));
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Názov sa nepodarilo uložiť."), ex);
        }
    }

    [RelayCommand]
    private void VybratLogo()
    {
        var dialog = new OpenFileDialog
        {
            Title = Preklad.T("Vyberte logo organizácie"),
            Filter = Preklad.T("Obrázky (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        NastavLogoZoSuboru(dialog.FileName);
    }

    /// <summary>Načíta logo zo súboru a uloží ho (verejné kvôli automatickému testu).</summary>
    public void NastavLogoZoSuboru(string cesta)
    {
        try
        {
            var bajty = File.ReadAllBytes(cesta);
            using (var prud = new MemoryStream(bajty))
            {
                // overenie, že je to naozaj obrázok
                BitmapFrame.Create(prud, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }

            NastaveniaTlace.UlozLogo(bajty);
            NastavLogo(bajty);
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Obrázok sa nepodarilo načítať. Použite súbor PNG alebo JPG."), ex);
        }
    }

    [RelayCommand]
    private void OdstranitLogo()
    {
        NastaveniaTlace.UlozLogo(null);
        NastavLogo(null);
    }

    private void NastavLogo(byte[]? bajty)
    {
        MaLogo = bajty is { Length: > 0 };
        if (!MaLogo)
        {
            Logo = null;
            return;
        }

        try
        {
            var obrazok = new BitmapImage();
            obrazok.BeginInit();
            obrazok.CacheOption = BitmapCacheOption.OnLoad;
            obrazok.StreamSource = new MemoryStream(bajty!);
            obrazok.EndInit();
            obrazok.Freeze();
            Logo = obrazok;
        }
        catch (Exception)
        {
            Logo = null;
        }
    }
}
