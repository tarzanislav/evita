using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Pomocne;
using Evita.Services;
using Kalendarium;
using Microsoft.Win32;

namespace Evita.ViewModels;

/// <summary>Riadok zoznamu citátov.</summary>
public sealed record CitatRiadok(Citat Citat)
{
    public string Text => Citat.Text;
    public string Autor => Citat.Autor ?? "";
    public string JazykNazov => Preklad.Jazyky.FirstOrDefault(j => j.Kod == Citat.Jazyk)?.Nazov ?? Citat.Jazyk;
    public string PovodText => CitatyVyrociaService.NazovPovodu(Citat.Povod);
    public bool Skryty => Citat.JeSkryty;
}

public sealed record VolbaJazyka(string? Kod, string Nazov);

public sealed record VolbaDruhuVyrocia(DruhVyrocia Druh, string Nazov);

/// <summary>Obrazovka Citáty a výročia: správa citátov, výročia podľa dňa, sťahovanie z Wikipédie, export a import.</summary>
public partial class CitatyVyrociaViewModel : ObservableObject, IObnovitelny
{
    private readonly MainViewModel? _hlavne;
    private CancellationTokenSource? _zrusenie;

    public CitatyVyrociaViewModel(MainViewModel? hlavne)
    {
        _hlavne = hlavne;
        _jazykFiltra = CitatyVyrociaService.Jazyk;
        _editJazyk = CitatyVyrociaService.Jazyk;
    }

    private static KalendariumDb Db => CitatyVyrociaService.Db;

    public void Obnov()
    {
        NacitajCitaty();
        NacitajVyrocia();
        StahovatAutomaticky = CitatyVyrociaService.StahovatAutomaticky;
    }

    // ------------------------------------------------------------------ citáty

    public IReadOnlyList<VolbaJazyka> JazykyFiltra { get; } =
        new[] { new VolbaJazyka(null, Preklad.T("všetky jazyky")) }.Concat(Preklad.Jazyky.Select(j => new VolbaJazyka(j.Kod, j.Nazov))).ToList();

    public IReadOnlyList<VolbaJazyka> Jazyky { get; } = Preklad.Jazyky.Select(j => new VolbaJazyka(j.Kod, j.Nazov)).ToList();

    [ObservableProperty] private string? _jazykFiltra;
    [ObservableProperty] private string _hladat = "";
    [ObservableProperty] private bool _ajSkryteCitaty;
    [ObservableProperty] private List<CitatRiadok> _citaty = new();
    [ObservableProperty] private string _suhrnCitatov = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JeVybranyCitat), nameof(TextSkrytiaCitatu), nameof(NadpisEditora))]
    private CitatRiadok? _vybranyCitat;

    [ObservableProperty] private string _editText = "";
    [ObservableProperty] private string _editAutor = "";
    [ObservableProperty] private string _editJazyk;

    public bool JeVybranyCitat => VybranyCitat is not null;

    public string NadpisEditora => VybranyCitat is null ? Preklad.T("Nový citát") : Preklad.T("Úprava citátu");

    public string TextSkrytiaCitatu => VybranyCitat?.Skryty == true ? Preklad.T("Znova zobrazovať") : Preklad.T("Skryť");

    partial void OnJazykFiltraChanged(string? value) => NacitajCitaty();
    partial void OnHladatChanged(string value) => NacitajCitaty();
    partial void OnAjSkryteCitatyChanged(bool value) => NacitajCitaty();

    partial void OnVybranyCitatChanged(CitatRiadok? value)
    {
        EditText = value?.Text ?? "";
        EditAutor = value?.Autor ?? "";
        EditJazyk = value?.Citat.Jazyk ?? JazykFiltra ?? CitatyVyrociaService.Jazyk;
    }

    private void NacitajCitaty()
    {
        try
        {
            var hladane = Sk.BezDiakritiky(Hladat.Trim());
            var vsetky = Db.Citaty(JazykFiltra, AjSkryteCitaty);
            Citaty = vsetky
                .Where(c => hladane.Length == 0 || Sk.BezDiakritiky(c.Text + " " + c.Autor).Contains(hladane))
                .OrderBy(c => c.JeSkryty).ThenBy(c => c.Autor ?? "", Sk.Porovnanie).ThenBy(c => c.Text, Sk.Porovnanie)
                .Select(c => new CitatRiadok(c))
                .ToList();
            SuhrnCitatov = Preklad.F($"Zobrazené: {Citaty.Count} z {vsetky.Count}") + " · " + Preklad.F($"citátov spolu: {Db.PocetCitatov()}");
            var id = VybranyCitat?.Citat.Id;
            VybranyCitat = Citaty.FirstOrDefault(c => c.Citat.Id == id);
        }
        catch (Exception ex)
        {
            SuhrnCitatov = ex.Message;
        }
    }

    [RelayCommand]
    private void NovyCitat()
    {
        VybranyCitat = null;
        EditText = "";
        EditAutor = "";
        EditJazyk = JazykFiltra ?? CitatyVyrociaService.Jazyk;
    }

    [RelayCommand]
    private void UlozitCitat()
    {
        if (string.IsNullOrWhiteSpace(EditText))
        {
            Dialogy.Upozornenie(Preklad.T("Napíšte text citátu."));
            return;
        }

        if (EditText.Trim().Length > 1000)
        {
            Dialogy.Upozornenie(Preklad.T("Citát je príliš dlhý (najviac 1000 znakov)."));
            return;
        }

        try
        {
            int id;
            if (VybranyCitat is { } vybrany)
            {
                Db.UpravCitat(vybrany.Citat.Id, EditText, EditAutor);
                id = vybrany.Citat.Id;
            }
            else
            {
                id = Db.PridajCitat(EditJazyk, EditText, EditAutor).Id;
            }

            NacitajCitaty();
            VybranyCitat = Citaty.FirstOrDefault(c => c.Citat.Id == id);
            _hlavne?.Dashboard.Obnov();
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Citát sa nepodarilo uložiť."), ex);
        }
    }

    [RelayCommand]
    private void PrepnutSkrytieCitatu()
    {
        if (VybranyCitat is not { } vybrany)
        {
            return;
        }

        Db.NastavSkrytyCitat(vybrany.Citat.Id, !vybrany.Skryty);
        if (!vybrany.Skryty && !AjSkryteCitaty)
        {
            VybranyCitat = null;
        }

        NacitajCitaty();
    }

    [RelayCommand]
    private void VymazatCitat()
    {
        if (VybranyCitat is not { } vybrany || !Dialogy.Potvrd(Preklad.F($"Vymazať citát „{Skrat(vybrany.Text)}“?")))
        {
            return;
        }

        Db.VymazCitat(vybrany.Citat.Id);
        VybranyCitat = null;
        NacitajCitaty();
    }

    [RelayCommand]
    private void ObnovitZabudovane()
    {
        var pocet = Db.ObnovZabudovaneCitaty();
        NacitajCitaty();
        Dialogy.Info(Preklad.F($"Znova sa zobrazuje {pocet} skrytých alebo vymazaných zabudovaných citátov."));
    }

    private static string Skrat(string text) => text.Length > 60 ? text[..57] + "…" : text;

    // ------------------------------------------------------------------ výročia

    public IReadOnlyList<VolbaDruhuVyrocia> Druhy { get; } = Enum.GetValues<DruhVyrocia>()
        .Select(d => new VolbaDruhuVyrocia(d, CitatyVyrociaService.NazovDruhu(d))).ToList();

    [ObservableProperty] private DateTime? _den = DateTime.Today;
    [ObservableProperty] private bool _ajSkryteVyrocia;
    [ObservableProperty] private List<VyrocieRiadok> _vyrocia = new();
    [ObservableProperty] private string _stavDna = "";
    [ObservableProperty] private string _pocetStiahnutych = "";
    [ObservableProperty] private VyrocieRiadok? _vybraneVyrocie;
    [ObservableProperty] private bool _stahovatAutomaticky = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NestahujeSa))]
    private bool _stahujeSa;

    [ObservableProperty] private double _priebeh;
    [ObservableProperty] private string _priebehText = "";

    public bool NestahujeSa => !StahujeSa;

    // nové vlastné výročie
    [ObservableProperty] private string _novyRok = "";
    [ObservableProperty] private DruhVyrocia _novyDruh = DruhVyrocia.Udalost;
    [ObservableProperty] private string _novyText = "";
    [ObservableProperty] private bool _noveDomace;

    partial void OnDenChanged(DateTime? value) => NacitajVyrocia();
    partial void OnAjSkryteVyrociaChanged(bool value) => NacitajVyrocia();

    partial void OnStahovatAutomatickyChanged(bool value)
    {
        if (CitatyVyrociaService.StahovatAutomaticky != value)
        {
            CitatyVyrociaService.StahovatAutomaticky = value;
        }
    }

    private DateOnly VybranyDen => DateOnly.FromDateTime(Den ?? DateTime.Today);

    private void NacitajVyrocia()
    {
        try
        {
            var den = VybranyDen;
            Vyrocia = CitatyVyrociaService.Vyrocia(den, AjSkryteVyrocia);
            var stiahnute = Db.Stiahnute(CitatyVyrociaService.Jazyk, den.Month, den.Day);
            StavDna = stiahnute is { } cas
                ? Preklad.F($"Výročia dňa {Sk.DenAMesiac(den.ToDateTime(TimeOnly.MinValue))}: {Vyrocia.Count} · z Wikipédie stiahnuté {Sk.DatumCas(cas)}")
                : Preklad.F($"Výročia dňa {Sk.DenAMesiac(den.ToDateTime(TimeOnly.MinValue))}: {Vyrocia.Count} · z Wikipédie zatiaľ nestiahnuté");
            PocetStiahnutych = Preklad.F($"Z Wikipédie je stiahnutých {Db.PocetStiahnutychDni(CitatyVyrociaService.Jazyk)} z 366 dní roka.");
        }
        catch (Exception ex)
        {
            StavDna = ex.Message;
        }
    }

    [RelayCommand]
    private async Task StiahnutDen()
    {
        if (StahujeSa)
        {
            return;
        }

        StahujeSa = true;
        try
        {
            var den = VybranyDen;
            await CitatyVyrociaService.StiahniDenAsync(den.Month, den.Day);
            NacitajVyrocia();
            _hlavne?.Dashboard.AktualizujVyrocia();
        }
        catch (Exception ex)
        {
            Dialogy.Upozornenie(Preklad.F($"Výročia sa nepodarilo stiahnuť z Wikipédie: {ex.GetBaseException().Message}"));
        }
        finally
        {
            StahujeSa = false;
        }
    }

    /// <summary>Stiahne výročia celého roka (verejné kvôli automatickému testu).</summary>
    [RelayCommand]
    public async Task StiahnutRok()
    {
        if (StahujeSa)
        {
            return;
        }

        StahujeSa = true;
        _zrusenie = new CancellationTokenSource();
        Priebeh = 0;
        PriebehText = "";
        try
        {
            var priebeh = new Progress<(int Hotove, int Spolu)>(p =>
            {
                Priebeh = 100.0 * p.Hotove / p.Spolu;
                PriebehText = Preklad.F($"{p.Hotove} z {p.Spolu} dní");
            });
            var (dni, chyby) = await CitatyVyrociaService.StiahniRokAsync(false, priebeh, _zrusenie.Token);
            NacitajVyrocia();
            _hlavne?.Dashboard.AktualizujVyrocia();
            PriebehText = chyby == 0
                ? Preklad.F($"Hotovo – stiahnuté dni: {dni}.")
                : Preklad.F($"Hotovo – stiahnuté dni: {dni}, nepodarilo sa: {chyby} (skúste znova neskôr).");
        }
        catch (OperationCanceledException)
        {
            NacitajVyrocia();
            PriebehText = Preklad.T("Sťahovanie je zrušené. Stiahnuté dni zostávajú uložené.");
        }
        catch (Exception ex)
        {
            PriebehText = ex.GetBaseException().Message;
        }
        finally
        {
            StahujeSa = false;
            _zrusenie.Dispose();
            _zrusenie = null;
        }
    }

    [RelayCommand]
    private void ZrusitStahovanie() => _zrusenie?.Cancel();

    [RelayCommand]
    private void OtvoritWikipediu()
    {
        var den = VybranyDen;
        try
        {
            Process.Start(new ProcessStartInfo(VyrociaZWikipedie.AdresaStranky(CitatyVyrociaService.Jazyk, den.Month, den.Day)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Stránku sa nepodarilo otvoriť."), ex);
        }
    }

    [RelayCommand]
    private void PrepnutSkrytieVyrocia(VyrocieRiadok? riadok)
    {
        riadok ??= VybraneVyrocie;
        if (riadok is null)
        {
            return;
        }

        Db.NastavSkryteVyrocie(riadok.Vyrocie.Id, !riadok.Skryte);
        NacitajVyrocia();
        _hlavne?.Dashboard.AktualizujVyrocia();
    }

    [RelayCommand]
    private void VymazatVyrocie(VyrocieRiadok? riadok)
    {
        riadok ??= VybraneVyrocie;
        if (riadok is null || !Dialogy.Potvrd(Preklad.F($"Vymazať výročie „{Skrat(riadok.Text)}“?")))
        {
            return;
        }

        Db.VymazVyrocie(riadok.Vyrocie.Id);
        NacitajVyrocia();
        _hlavne?.Dashboard.AktualizujVyrocia();
    }

    [RelayCommand]
    private void PridatVyrocie()
    {
        if (string.IsNullOrWhiteSpace(NovyText))
        {
            Dialogy.Upozornenie(Preklad.T("Napíšte, čo sa v tento deň stalo alebo kto sa narodil či zomrel."));
            return;
        }

        int? rok = null;
        if (!string.IsNullOrWhiteSpace(NovyRok))
        {
            if (!int.TryParse(NovyRok.Trim(), out var r) || r < 1 || r > DateTime.Today.Year + 100)
            {
                Dialogy.Upozornenie(Preklad.T("Rok zadajte ako číslo, napríklad 1918 (alebo ho nechajte prázdny)."));
                return;
            }

            rok = r;
        }

        var den = VybranyDen;
        try
        {
            Db.PridajVyrocie(CitatyVyrociaService.Jazyk, den.Month, den.Day, rok, NovyDruh, NovyText, NoveDomace);
            NovyText = "";
            NovyRok = "";
            NacitajVyrocia();
            _hlavne?.Dashboard.AktualizujVyrocia();
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Výročie sa nepodarilo uložiť."), ex);
        }
    }

    [RelayCommand]
    private void PredchadzajuciDen() => Den = (Den ?? DateTime.Today).AddDays(-1);

    [RelayCommand]
    private void NasledujuciDen() => Den = (Den ?? DateTime.Today).AddDays(1);

    [RelayCommand]
    private void Dnes() => Den = DateTime.Today;

    // ------------------------------------------------------------------ export a import

    [ObservableProperty] private bool _exportLenVlastne;

    /// <summary>Pre automatický test: cesty bez okien Uložiť / Otvoriť.</summary>
    public string? CestaExportuBezDialogu { get; set; }

    public string? CestaImportuBezDialogu { get; set; }

    [RelayCommand]
    private void Exportovat()
    {
        var cesta = CestaExportuBezDialogu;
        if (cesta is null)
        {
            var dialog = new SaveFileDialog
            {
                Title = Preklad.T("Export citátov a výročí"),
                Filter = Preklad.T("Kalendárium (*.json)|*.json"),
                FileName = $"Kalendarium_citaty_vyrocia_{DateTime.Now:yyyy-MM-dd}.json",
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            cesta = dialog.FileName;
        }

        try
        {
            var (citaty, vyrocia) = Db.Exportuj(cesta, lenVlastne: ExportLenVlastne);
            Dialogy.Info(Preklad.F($"Uložené: {citaty} citátov a {vyrocia} výročí.\n\n{cesta}"));
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Export sa nepodaril."), ex);
        }
    }

    [RelayCommand]
    private void Importovat()
    {
        var cesta = CestaImportuBezDialogu;
        if (cesta is null)
        {
            var dialog = new OpenFileDialog
            {
                Title = Preklad.T("Import citátov a výročí"),
                Filter = Preklad.T("Kalendárium (*.json)|*.json"),
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            cesta = dialog.FileName;
        }

        try
        {
            var vysledok = Db.Importuj(cesta);
            Obnov();
            _hlavne?.Dashboard.Obnov();
            Dialogy.Info(Preklad.F($"Pribudlo {vysledok.Citaty} citátov a {vysledok.Vyrocia} výročí. Preskočené (už sú v Kalendáriu): {vysledok.Preskocene}."));
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Súbor sa nepodarilo načítať. Vyberte export citátov a výročí z Kalendária."), ex);
        }
    }
}
