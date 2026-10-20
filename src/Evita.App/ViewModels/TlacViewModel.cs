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
        new(Zoskupenie.Kategoria, "kategórie"),
        new(Zoskupenie.Miestnost, "miestnosti"),
        new(Zoskupenie.Pridelenie, "pridelenia (osoby)"),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UkazObdobie), nameof(UkazVyradene), nameof(UkazPodpis), nameof(UkazNovuStranu),
        nameof(UkazStlpce), nameof(UkazZoskupenie), nameof(UkazMiestnost), nameof(UkazOsobu), nameof(UkazKategoriu),
        nameof(UkazPolozku), nameof(Dostupna), nameof(Poznamka), nameof(TextPodpisu), nameof(PopisVyfiltrovanych), nameof(UkazKalendar))]
    private PopisZostavy _vybrana;

    partial void OnVybranaChanged(PopisZostavy value) => NastavPodlaZostavy();

    public bool UkazObdobie => Vybrana.MaObdobie;
    public bool UkazVyradene => Vybrana.MaVyradene;
    public bool UkazPodpis => Vybrana.MaPodpis;
    public bool UkazNovuStranu => Vybrana.MaNovuStranu;
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
        ? "Zoznam zverenej techniky s podpisom (prevzal / odovzdal)"
        : "Inventúrny zoznam s riadkami na podpis";

    public string PopisVyfiltrovanych
    {
        get
        {
            if (Vybrana.Druh != DruhZostavy.Vyfiltrovane)
            {
                return "";
            }

            var (id, popis) = Vyfiltrovane();
            return $"Vytlačí sa {ExportService.PocetPoloziek(id.Count)} zobrazených v Evidencii" +
                   (string.IsNullOrWhiteSpace(popis) ? " (bez filtra)." : $" – filter: {popis}.") +
                   " Filter zmeníte na obrazovke Evidencia.";
        }
    }

    [ObservableProperty] private DateTime? _od;
    [ObservableProperty] private DateTime? _do;
    [ObservableProperty] private bool _vrataneVyradenych;
    [ObservableProperty] private bool _podpis = true;
    [ObservableProperty] private bool _kazdaNaNovejStrane = true;
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
        Miestnosti = new List<Volba> { new(0, "(všetky)") }.Concat(_ciselniky.Volby(TypCiselnika.Miestnosti, null, null)).ToList();
        Osoby = new List<Volba> { new(0, "(všetci)") }.Concat(_ciselniky.Volby(TypCiselnika.Osoby, null, null)).ToList();
        Kategorie = new List<Volba> { new(0, "(všetky)") }.Concat(_ciselniky.Volby(TypCiselnika.Kategorie, null, null)).ToList();
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
            throw new EvitaException("Dátum „od“ je neskôr ako dátum „do“.");
        }

        int? cislo = null;
        if (UkazPolozku && !string.IsNullOrWhiteSpace(PrirastkoveCislo))
        {
            if (!int.TryParse(PrirastkoveCislo.Trim(), out var c) || c < 1)
            {
                throw new EvitaException("Prírastkové číslo položky musí byť kladné celé číslo.");
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
            Dialogy.Chyba("Zostavu sa nepodarilo vytvoriť.", ex);
            return null;
        }
    }

    [RelayCommand]
    private void Nahlad() => VytvorNahlad()?.ShowDialog();

    /// <summary>Pre automatický test: cesta k PDF bez okna Uložiť ako.</summary>
    public string? CestaBezDialogu { get; set; }

    [RelayCommand]
    private void UlozitPdf()
    {
        try
        {
            var moznosti = Moznosti();
            NahladWindow.UlozPdf(() => _zostavy.Vytvor(moznosti), ZostavyService.NazovSuboru(Vybrana.Druh), CestaBezDialogu);
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
            Dialogy.Chyba("Tlač sa nepodarila.", ex);
        }
    }

    // ------------------------------------------------------------------ hlavička: organizácia a logo

    [RelayCommand]
    private void UlozitOrganizaciu()
    {
        try
        {
            NastaveniaTlace.UlozNazov(NazovOrganizacie);
            Dialogy.Info("Názov organizácie je uložený. Zobrazí sa v hlavičke každej zostavy.");
        }
        catch (Exception ex)
        {
            Dialogy.Chyba("Názov sa nepodarilo uložiť.", ex);
        }
    }

    [RelayCommand]
    private void VybratLogo()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Vyberte logo organizácie",
            Filter = "Obrázky (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
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
            Dialogy.Chyba("Obrázok sa nepodarilo načítať. Použite súbor PNG alebo JPG.", ex);
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
