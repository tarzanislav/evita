using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;

namespace Evita.ViewModels;

/// <summary>Jeden naskenovaný kód v zozname posledných skenov.</summary>
public sealed class SkenRiadok
{
    public SkenRiadok(VysledokSkenu vysledok)
    {
        Vysledok = vysledok;
    }

    public VysledokSkenu Vysledok { get; }
    public string Cas => Sk.Cas(Vysledok.Sken.Cas);
    public string Kod => Vysledok.PouzityKod;
    public string Text => Vysledok.Polozky.Count == 1 ? Vysledok.Polozky[0].Popis : Vysledok.Popis;
    public string Detail => Vysledok.Polozky.Count == 1
        ? $"{Vysledok.Polozky[0].Detail} · {Vysledok.DruhText}"
        : Vysledok.Najdene ? Vysledok.DruhText : Vysledok.Ean is not null ? Preklad.T("EAN výrobku – údaje o ňom sa dajú vyhľadať na internete") : "";

    public string Zdroj => Vysledok.Sken.Zdroj switch
    {
        ZdrojSkenu.Kamera => Preklad.T("kamera"),
        ZdrojSkenu.Mobil => Preklad.T("mobil"),
        ZdrojSkenu.Klavesnica => Preklad.T("ručne"),
        _ => Preklad.T("čítačka"),
    };

    public bool Najdene => Vysledok.Najdene;
    public bool JednaPolozka => Vysledok.Polozky.Count == 1;
    public bool ViacPoloziek => Vysledok.Polozky.Count > 1;
    public bool Nenajdene => !Vysledok.Najdene;
    public bool JeEan => Vysledok.Ean is not null && !Vysledok.Najdene;
}

public partial class SkenovanieViewModel : ObservableObject, IObnovitelny, IPrijimacSkenov
{
    private readonly MainViewModel _hlavne;
    private readonly SkenovanieService _sluzba = new();
    private readonly InventuraService _inventury = new();
    private readonly CiselnikService _ciselniky = new();
    private bool _nacitavam;

    public SkenovanieViewModel(MainViewModel hlavne)
    {
        _hlavne = hlavne;
    }

    public ObservableCollection<SkenRiadok> Posledne { get; } = new();

    [ObservableProperty] private List<InventuraRiadok> _inventury = new();
    [ObservableProperty] private InventuraRiadok? _vybranaInventura;

    [ObservableProperty] private List<Volba> _miestnostiStitkov = new();
    [ObservableProperty] private List<Volba> _kategorieStitkov = new();
    [ObservableProperty] private int _miestnostStitkov;
    [ObservableProperty] private int _kategoriaStitkov;
    [ObservableProperty] private bool _ajVyradene;
    [ObservableProperty] private string _pocetStitkovText = "";

    [ObservableProperty] private bool _zvuk = true;
    [ObservableProperty] private bool _otvoritKartu = true;
    [ObservableProperty] private bool _citackaVsade = true;
    [ObservableProperty] private string _port = SkenovanieService.PredvolenyPort.ToString();
    [ObservableProperty] private string _icecat = "";

    public bool MaPosledne => Posledne.Count > 0;

    public void Obnov()
    {
        _nacitavam = true;
        try
        {
            Zvuk = SkenovanieService.Nastavenie(SkenovanieService.KlucZvuk, true);
            OtvoritKartu = SkenovanieService.Nastavenie(SkenovanieService.KlucOtvoritKartu, true);
            CitackaVsade = SkenovanieService.Nastavenie(SkenovanieService.KlucCitackaVsade, true);
            Port = SkenovanieService.PortMobilu().ToString();
            Icecat = EanService.IcecatPouzivatel() ?? "";

            var miestnost = MiestnostStitkov;
            var kategoria = KategoriaStitkov;
            MiestnostiStitkov = new List<Volba> { new(0, Preklad.T("(všetky)")), new(-1, Preklad.T("(nezadaná)")) }
                .Concat(_ciselniky.Volby(TypCiselnika.Miestnosti, null, null)).ToList();
            KategorieStitkov = new List<Volba> { new(0, Preklad.T("(všetky)")) }
                .Concat(_ciselniky.Volby(TypCiselnika.Kategorie, null, null)).ToList();
            MiestnostStitkov = MiestnostiStitkov.Any(v => v.Id == miestnost) ? miestnost : 0;
            KategoriaStitkov = KategorieStitkov.Any(v => v.Id == kategoria) ? kategoria : 0;
        }
        finally
        {
            _nacitavam = false;
        }

        NacitajInventury();
        AktualizujPocetStitkov();
    }

    private void NacitajInventury()
    {
        var vybrana = VybranaInventura?.Id;
        Inventury = _inventury.Zoznam();
        VybranaInventura = Inventury.FirstOrDefault(i => i.Id == vybrana) ?? Inventury.FirstOrDefault(i => i.Prebieha) ?? Inventury.FirstOrDefault();
    }

    // ------------------------------------------------------------------ skenovanie

    /// <summary>Kód naskenovaný, kým je otvorená obrazovka Skenovanie.</summary>
    public VysledokSpracovania Prijmi(Sken sken)
    {
        var vysledok = Vyhladaj(sken);
        if (vysledok.Polozky.Count == 1 && OtvoritKartu && !JeOtvoreneIneOkno())
        {
            Application.Current?.Dispatcher.InvokeAsync(() => OtvorPolozku(vysledok.Polozky[0].Id));
        }

        return new VysledokSpracovania(vysledok.Najdene, vysledok.Popis);
    }

    /// <summary>Vyhľadá kód a pridá ho do zoznamu posledných skenov.</summary>
    public VysledokSkenu Vyhladaj(Sken sken)
    {
        var vysledok = _sluzba.Najdi(sken);
        Posledne.Insert(0, new SkenRiadok(vysledok));
        while (Posledne.Count > 50)
        {
            Posledne.RemoveAt(Posledne.Count - 1);
        }

        OnPropertyChanged(nameof(MaPosledne));
        return vysledok;
    }

    /// <summary>Je otvorené iné okno než hlavné (karta položky, dialóg …) – vtedy sa ďalšie okno neotvára.</summary>
    public static bool JeOtvoreneIneOkno() =>
        Application.Current?.Windows.OfType<Window>()
            .Any(w => w.IsVisible && w != Application.Current.MainWindow && w is not KameraWindow and not MobilWindow) ?? false;

    [RelayCommand]
    private void VymazatZoznam()
    {
        Posledne.Clear();
        OnPropertyChanged(nameof(MaPosledne));
    }

    [RelayCommand]
    private void OtvoritRiadok(SkenRiadok? riadok)
    {
        if (riadok is null)
        {
            return;
        }

        if (riadok.JednaPolozka)
        {
            OtvorPolozku(riadok.Vysledok.Polozky[0].Id);
        }
        else if (riadok.ViacPoloziek)
        {
            ZobrazitVEvidencii(riadok);
        }
        else
        {
            NovaPolozka(riadok);
        }
    }

    [RelayCommand]
    private void ZobrazitVEvidencii(SkenRiadok? riadok)
    {
        if (riadok is null || !riadok.Najdene)
        {
            return;
        }

        _hlavne.Evidencia.Obnov();
        _hlavne.Evidencia.ZobrazPolozky(riadok.Vysledok.Polozky.Select(p => p.Id), Preklad.F($"kód {riadok.Kod}"));
        _hlavne.PrejdiNaSekciu("evidencia");
    }

    [RelayCommand]
    private void NovaPolozka(SkenRiadok? riadok)
    {
        if (riadok is null)
        {
            return;
        }

        var model = new PolozkaEditViewModel(null, null);
        if (riadok.Vysledok.Ean is { } ean)
        {
            model.Ean = ean;
            model.HladatEanPoOtvoreni = true;
        }
        else if (!riadok.Vysledok.JeStitokEviTA)
        {
            model.SerioveCislo = riadok.Kod;
        }

        var okno = new PolozkaEditWindow(model) { Owner = Dialogy.Vlastnik };
        if (okno.ShowDialog() == true)
        {
            _hlavne.DataZmenene();
            var id = model.UlozeneId.FirstOrDefault();
            if (id != 0)
            {
                var index = Posledne.IndexOf(riadok);
                var novy = new SkenRiadok(_sluzba.Najdi(riadok.Vysledok.Sken));
                if (index >= 0)
                {
                    Posledne[index] = novy;
                }
            }
        }
    }

    /// <summary>Otvorí kartu položky (z naskenovaného kódu).</summary>
    public void OtvorPolozku(int id)
    {
        try
        {
            var okno = new PolozkaEditWindow(new PolozkaEditViewModel(id, null)) { Owner = Dialogy.Vlastnik };
            if (okno.ShowDialog() == true)
            {
                _hlavne.DataZmenene();
            }
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
    }

    [RelayCommand]
    private void Kamera() => KameraWindow.Otvor();

    [RelayCommand]
    private void Mobil() => MobilWindow.Otvor();

    // ------------------------------------------------------------------ inventúra

    [RelayCommand]
    private void NovaInventura()
    {
        var bezi = Inventury.FirstOrDefault(i => i.Prebieha);
        if (bezi is not null && !Dialogy.Potvrd(Preklad.F($"Inventúra „{bezi.Nazov}“ ešte prebieha. Začať aj tak novú?")))
        {
            return;
        }

        var nazov = Dialogy.Vstup(Preklad.T("Nová inventúra"), Preklad.T("Názov inventúry:"), InventuraService.PredvolenyNazov());
        if (string.IsNullOrWhiteSpace(nazov))
        {
            return;
        }

        try
        {
            var id = _inventury.Nova(nazov);
            NacitajInventury();
            VybranaInventura = Inventury.FirstOrDefault(i => i.Id == id);
            OtvorInventuru(id);
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
    }

    [RelayCommand]
    private void PokracovatInventuru(InventuraRiadok? inventura)
    {
        inventura ??= VybranaInventura;
        if (inventura is null)
        {
            Dialogy.Info(Preklad.T("Najprv začnite novú inventúru."));
            return;
        }

        OtvorInventuru(inventura.Id);
    }

    private void OtvorInventuru(int id)
    {
        try
        {
            InventuraWindow.Otvor(id, () =>
            {
                _hlavne.DataZmenene();
                NacitajInventury();
            });
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
    }

    [RelayCommand]
    private void ProtokolInventury(InventuraRiadok? inventura)
    {
        inventura ??= VybranaInventura;
        if (inventura is null)
        {
            return;
        }

        try
        {
            var id = inventura.Id;
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
    private void VymazatInventuru(InventuraRiadok? inventura)
    {
        inventura ??= VybranaInventura;
        if (inventura is null || !Dialogy.Potvrd(Preklad.F($"Vymazať inventúru „{inventura.Nazov}“ so všetkými naskenovanými záznamami?\n\nPoložky evidencie sa nezmenia.")))
        {
            return;
        }

        _inventury.Vymaz(inventura.Id);
        NacitajInventury();
    }

    // ------------------------------------------------------------------ štítky

    partial void OnMiestnostStitkovChanged(int value) => AktualizujPocetStitkov();
    partial void OnKategoriaStitkovChanged(int value) => AktualizujPocetStitkov();
    partial void OnAjVyradeneChanged(bool value) => AktualizujPocetStitkov();

    private List<int> PolozkyNaStitky()
    {
        using var db = Databaza.Otvor();
        var polozky = db.Polozky.AsQueryable();
        if (!AjVyradene)
        {
            polozky = polozky.Where(p => p.Stav != StavPolozky.Vyradene);
        }

        var miestnost = MiestnostStitkov;
        if (miestnost == -1)
        {
            polozky = polozky.Where(p => p.MiestnostId == null);
        }
        else if (miestnost > 0)
        {
            polozky = polozky.Where(p => p.MiestnostId == miestnost);
        }

        var kategoria = KategoriaStitkov;
        if (kategoria > 0)
        {
            polozky = polozky.Where(p => p.KategoriaId == kategoria);
        }

        return polozky.OrderBy(p => p.PrirastkoveCislo).Select(p => p.Id).ToList();
    }

    private void AktualizujPocetStitkov()
    {
        if (_nacitavam)
        {
            return;
        }

        try
        {
            PocetStitkovText = Preklad.Pocet(PolozkyNaStitky().Count, "{0} položka", "{0} položky", "{0} položiek");
        }
        catch (Exception)
        {
            PocetStitkovText = "";
        }
    }

    [RelayCommand]
    private void TlacitStitky()
    {
        var idcka = PolozkyNaStitky();
        if (idcka.Count == 0)
        {
            Dialogy.Info(Preklad.T("Zvolenému výberu nezodpovedá žiadna položka."));
            return;
        }

        var casti = new List<string>();
        if (MiestnostiStitkov.FirstOrDefault(v => v.Id == MiestnostStitkov && v.Id != 0) is { } m)
        {
            casti.Add(Preklad.F($"miestnosť {m.Nazov}"));
        }

        if (KategorieStitkov.FirstOrDefault(v => v.Id == KategoriaStitkov && v.Id != 0) is { } k)
        {
            casti.Add(Preklad.F($"kategória {k.Nazov}"));
        }

        var popis = casti.Count == 0 ? Preklad.F($"všetky položky ({idcka.Count})") : $"{string.Join(", ", casti)} ({idcka.Count})";
        new StitkyWindow(new StitkyViewModel(idcka, popis)) { Owner = Dialogy.Vlastnik }.ShowDialog();
    }

    // ------------------------------------------------------------------ nastavenia

    partial void OnZvukChanged(bool value) => Uloz(SkenovanieService.KlucZvuk, value ? "1" : "0");
    partial void OnOtvoritKartuChanged(bool value) => Uloz(SkenovanieService.KlucOtvoritKartu, value ? "1" : "0");

    partial void OnCitackaVsadeChanged(bool value)
    {
        CitackaKodov.VCelomPrograme = value;
        Uloz(SkenovanieService.KlucCitackaVsade, value ? "1" : "0");
    }

    partial void OnPortChanged(string value)
    {
        if (int.TryParse(value, out var port) && port is > 1024 and < 65536)
        {
            Uloz(SkenovanieService.KlucPort, port.ToString());
        }
    }

    partial void OnIcecatChanged(string value) => Uloz(EanService.KlucIcecat, Sk.Prazdne(value));

    private void Uloz(string kluc, string? hodnota)
    {
        if (_nacitavam)
        {
            return;
        }

        try
        {
            SkenovanieService.UlozNastavenie(kluc, hodnota);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Nastavenie sa nepodarilo uložiť."), ex);
        }
    }
}
