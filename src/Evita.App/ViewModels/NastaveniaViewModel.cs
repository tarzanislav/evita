using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Pomocne;
using Evita.Services;
using Microsoft.Win32;

namespace Evita.ViewModels;

/// <summary>Mena v kurzovom lístku (zaškrtávacie políčko v nastaveniach).</summary>
public partial class VolbaMeny : ObservableObject
{
    public VolbaMeny(string kod, bool zvolena)
    {
        Kod = kod;
        _zvolena = zvolena;
    }

    public string Kod { get; }
    public string Nazov => KurzyService.NazovMeny(Kod);

    [ObservableProperty] private bool _zvolena;

    public event Action? Zmenena;

    partial void OnZvolenaChanged(bool value) => Zmenena?.Invoke();
}

/// <summary>Časť Nastavenia: jazyk, počasie, kurzový lístok a export/import konfigurácie.</summary>
public partial class NastaveniaViewModel : ObservableObject, IObnovitelny
{
    private readonly MainViewModel _hlavne;
    private readonly PocasieService _pocasie = new();
    private readonly KurzyService _kurzy = new();
    private bool _nacitavam;

    public NastaveniaViewModel(MainViewModel hlavne)
    {
        _hlavne = hlavne;
    }

    public IReadOnlyList<Jazyk> Jazyky => Preklad.Jazyky;

    [ObservableProperty] private Jazyk _vybranyJazyk = Preklad.Aktualny;
    [ObservableProperty] private string _aktualneMiesto = "";
    [ObservableProperty] private bool _jeMiesto;
    [ObservableProperty] private string _hladaneMiesto = "";
    [ObservableProperty] private List<MiestoPocasia> _najdeneMiesta = new();
    [ObservableProperty] private MiestoPocasia? _vybraneMiesto;
    [ObservableProperty] private string _stavHladania = "";
    [ObservableProperty] private bool _hladam;
    [ObservableProperty] private List<VolbaMeny> _meny = new();
    [ObservableProperty] private string _menyText = "";
    [ObservableProperty] private string _konfiguraciaText = "";

    public string AktualnyJazykText => Preklad.Aktualny.Nazov;

    public void Obnov()
    {
        _nacitavam = true;
        try
        {
            VybranyJazyk = Preklad.Aktualny;
            var miesto = _pocasie.NacitajMiesto();
            JeMiesto = miesto is not null;
            AktualneMiesto = miesto is null ? Preklad.T("Miesto nie je nastavené.") : $"{miesto.Popis} ({miesto.Suradnice})";

            var zvolene = _kurzy.ZvoleneMeny();
            var meny = zvolene.Concat(KurzyService.VsetkyMeny.Except(zvolene))
                .Select(k => new VolbaMeny(k, zvolene.Contains(k)))
                .ToList();
            foreach (var m in meny)
            {
                m.Zmenena += UlozMeny;
            }

            Meny = meny;
            AktualizujMenyText();
        }
        finally
        {
            _nacitavam = false;
        }
    }

    // ------------------------------------------------------------------ jazyk

    /// <summary>Pre automatický test: nereštartovať program po zmene jazyka.</summary>
    public bool BezRestartu { get; set; }

    [RelayCommand]
    private void PouzitJazyk()
    {
        if (VybranyJazyk is null || VybranyJazyk.Kod == Preklad.Kod)
        {
            return;
        }

        ZmenJazyk(VybranyJazyk.Kod);
    }

    /// <summary>Language reset – vráti predvolený jazyk (angličtinu).</summary>
    [RelayCommand]
    private void ResetJazyka()
    {
        VybranyJazyk = Preklad.Najdi(Preklad.Predvoleny);
        if (Preklad.Kod == Preklad.Predvoleny)
        {
            Preklad.UlozJazyk(Preklad.Predvoleny);
            Dialogy.Info(Preklad.T("Program už je v predvolenom jazyku (English)."));
            return;
        }

        ZmenJazyk(Preklad.Predvoleny);
    }

    private void ZmenJazyk(string kod)
    {
        var povodny = Preklad.Kod;
        Preklad.UlozJazyk(kod);
        if (BezRestartu)
        {
            return;
        }

        // otázka už v novom jazyku
        Preklad.Nastav(kod);
        if (Dialogy.Potvrd(Preklad.T("Jazyk programu sa zmení po reštarte. Reštartovať program teraz?")))
        {
            Restartuj();
            return;
        }

        Preklad.Nastav(povodny);
        Dialogy.Info(Preklad.T("Nový jazyk sa použije pri najbližšom spustení programu."));
    }

    /// <summary>Spustí program znova a tento zatvorí.</summary>
    public static void Restartuj()
    {
        try
        {
            if (Environment.ProcessPath is { } cesta)
            {
                Process.Start(new ProcessStartInfo(cesta, "--restart") { UseShellExecute = false });
            }
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Program sa nepodarilo reštartovať. Zatvorte ho a spustite znova."), ex);
            return;
        }

        Application.Current.MainWindow?.Close();
        Application.Current.Shutdown();
    }

    // ------------------------------------------------------------------ počasie

    [RelayCommand]
    private async Task HladatMiesto()
    {
        if (Hladam)
        {
            return;
        }

        Hladam = true;
        StavHladania = Preklad.T("Hľadám…");
        try
        {
            NajdeneMiesta = await _pocasie.HladajAsync(HladaneMiesto);
            VybraneMiesto = NajdeneMiesta.FirstOrDefault();
            StavHladania = NajdeneMiesta.Count == 0
                ? Preklad.T("Nič sa nenašlo. Skúste iný názov (napr. bez skratiek) alebo väčšie mesto v okolí.")
                : Preklad.T("Vyberte miesto v zozname a kliknite na Použiť.");
        }
        catch (EvitaException ex)
        {
            StavHladania = ex.Message;
        }
        catch (Exception ex)
        {
            StavHladania = ex.GetBaseException().Message;
        }
        finally
        {
            Hladam = false;
        }
    }

    [RelayCommand]
    private void PouzitMiesto()
    {
        if (VybraneMiesto is not { } miesto)
        {
            return;
        }

        _pocasie.UlozMiesto(miesto);
        NajdeneMiesta = new List<MiestoPocasia>();
        StavHladania = "";
        HladaneMiesto = "";
        Obnov();
        _hlavne.Dashboard.ObnovPocasie();
    }

    [RelayCommand]
    private void ZrusitMiesto()
    {
        _pocasie.UlozMiesto(null);
        Obnov();
        _hlavne.Dashboard.ObnovPocasie();
    }

    // ------------------------------------------------------------------ kurzy

    private void UlozMeny()
    {
        if (_nacitavam)
        {
            return;
        }

        _kurzy.UlozMeny(Meny.Where(m => m.Zvolena).Select(m => m.Kod));
        AktualizujMenyText();
        _hlavne.Dashboard.ObnovKurzy(stiahnut: false);
    }

    private void AktualizujMenyText()
    {
        var zvolene = Meny.Where(m => m.Zvolena).Select(m => m.Kod).ToList();
        MenyText = zvolene.Count == 0
            ? Preklad.T("Nie je zvolená žiadna mena – kurzový lístok na dashboarde bude prázdny.")
            : Preklad.F($"Na dashboarde: {string.Join(", ", zvolene)}");
    }

    [RelayCommand]
    private void PredvoleneMeny()
    {
        _kurzy.UlozMeny(KurzyService.PredvoleneMeny.Split(','));
        Obnov();
        _hlavne.Dashboard.ObnovKurzy(stiahnut: false);
    }

    // ------------------------------------------------------------------ konfigurácia

    /// <summary>Pre automatický test: cesty bez okien Uložiť / Otvoriť.</summary>
    public string? CestaExportuBezDialogu { get; set; }

    public string? CestaImportuBezDialogu { get; set; }

    [RelayCommand]
    private void ExportovatKonfiguraciu()
    {
        var cesta = CestaExportuBezDialogu;
        if (cesta is null)
        {
            var dialog = new SaveFileDialog
            {
                Title = Preklad.T("Export konfigurácie programu"),
                Filter = Preklad.T("Konfigurácia EviTA (*.json)|*.json"),
                FileName = KonfiguraciaService.NazovSuboru(),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            cesta = dialog.FileName;
        }

        try
        {
            var pocet = KonfiguraciaService.Exportuj(cesta);
            KonfiguraciaText = Preklad.F($"Konfigurácia je uložená ({pocet} nastavení): {cesta}");
            if (CestaExportuBezDialogu is null)
            {
                Dialogy.Info(Preklad.F($"Konfigurácia programu je uložená do súboru\n\n{cesta}\n\nEvidencia (položky, kalendár, úlohy, poznámky) v nej nie je."));
            }
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Konfiguráciu sa nepodarilo uložiť."), ex);
        }
    }

    [RelayCommand]
    private void ImportovatKonfiguraciu()
    {
        var cesta = CestaImportuBezDialogu;
        if (cesta is null)
        {
            var dialog = new OpenFileDialog
            {
                Title = Preklad.T("Import konfigurácie programu"),
                Filter = Preklad.T("Konfigurácia EviTA (*.json)|*.json|Všetky súbory (*.*)|*.*"),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            cesta = dialog.FileName;
        }

        try
        {
            var konfiguracia = KonfiguraciaService.Precitaj(cesta);
            var obsah = KonfiguraciaService.Obsah(konfiguracia);
            if (obsah.Count == 0)
            {
                Dialogy.Upozornenie(Preklad.T("Súbor neobsahuje žiadne nastavenia, ktoré by sa dali načítať."));
                return;
            }

            var odkial = string.Join(", ", new[] { konfiguracia.Program, konfiguracia.Pocitac, konfiguracia.Vytvorene }.Where(x => !string.IsNullOrEmpty(x)));
            if (!Dialogy.Potvrd(Preklad.F($"Načítať konfiguráciu zo súboru {Path.GetFileName(cesta)} ({odkial})?\n\nPrepíšu sa tieto nastavenia:\n• {string.Join("\n• ", obsah)}\n\nEvidencia (položky, kalendár, úlohy, poznámky) sa nezmení.")))
            {
                return;
            }

            var vysledok = KonfiguraciaService.Importuj(konfiguracia);
            KonfiguraciaText = Preklad.F($"Konfigurácia je načítaná ({vysledok.Nastavenia} nastavení, {vysledok.TypyUdalosti} typov udalostí, {vysledok.Kategorie} kategórií s ikonami).");
            Obnov();
            _hlavne.PoZmeneKonfiguracie();

            var sprava = Preklad.T("Konfigurácia je načítaná.");
            if (vysledok.Poznamky.Count > 0)
            {
                sprava += "\n\n" + string.Join("\n\n", vysledok.Poznamky);
            }

            if (vysledok.ZmenaJazyka && !BezRestartu)
            {
                var povodny = Preklad.Kod;
                Preklad.Nastav(Preklad.JazykZDatabazy());
                if (Dialogy.Potvrd(sprava + "\n\n" + Preklad.T("Jazyk programu sa zmení po reštarte. Reštartovať program teraz?")))
                {
                    Restartuj();
                    return;
                }

                Preklad.Nastav(povodny);
                return;
            }

            if (CestaImportuBezDialogu is null)
            {
                Dialogy.Info(sprava);
            }
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Konfiguráciu sa nepodarilo načítať."), ex);
        }
    }

    // ------------------------------------------------------------------ odkazy na ďalšie nastavenia

    [RelayCommand]
    private void NastaveniaKalendara() => new Views.NastaveniaKalendaraWindow { Owner = Dialogy.Vlastnik }.ShowDialog();

    [RelayCommand]
    private void NastavenieEmailu() => new Views.EmailNastaveniaWindow { Owner = Dialogy.Vlastnik }.ShowDialog();

    [RelayCommand]
    private void OtvorSekciu(string? sekcia)
    {
        if (sekcia is not null)
        {
            _hlavne.PrejdiNaSekciu(sekcia);
        }
    }
}
