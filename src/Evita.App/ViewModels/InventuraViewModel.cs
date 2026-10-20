using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;

namespace Evita.ViewModels;

/// <summary>Okno inventúry: skenovanie po miestnostiach a vyhodnotenie.</summary>
public partial class InventuraViewModel : ObservableObject, IPrijimacSkenov
{
    private readonly InventuraService _sluzba = new();
    private readonly CiselnikService _ciselniky = new();
    private List<PolozkaInventury> _vsetky = new();
    private bool _nacitavam;

    public InventuraViewModel(int inventuraId)
    {
        InventuraId = inventuraId;
        var inventura = _sluzba.Nacitaj(inventuraId) ?? throw new EvitaException(Preklad.T("Inventúra už neexistuje."));
        _nacitavam = true;
        Nazov = inventura.Nazov;
        Miestnosti = new List<Volba> { new(0, Preklad.T("(vyberte miestnosť)")) }
            .Concat(_ciselniky.Volby(TypCiselnika.Miestnosti, inventura.AktualnaMiestnostId, null))
            .ToList();
        MiestnostId = inventura.AktualnaMiestnostId is int m && Miestnosti.Any(v => v.Id == m) ? m : 0;
        _nacitavam = false;
        Obnov();
    }

    public int InventuraId { get; }
    public string Nazov { get; }
    public List<Volba> Miestnosti { get; }

    [ObservableProperty] private int _miestnostId;
    [ObservableProperty] private bool _lenChybajuce;
    [ObservableProperty] private bool _ukoncena;
    [ObservableProperty] private List<PolozkaInventury> _polozkyMiestnosti = new();
    [ObservableProperty] private List<PolozkaInventury> _najdeneInde = new();
    [ObservableProperty] private List<ZaznamInventury> _zaznamy = new();
    [ObservableProperty] private ZaznamInventury? _vybranyZaznam;
    [ObservableProperty] private string _miestnostText = "";
    [ObservableProperty] private string _celkomText = "";
    [ObservableProperty] private double _percento;
    [ObservableProperty] private string _posledny = "";

    /// <summary>0 = bez výsledku, 1 = v poriadku, 2 = upozornenie, 3 = chyba (farba pruhu s výsledkom).</summary>
    [ObservableProperty] private int _druhPosledneho;

    public string StavText => Ukoncena ? Preklad.T("ukončená") : Preklad.T("prebieha");

    private int? Miestnost => MiestnostId > 0 ? MiestnostId : null;

    partial void OnMiestnostIdChanged(int value)
    {
        if (_nacitavam)
        {
            return;
        }

        try
        {
            _sluzba.NastavMiestnost(InventuraId, Miestnost);
        }
        catch (Exception)
        {
            // miestnosť sa len nezapamätá
        }

        Posledny = "";
        DruhPosledneho = 0;
        Filtruj();
    }

    partial void OnLenChybajuceChanged(bool value) => Filtruj();

    partial void OnUkoncenaChanged(bool value) => OnPropertyChanged(nameof(StavText));

    public void Obnov()
    {
        Ukoncena = _sluzba.Nacitaj(InventuraId)?.Stav == StavInventury.Ukoncena;
        _vsetky = _sluzba.Polozky(InventuraId);
        Zaznamy = _sluzba.Zaznamy(InventuraId, 300);
        var aktivne = _vsetky.Where(p => !p.Vyradena).ToList();
        var najdene = aktivne.Count(p => p.Najdena);
        CelkomText = Preklad.F($"Spolu nájdené {najdene} z {aktivne.Count} položiek · chýba {aktivne.Count - najdene} · v inej miestnosti {aktivne.Count(p => p.Inde)}");
        Percento = aktivne.Count == 0 ? 0 : 100.0 * najdene / aktivne.Count;
        Filtruj();
    }

    private void Filtruj()
    {
        var miestnost = Miestnost;
        var tu = _vsetky.Where(p => !p.Vyradena && p.MiestnostId == miestnost).ToList();
        PolozkyMiestnosti = (LenChybajuce ? tu.Where(p => !p.Najdena || p.Inde) : tu)
            .OrderBy(p => p.Najdena && !p.Inde)
            .ThenBy(p => p.PrirastkoveCislo)
            .ToList();
        NajdeneInde = _vsetky.Where(p => p.Najdena && p.NajdenaV == miestnost && (p.Inde || p.Vyradena)).ToList();
        var najdeneTu = tu.Count(p => p.Najdena && !p.Inde);
        MiestnostText = miestnost is null
            ? Preklad.F($"Položky bez miestnosti: nájdené {najdeneTu} z {tu.Count}")
            : Preklad.F($"V tejto miestnosti: nájdené {najdeneTu} z {tu.Count}");
    }

    // ------------------------------------------------------------------ skenovanie

    public VysledokSpracovania Prijmi(Sken sken)
    {
        if (Ukoncena)
        {
            Posledny = Preklad.T("Inventúra je ukončená. Ak chcete pokračovať, otvorte ju znova.");
            DruhPosledneho = 3;
            return new VysledokSpracovania(false, Posledny);
        }

        try
        {
            var vysledok = _sluzba.Zaznamenaj(InventuraId, sken, Miestnost);
            Posledny = vysledok.Text;
            DruhPosledneho = vysledok.Druh switch
            {
                VysledokZaznamu.Najdena or VysledokZaznamu.UzNaskenovana => 1,
                VysledokZaznamu.NajdenaInde or VysledokZaznamu.BezMiestnosti => 2,
                _ => 3,
            };
            Obnov();
            return new VysledokSpracovania(vysledok.Uspech, vysledok.Text);
        }
        catch (EvitaException ex)
        {
            Posledny = ex.Message;
            DruhPosledneho = 3;
            return new VysledokSpracovania(false, ex.Message);
        }
    }

    [RelayCommand]
    private void OznacitRucne(PolozkaInventury? polozka)
    {
        if (polozka is null)
        {
            return;
        }

        if (Ukoncena)
        {
            Dialogy.Info(Preklad.T("Inventúra je ukončená. Ak chcete pokračovať, otvorte ju znova."));
            return;
        }

        try
        {
            if (polozka.Najdena && polozka.NajdenaV == Miestnost)
            {
                _sluzba.ZrusPolozku(InventuraId, polozka.Id);
                Posledny = Preklad.F($"{polozka.Popis} – označenie zrušené.");
                DruhPosledneho = 0;
            }
            else
            {
                var vysledok = _sluzba.OznacRucne(InventuraId, polozka.Id, Miestnost);
                Posledny = vysledok.Text;
                DruhPosledneho = vysledok.Uspech ? 1 : 2;
            }

            Obnov();
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
    }

    [RelayCommand]
    private void ZrusitZaznam()
    {
        if (VybranyZaznam is not { } zaznam)
        {
            Dialogy.Info(Preklad.T("Vyberte v zozname naskenovaných kódov záznam, ktorý chcete zrušiť."));
            return;
        }

        _sluzba.ZrusZaznam(zaznam.Id);
        Obnov();
    }

    [RelayCommand]
    private void PresunutSem(PolozkaInventury? polozka)
    {
        var zoznam = polozka is null ? NajdeneInde.Where(p => !p.Vyradena).ToList() : new List<PolozkaInventury> { polozka };
        if (zoznam.Count == 0 || Miestnost is null)
        {
            return;
        }

        var nazov = Miestnosti.FirstOrDefault(v => v.Id == MiestnostId)?.Nazov ?? "";
        var otazka = zoznam.Count == 1
            ? Preklad.F($"Zapísať do evidencie, že položka {zoznam[0].Popis} je v miestnosti {nazov}?")
            : Preklad.F($"Zapísať do evidencie, že {zoznam.Count} položiek je v miestnosti {nazov}?");
        if (!Dialogy.Potvrd(otazka))
        {
            return;
        }

        var presunute = _sluzba.PresunNajdene(zoznam);
        ZmenaEvidencie = true;
        Posledny = Preklad.F($"Presunuté do miestnosti {nazov}: {presunute}");
        DruhPosledneho = 1;
        Obnov();
    }

    /// <summary>Počas inventúry sa zmenila evidencia (presuny) – po zatvorení treba obnoviť zoznamy.</summary>
    public bool ZmenaEvidencie { get; private set; }

    [RelayCommand]
    private void Protokol()
    {
        try
        {
            var id = InventuraId;
            var okno = new NahladWindow(() => new InventuraService().Protokol(id), false, Preklad.T("Inventúrny protokol"),
                $"Inventura_{DateTime.Today:yyyy-MM-dd}.pdf")
            {
                Owner = Dialogy.Vlastnik,
            };
            okno.ShowDialog();
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Protokol sa nepodarilo vytvoriť."), ex);
        }
    }

    [RelayCommand]
    private void Ukoncit()
    {
        if (Ukoncena)
        {
            _sluzba.ZnovaOtvor(InventuraId);
            Obnov();
            return;
        }

        var chyba = _vsetky.Count(p => !p.Vyradena && !p.Najdena);
        var otazka = chyba > 0
            ? Preklad.F($"Ukončiť inventúru? {chyba} položiek sa nenašlo – v protokole budú ako chýbajúce.")
            : Preklad.T("Ukončiť inventúru? Všetky položky sa našli.");
        if (!Dialogy.Potvrd(otazka))
        {
            return;
        }

        _sluzba.Ukonci(InventuraId);
        Obnov();
        Protokol();
    }

    [RelayCommand]
    private void Kamera() => KameraWindow.Otvor();

    [RelayCommand]
    private void Mobil() => MobilWindow.Otvor();
}
