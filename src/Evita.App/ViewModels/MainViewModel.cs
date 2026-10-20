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
    }

    public DashboardViewModel Dashboard { get; }
    public EvidenciaViewModel Evidencia { get; }
    public CiselnikViewModel Kategorie { get; }
    public CiselnikViewModel Miestnosti { get; }
    public CiselnikViewModel Osoby { get; }
    public ServisViewModel Servis { get; }

    public string TitulokOkna => $"EviTA {AktualizaciaService.AktualnaVerzia} – evidencia IT techniky";

    public string VerziaText =>
        "Verzia " + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");

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
        : $"Je dostupná nová verzia EviTA {NovaVerzia.Verzia} (máte {AktualizaciaService.AktualnaVerzia}).";

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
            _ => Dashboard,
        };

        if (obsah is IObnovitelny obnovitelny)
        {
            obnovitelny.Obnov();
        }

        AktualnyObsah = obsah;
        AktualizujStav();
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

    public void AktualizujStav()
    {
        UkazkoveData = Databaza.SuUkazkoveData();
    }
}
