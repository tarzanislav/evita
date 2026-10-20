using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Models;
using Evita.Services;

namespace Evita.ViewModels;

/// <summary>Časť programu, ktorá vie znova načítať svoje dáta.</summary>
public interface IObnovitelny
{
    void Obnov();
}

public partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Dashboard = new DashboardViewModel(this);
        Evidencia = new EvidenciaViewModel(this);
        Kategorie = new CiselnikViewModel(TypCiselnika.Kategorie, this);
        Miestnosti = new CiselnikViewModel(TypCiselnika.Miestnosti, this);
        Osoby = new CiselnikViewModel(TypCiselnika.Osoby, this);
        Servis = new ServisViewModel(this);
        Sviatky = new SviatkyViewModel();
        ImportExport = new ImportExportViewModel(this);
        Tlac = new TlacViewModel(this);
        Kalendar = new KalendarViewModel(this);
        Ulohy = new UlohyViewModel();
        Poznamky = new PoznamkyViewModel();
        Nastavenia = new NastaveniaViewModel(this);
        CitatyVyrocia = new CitatyVyrociaViewModel(this);
        Skenovanie = new SkenovanieViewModel(this);
    }

    public DashboardViewModel Dashboard { get; }
    public EvidenciaViewModel Evidencia { get; }
    public CiselnikViewModel Kategorie { get; }
    public CiselnikViewModel Miestnosti { get; }
    public CiselnikViewModel Osoby { get; }
    public ServisViewModel Servis { get; }
    public SviatkyViewModel Sviatky { get; }
    public ImportExportViewModel ImportExport { get; }
    public TlacViewModel Tlac { get; }
    public KalendarViewModel Kalendar { get; }
    public UlohyViewModel Ulohy { get; }
    public PoznamkyViewModel Poznamky { get; }
    public NastaveniaViewModel Nastavenia { get; }
    public CitatyVyrociaViewModel CitatyVyrocia { get; }
    public SkenovanieViewModel Skenovanie { get; }

    public string TitulokOkna => Preklad.F($"EviTA {AktualizaciaService.AktualnaVerzia} – evidencia IT techniky");

    public string VerziaText =>
        Preklad.F($"Verzia {typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0"}");

    [ObservableProperty]
    private object? _aktualnyObsah;

    [ObservableProperty]
    private bool _ukazkoveData;

    /// <summary>Novšia verzia programu zverejnená na GitHube (ak existuje).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JeNovaVerzia), nameof(NovaVerziaText))]
    private DostupnaVerzia? _novaVerzia;

    public bool JeNovaVerzia => NovaVerzia is not null;

    public string NovaVerziaText => NovaVerzia is null
        ? ""
        : Preklad.F($"Je dostupná nová verzia EviTA {NovaVerzia.Verzia} (máte {AktualizaciaService.AktualnaVerzia}).");

    /// <summary>Žiadosť pre hlavné okno: zobraziť ponuku aktualizácie.</summary>
    public event Action<DostupnaVerzia>? ZiadostOAktualizaciu;

    public void ZobrazitAktualizaciu(DostupnaVerzia verzia)
    {
        NovaVerzia = verzia;
        ZiadostOAktualizaciu?.Invoke(verzia);
    }

    [RelayCommand]
    private void Aktualizovat()
    {
        if (NovaVerzia is { } verzia)
        {
            ZobrazitAktualizaciu(verzia);
        }
    }

    /// <summary>Žiadosť pre hlavné okno: prepnúť menu na sekciu (okno potom zavolá Prejdi).</summary>
    public event Action<string>? ZiadostOSekciu;

    public void PrejdiNaSekciu(string sekcia)
    {
        if (ZiadostOSekciu is null)
        {
            Prejdi(sekcia);
        }
        else
        {
            ZiadostOSekciu(sekcia);
        }
    }

    public void Prejdi(string sekcia)
    {
        object obsah = sekcia switch
        {
            "evidencia" => Evidencia,
            "kategorie" => Kategorie,
            "miestnosti" => Miestnosti,
            "osoby" => Osoby,
            "servis" => Servis,
            "sviatky" => Sviatky,
            "import" => ImportExport,
            "tlac" => Tlac,
            "kalendar" => Kalendar,
            "ulohy" => Ulohy,
            "poznamky" => Poznamky,
            "nastavenia" => Nastavenia,
            "kalendarium" => CitatyVyrocia,
            "skenovanie" => Skenovanie,
            _ => Dashboard,
        };

        if (obsah is IObnovitelny obnovitelny)
        {
            obnovitelny.Obnov();
        }

        AktualnyObsah = obsah;
        AktualizujStav();

        // kým je otvorená obrazovka Skenovanie, kódy sa zobrazujú v nej
        if (obsah == Skenovanie)
        {
            SkenerCentrum.Zaregistruj(Skenovanie);
        }
        else
        {
            SkenerCentrum.Odregistruj(Skenovanie);
        }
    }

    /// <summary>
    /// Kód naskenovaný mimo obrazovky Skenovanie a inventúry: jedna nájdená položka sa otvorí, viac položiek sa zobrazí
    /// v evidencii, neznámy kód na obrazovke Skenovanie (s ponukou pridať novú položku).
    /// </summary>
    public VysledokSpracovania SpracujSken(Sken sken)
    {
        var vysledok = Skenovanie.Vyhladaj(sken);
        if (SkenovanieViewModel.JeOtvoreneIneOkno())
        {
            // otvorený dialóg (karta položky …) – kód sa len zapíše do zoznamu posledných skenov
            return new VysledokSpracovania(vysledok.Najdene, vysledok.Popis);
        }

        if (vysledok.Polozky.Count == 1 && SkenovanieService.Nastavenie(SkenovanieService.KlucOtvoritKartu, true))
        {
            var id = vysledok.Polozky[0].Id;
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => Skenovanie.OtvorPolozku(id));
        }
        else if (vysledok.Najdene)
        {
            Evidencia.Obnov();
            Evidencia.ZobrazPolozky(vysledok.Polozky.Select(p => p.Id), Preklad.F($"kód {vysledok.PouzityKod}"));
            PrejdiNaSekciu("evidencia");
        }
        else
        {
            PrejdiNaSekciu("skenovanie");
        }

        return new VysledokSpracovania(vysledok.Najdene, vysledok.Popis);
    }

    /// <summary>Volá sa po zmene dát, ktorá sa týka viacerých častí programu.</summary>
    public void DataZmenene()
    {
        Evidencia.OznacNaObnovenie();
        AktualizujStav();
    }

    /// <summary>Volá sa po zálohe na pozadí – obnoví stav záloh na dashboarde a v servise.</summary>
    public void PoZmeneZaloh()
    {
        if (AktualnyObsah is IObnovitelny obnovitelny and (DashboardViewModel or ServisViewModel))
        {
            obnovitelny.Obnov();
        }
    }

    /// <summary>Po načítaní konfigurácie – panely dashboardu a ďalšie nastavenia sa načítajú znova.</summary>
    public void PoZmeneKonfiguracie()
    {
        Dashboard.NacitajPanely();
        Evidencia.OznacNaObnovenie();
    }

    public void AktualizujStav()
    {
        UkazkoveData = Databaza.SuUkazkoveData();
    }
}
