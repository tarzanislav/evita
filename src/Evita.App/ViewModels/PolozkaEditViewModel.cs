using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.ViewModels;

/// <summary>Karta položky – nová, úprava alebo kópia.</summary>
public partial class PolozkaEditViewModel : ObservableObject
{
    private readonly PolozkaService _sluzba = new();
    private readonly CiselnikService _ciselniky = new();
    private readonly int _id;
    private readonly int _verzia;

    /// <summary>Udalosti kalendára naviazané na položku (rok dozadu, dva dopredu) vrátane záruky a revízie.</summary>
    [ObservableProperty] private List<VyskytUdalosti> _udalosti = new();

    private void NacitajUdalosti()
    {
        try
        {
            var dnes = DateTime.Today;
            Udalosti = new KalendarService().Vyskyty(dnes.AddYears(-1), dnes.AddYears(2), _id);
        }
        catch (Exception)
        {
            Udalosti = new List<VyskytUdalosti>();
        }
    }

    [RelayCommand]
    private void NovaUdalost()
    {
        var okno = new Views.UdalostWindow(new UdalostEditViewModel(null, DateTime.Today, _id)) { Owner = Dialogy.Vlastnik };
        if (okno.ShowDialog() == true)
        {
            NacitajUdalosti();
        }
    }

    [RelayCommand]
    private void OtvorUdalost(VyskytUdalosti? vyskyt)
    {
        if (vyskyt?.UdalostId is not int id)
        {
            return;
        }

        var okno = new Views.UdalostWindow(new UdalostEditViewModel(id, null, null)) { Owner = Dialogy.Vlastnik };
        if (okno.ShowDialog() == true)
        {
            NacitajUdalosti();
        }
    }

    // ------------------------------------------------------------------ poznámky a úlohy k položke

    [ObservableProperty] private List<PoznamkaRiadok> _poznamky = new();
    [ObservableProperty] private List<UlohaRiadok> _ulohyPolozky = new();
    [ObservableProperty] private string _novaUloha = "";

    private void NacitajPoznamkyAUlohy()
    {
        try
        {
            Poznamky = new PoznamkyService().PrePolozku(_id);
            UlohyPolozky = new UlohyService().Nacitaj(ajHotove: true, polozkaId: _id);
        }
        catch (Exception)
        {
            Poznamky = new List<PoznamkaRiadok>();
            UlohyPolozky = new List<UlohaRiadok>();
        }
    }

    [RelayCommand]
    private void NovaPoznamka()
    {
        if (Views.PoznamkaWindow.Otvor(new PoznamkaEditViewModel(null, polozkaId: _id)))
        {
            NacitajPoznamkyAUlohy();
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

        NacitajPoznamkyAUlohy();
    }

    [RelayCommand]
    private void PridatUlohu()
    {
        if (string.IsNullOrWhiteSpace(NovaUloha))
        {
            return;
        }

        try
        {
            new UlohyService().Pridaj(NovaUloha, polozkaId: _id);
            NovaUloha = "";
            NacitajPoznamkyAUlohy();
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
    }

    [RelayCommand]
    private void PrepnutHotovo(UlohaRiadok? uloha)
    {
        if (uloha is null)
        {
            return;
        }

        new UlohyService().NastavHotovo(uloha.Id, !uloha.Hotovo);
        NacitajPoznamkyAUlohy();
    }

    /// <param name="id">Id upravovanej položky, alebo null pre novú.</param>
    /// <param name="kopiaZ">Id položky, ktorá sa má skopírovať do novej.</param>
    public PolozkaEditViewModel(int? id, int? kopiaZ)
    {
        Polozka? zdroj = null;
        if (id is int upravovana)
        {
            zdroj = _sluzba.Nacitaj(upravovana) ?? throw new EvitaException(Preklad.T("Položka už neexistuje."));
            _id = zdroj.Id;
            _verzia = zdroj.Verzia;
            Titulok = Preklad.F($"Položka {zdroj.PrirastkoveCislo} – {zdroj.Nazov}");
            PrirastkoveCisloText = zdroj.PrirastkoveCislo.ToString();
            DatumPridaniaText = Sk.DatumCas(zdroj.DatumPridania);
            DatumZmenyText = Sk.DatumCas(zdroj.DatumZmeny);
            Historia = _sluzba.Historia(zdroj.Id);
            NacitajUdalosti();
            NacitajPoznamkyAUlohy();
        }
        else
        {
            JeNova = true;
            using var db = Databaza.Otvor();
            PrirastkoveCisloText = Preklad.F($"{Databaza.DalsiePrirastkoveCislo(db)} (pridelí sa pri uložení)");
            if (kopiaZ is int kopia)
            {
                zdroj = _sluzba.Nacitaj(kopia);
                Titulok = Preklad.T("Nová položka (kópia)");
            }
            else
            {
                Titulok = Preklad.T("Nová položka");
            }
        }

        NacitajVolby(zdroj?.KategoriaId, zdroj?.MiestnostId, zdroj?.OsobaId);

        if (zdroj is not null)
        {
            _nazov = zdroj.Nazov;
            _kategoriaId = zdroj.KategoriaId;
            _miestnostId = zdroj.MiestnostId ?? 0;
            _osobaId = zdroj.OsobaId ?? 0;
            _vyrobca = zdroj.Vyrobca ?? "";
            _model = zdroj.Model ?? "";
            _ean = zdroj.Ean ?? "";
            _stav = zdroj.Stav;
            _datumNakupu = zdroj.DatumNakupu;
            _cenaText = zdroj.Cena?.ToString("0.00", Sk.Kultura) ?? "";
            _zarukaDo = zdroj.ZarukaDo;
            _dalsiaRevizia = zdroj.DalsiaRevizia;
            _poznamka = zdroj.Poznamka ?? "";

            // pri kópii sa jedinečné údaje neprenášajú
            if (!JeNova)
            {
                _serioveCislo = zdroj.SerioveCislo ?? "";
                _inventarneCislo = zdroj.InventarneCislo ?? "";
                _datumVyradenia = zdroj.DatumVyradenia;
                _dovodVyradenia = zdroj.DovodVyradenia ?? "";
            }
            else if (_stav == StavPolozky.Vyradene)
            {
                _stav = StavPolozky.VPrevadzke;
            }
        }
    }

    public string Titulok { get; }
    public bool JeNova { get; }
    public string PrirastkoveCisloText { get; }
    public string DatumPridaniaText { get; } = "";
    public string DatumZmenyText { get; } = "";
    public List<HistoriaZmeny> Historia { get; } = new();
    public bool MaHistoriu => Historia.Count > 0;

    public IReadOnlyList<VolbaStavu> Stavy => StavyPoloziek.Vsetky;

    [ObservableProperty] private List<Volba> _kategorie = new();
    [ObservableProperty] private List<Volba> _miestnosti = new();
    [ObservableProperty] private List<Volba> _osoby = new();

    [ObservableProperty] private string _nazov = "";
    [ObservableProperty] private int _kategoriaId;
    [ObservableProperty] private int _miestnostId;
    [ObservableProperty] private int _osobaId;
    [ObservableProperty] private string _vyrobca = "";
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _serioveCislo = "";
    [ObservableProperty] private string _inventarneCislo = "";
    [ObservableProperty] private string _ean = "";
    [ObservableProperty] private bool _hladamEan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JeVyradena))]
    private StavPolozky _stav = StavPolozky.VPrevadzke;

    [ObservableProperty] private DateTime? _datumNakupu;
    [ObservableProperty] private string _cenaText = "";
    [ObservableProperty] private DateTime? _zarukaDo;
    [ObservableProperty] private DateTime? _dalsiaRevizia;
    [ObservableProperty] private DateTime? _datumVyradenia;
    [ObservableProperty] private string _dovodVyradenia = "";
    [ObservableProperty] private string _poznamka = "";

    [ObservableProperty] private int _pocetKusov = 1;
    [ObservableProperty] private string _serioveCislaHromadne = "";

    public bool JeVyradena => Stav == StavPolozky.Vyradene;

    /// <summary>Id uložených položiek (pri hromadnom pridaní ich je viac).</summary>
    public List<int> UlozeneId { get; private set; } = new();

    /// <summary>Žiadosť na zatvorenie okna s výsledkom.</summary>
    public event Action<bool>? Zatvorit;

    partial void OnStavChanged(StavPolozky value)
    {
        if (value == StavPolozky.Vyradene && DatumVyradenia is null)
        {
            DatumVyradenia = DateTime.Today;
        }
    }

    [RelayCommand]
    private void Ulozit()
    {
        if (string.IsNullOrWhiteSpace(Nazov))
        {
            Dialogy.Upozornenie(Preklad.T("Zadajte názov technológie."));
            return;
        }

        if (KategoriaId <= 0)
        {
            Dialogy.Upozornenie(Preklad.T("Vyberte kategóriu."));
            return;
        }

        if (!string.IsNullOrWhiteSpace(Ean) && !KodyEvita.JeEan(Ean)
            && !Dialogy.Potvrd(Preklad.T("EAN nemá platný tvar (8, 12 alebo 13 číslic s kontrolnou číslicou). Uložiť ho aj tak?")))
        {
            return;
        }

        if (!Sk.SkusPrecitatSumu(CenaText, out var cena))
        {
            Dialogy.Upozornenie(Preklad.T("Cenu sa nepodarilo prečítať. Napíšte ju ako číslo, napríklad 749,90."));
            return;
        }

        var p = new Polozka
        {
            Id = _id,
            Verzia = _verzia,
            Nazov = Nazov,
            KategoriaId = KategoriaId,
            MiestnostId = MiestnostId > 0 ? MiestnostId : null,
            OsobaId = OsobaId > 0 ? OsobaId : null,
            Vyrobca = Vyrobca,
            Model = Model,
            SerioveCislo = SerioveCislo,
            InventarneCislo = InventarneCislo,
            Ean = string.IsNullOrWhiteSpace(Ean) ? null : KodyEvita.NormalizujEan(Ean) ?? Ean.Trim(),
            Stav = Stav,
            DatumNakupu = DatumNakupu,
            Cena = cena,
            ZarukaDo = ZarukaDo,
            DalsiaRevizia = DalsiaRevizia,
            DatumVyradenia = DatumVyradenia,
            DovodVyradenia = DovodVyradenia,
            Poznamka = Poznamka,
        };

        if (JeNova)
        {
            if (PocetKusov < 1 || PocetKusov > 500)
            {
                Dialogy.Upozornenie(Preklad.T("Počet kusov musí byť od 1 do 500."));
                return;
            }

            var serioveCisla = SerioveCislaHromadne
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            if (PocetKusov == 1 && serioveCisla.Count == 0 && !string.IsNullOrWhiteSpace(SerioveCislo))
            {
                serioveCisla.Add(SerioveCislo);
            }

            if (PocetKusov > 1 && serioveCisla.Count > PocetKusov)
            {
                Dialogy.Upozornenie(Preklad.F($"Zadali ste {serioveCisla.Count} sériových čísel, ale počet kusov je {PocetKusov}."));
                return;
            }

            if (PocetKusov > 1 && !Dialogy.Potvrd(Preklad.F($"Pridať {PocetKusov} rovnakých položiek s po sebe idúcimi prírastkovými číslami?")))
            {
                return;
            }

            UlozeneId = _sluzba.Pridaj(p, PocetKusov, serioveCisla);
        }
        else
        {
            _sluzba.Uloz(p);
            UlozeneId = new List<int> { _id };
        }

        Zatvorit?.Invoke(true);
    }

    [RelayCommand]
    private void Zrusit() => Zatvorit?.Invoke(false);

    // ------------------------------------------------------------------ EAN a štítok

    /// <summary>Po otvorení karty hneď vyhľadať údaje podľa EAN (nová položka z naskenovaného kódu).</summary>
    public bool HladatEanPoOtvoreni { get; set; }

    [RelayCommand]
    private async Task DoplnitPodlaEan()
    {
        if (HladamEan)
        {
            return;
        }

        if (KodyEvita.NormalizujEan(Ean) is null)
        {
            Dialogy.Upozornenie(Preklad.T("Zadajte alebo naskenujte EAN z krabice výrobku (8, 12 alebo 13 číslic)."));
            return;
        }

        VysledokEan vysledok;
        HladamEan = true;
        try
        {
            vysledok = await new EanService().HladajAsync(Ean, JeNova ? null : _id);
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Údaje o výrobku sa nepodarilo vyhľadať."), ex);
            return;
        }
        finally
        {
            HladamEan = false;
        }

        Ean = vysledok.Ean;
        if (vysledok.Najdene.Count == 0)
        {
            Dialogy.Info(Preklad.F($"Výrobok s EAN {vysledok.Ean} sa nenašiel.\n\n{string.Join("\n", vysledok.Spravy)}"));
            return;
        }

        if (Dialogy.TichyRezim)
        {
            Dialogy.TicheHlasenia.Add($"[EAN] {vysledok.Najdene[0].Zdroj}: {vysledok.Najdene[0].Nazov}");
            return;
        }

        var okno = new Views.EanWindow(vysledok, Kategorie) { Owner = Dialogy.Vlastnik };
        if (okno.ShowDialog() != true)
        {
            return;
        }

        if (okno.VybranyNazov is { } nazov)
        {
            Nazov = nazov;
        }

        if (okno.VybranyVyrobca is { } vyrobca)
        {
            Vyrobca = vyrobca;
        }

        if (okno.VybranyModel is { } model)
        {
            Model = model;
        }

        if (okno.VybranaKategoria is int kategoria && Kategorie.Any(k => k.Id == kategoria))
        {
            KategoriaId = kategoria;
        }
    }

    [RelayCommand]
    private void Stitok()
    {
        if (JeNova)
        {
            Dialogy.Info(Preklad.T("Štítok sa dá vytlačiť až po uložení položky (vtedy dostane prírastkové číslo)."));
            return;
        }

        new Views.StitkyWindow(new StitkyViewModel(new[] { _id }, Titulok)) { Owner = Dialogy.Vlastnik }.ShowDialog();
    }

    [RelayCommand]
    private void NovaKategoria()
    {
        var nazov = Dialogy.Vstup(Preklad.T("Nová kategória"), Preklad.T("Názov novej kategórie:"));
        if (string.IsNullOrWhiteSpace(nazov))
        {
            return;
        }

        var id = _ciselniky.Uloz(TypCiselnika.Kategorie, 0, nazov, null, null);
        NacitajVolby(id, MiestnostId, OsobaId);
        KategoriaId = id;
    }

    [RelayCommand]
    private void NovaMiestnost()
    {
        var nazov = Dialogy.Vstup(Preklad.T("Nová miestnosť"), Preklad.T("Názov novej miestnosti (napr. Učebňa 105):"));
        if (string.IsNullOrWhiteSpace(nazov))
        {
            return;
        }

        var id = _ciselniky.Uloz(TypCiselnika.Miestnosti, 0, nazov, null, null);
        NacitajVolby(KategoriaId, id, OsobaId);
        MiestnostId = id;
    }

    [RelayCommand]
    private void NovaOsoba()
    {
        var text = Dialogy.Vstup(Preklad.T("Nová osoba"), Preklad.T("Meno a priezvisko (alebo názov útvaru):"));
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var casti = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (meno, priezvisko) = casti.Length == 2 ? (casti[0], casti[1]) : ("", casti[0]);
        var id = _ciselniky.Uloz(TypCiselnika.Osoby, 0, meno, priezvisko, null);
        NacitajVolby(KategoriaId, MiestnostId, id);
        OsobaId = id;
    }

    private void NacitajVolby(int? kategoria, int? miestnost, int? osoba)
    {
        Kategorie = _ciselniky.Volby(TypCiselnika.Kategorie, kategoria, null);
        Miestnosti = _ciselniky.Volby(TypCiselnika.Miestnosti, miestnost, Preklad.T("(nezadaná)"));
        Osoby = _ciselniky.Volby(TypCiselnika.Osoby, osoba, Preklad.T("(nikto)"));
    }
}
