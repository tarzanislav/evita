using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;
using Microsoft.Win32;

namespace Evita.ViewModels;

/// <summary>Pole EviTA vo výbere pri priradení stĺpcov ("" = stĺpec sa nepoužije).</summary>
public sealed record PoleEviTA(string Kluc, string Nazov);

public sealed partial class PriradenieStlpca : ObservableObject
{
    public PriradenieStlpca(string hlavicka, string ukazka, string? kluc)
    {
        Hlavicka = hlavicka;
        Ukazka = ukazka;
        _kluc = kluc ?? "";
    }

    public string Hlavicka { get; }
    public string Ukazka { get; }

    [ObservableProperty] private string _kluc;
}

/// <summary>Sprievodca importom: 1. súbor a druh, 2. priradenie stĺpcov, 3. kontrola, 4. náhľad, 5. výsledok.</summary>
public partial class ImportViewModel : ObservableObject
{
    private readonly ImportService _sluzba = new();
    private ZdrojImportu? _zdroj;
    private KontrolaImportu? _kontrola;
    private bool _kontrolujem;

    public ImportViewModel()
    {
        PolaEviTA = new List<PoleEviTA> { new("", Preklad.T("(nepoužiť)")) }
            .Concat(StlpcePrenosu.Vsetky.Select(s => new PoleEviTA(s.Kluc, s.Povinny ? s.Nazov + " *" : s.Nazov)))
            .ToList();
    }

    // ------------------------------------------------------------------ kroky

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JeKrok1), nameof(JeKrok2), nameof(JeKrok3), nameof(JeKrok4), nameof(JeKrok5),
        nameof(NadpisKroku), nameof(MozeSpat), nameof(MozeDalej), nameof(MozeImportovat))]
    private int _krok = 1;

    public bool JeKrok1 => Krok == 1;
    public bool JeKrok2 => Krok == 2;
    public bool JeKrok3 => Krok == 3;
    public bool JeKrok4 => Krok == 4;
    public bool JeKrok5 => Krok == 5;
    public bool MozeSpat => Krok is > 1 and < 5;
    public bool MozeDalej => Krok < 4;
    public bool MozeImportovat => Krok == 4;

    public string NadpisKroku => Krok switch
    {
        1 => Preklad.T("Krok 1 z 5 – súbor a druh importu"),
        2 => Preklad.T("Krok 2 z 5 – priradenie stĺpcov"),
        3 => Preklad.T("Krok 3 z 5 – kontrola údajov"),
        4 => Preklad.T("Krok 4 z 5 – náhľad výsledku"),
        _ => Preklad.T("Krok 5 z 5 – hotovo"),
    };

    /// <summary>Import zmenil dáta (hlavné okno má obnoviť zoznamy).</summary>
    public bool ZmenyVDatach { get; private set; }

    // ------------------------------------------------------------------ krok 1

    [ObservableProperty] private string _cesta = "";
    [ObservableProperty] private bool _druhZlucenie = true;
    [ObservableProperty] private bool _druhCela;

    public IReadOnlyList<VzorovySubor> Vzory => VzoryService.Zoznam;

    [ObservableProperty] private string _stiahnutyVzor = "";

    public DruhImportu Druh => DruhCela ? DruhImportu.CelaDatabaza : DruhImportu.Zlucenie;

    [RelayCommand]
    private void VybratSubor()
    {
        var dialog = new OpenFileDialog
        {
            Title = Preklad.T("Vyberte súbor na import"),
            Filter = CitacSuborov.FilterSuborov,
        };
        if (!string.IsNullOrEmpty(Cesta) && Path.GetDirectoryName(Cesta) is { } priecinok && Directory.Exists(priecinok))
        {
            dialog.InitialDirectory = priecinok;
        }

        if (dialog.ShowDialog() == true)
        {
            Cesta = dialog.FileName;
        }
    }

    [RelayCommand]
    private void StiahnutVzor(VzorovySubor? vzor)
    {
        if (vzor is null)
        {
            return;
        }

        try
        {
            string? cesta = null;
            Dialogy.Cakaj(() => cesta = VzoryService.Stiahni(vzor));
            StiahnutyVzor = cesta!;
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Vzorový súbor sa nepodarilo uložiť do priečinka Stiahnuté."), ex);
        }
    }

    [RelayCommand]
    private void ZobrazitStiahnuty()
    {
        if (StiahnutyVzor.Length > 0)
        {
            Tlac.OtvorPriecinok(Path.GetDirectoryName(StiahnutyVzor)!, StiahnutyVzor);
        }
    }

    /// <summary>Stiahnutý vzor rovno použije ako súbor na import.</summary>
    [RelayCommand]
    private void PouzitStiahnuty()
    {
        if (StiahnutyVzor.Length > 0)
        {
            Cesta = StiahnutyVzor;
        }
    }

    // ------------------------------------------------------------------ krok 2

    public List<PoleEviTA> PolaEviTA { get; }

    [ObservableProperty] private List<PriradenieStlpca> _priradenia = new();
    [ObservableProperty] private string _infoOSubore = "";

    // ------------------------------------------------------------------ krok 3

    public ObservableCollection<RiadokImportu> Riadky { get; } = new();

    [ObservableProperty] private ListCollectionView? _pohladRiadkov;
    [ObservableProperty] private bool _lenProblemove;
    [ObservableProperty] private string _suhrnKontroly = "";

    partial void OnLenProblemoveChanged(bool value) => PohladRiadkov?.Refresh();

    [RelayCommand]
    private void VybratVsetky()
    {
        foreach (var r in Riadky.Where(r => r.MozeImportovat))
        {
            r.Importovat = true;
        }
    }

    [RelayCommand]
    private void ZrusitVyber()
    {
        foreach (var r in Riadky)
        {
            r.Importovat = false;
        }
    }

    // ------------------------------------------------------------------ krok 4

    [ObservableProperty] private string _nahladPridat = "";
    [ObservableProperty] private string _nahladPreskocit = "";
    [ObservableProperty] private string _nahladCiselniky = "";

    // ------------------------------------------------------------------ krok 5

    [ObservableProperty] private string _protokol = "";
    [ObservableProperty] private string _vysledokText = "";
    [ObservableProperty] private bool _importVrateny;

    public VysledokImportu? Vysledok { get; private set; }

    public event Action? Zatvorit;

    // ------------------------------------------------------------------ navigácia

    [RelayCommand]
    private void Spat()
    {
        if (MozeSpat)
        {
            Krok--;
        }
    }

    [RelayCommand]
    private void Dalej()
    {
        try
        {
            switch (Krok)
            {
                case 1:
                    NacitajSubor();
                    break;
                case 2:
                    PripravKontrolu();
                    break;
                case 3:
                    PripravNahlad();
                    break;
            }
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Pri spracovaní súboru nastala chyba."), ex);
        }
    }

    private void NacitajSubor()
    {
        if (string.IsNullOrWhiteSpace(Cesta))
        {
            Dialogy.Info(Preklad.T("Vyberte súbor, ktorý chcete importovať."));
            return;
        }

        if (Druh == DruhImportu.CelaDatabaza)
        {
            using var db = Databaza.Otvor();
            if (db.Polozky.Any())
            {
                Dialogy.Upozornenie(
                    Preklad.T("Prenos celej databázy sa dá urobiť len do prázdnej evidencie, aby sa zachovali pôvodné prírastkové čísla.\n\nAk chcete evidenciu nahradiť, najprv vymažte dáta (Servis → Vymazať dáta a začať nanovo). Ak chcete položky len pridať k existujúcim, zvoľte Doplniť položky do evidencie."));
                return;
            }
        }

        ZdrojImportu? zdroj = null;
        Dialogy.Cakaj(() => zdroj = _sluzba.Nacitaj(Cesta));
        if (zdroj is null)
        {
            return;
        }

        if (zdroj.Riadky.Count == 0)
        {
            Dialogy.Info(Preklad.T("V súbore sa nenašli žiadne riadky s údajmi."));
            return;
        }

        _zdroj = zdroj;
        var priradenie = ImportService.AutomatickePriradenie(zdroj.Hlavicky);
        Priradenia = zdroj.Hlavicky.Select((h, i) => new PriradenieStlpca(h, zdroj.Ukazka(i), priradenie[i])).ToList();

        var info = new StringBuilder();
        info.Append(Preklad.F($"{zdroj.Format}, {zdroj.Riadky.Count} riadkov, {zdroj.Hlavicky.Count} stĺpcov"));
        if (zdroj.Balik is not null)
        {
            info.Append(Preklad.T(" – export z programu EviTA, stĺpce sú priradené automaticky"));
        }

        info.Append('.');
        var rozpoznane = priradenie.Count(k => k is not null);
        info.Append(Preklad.F($" Rozpoznaných stĺpcov: {rozpoznane} z {zdroj.Hlavicky.Count}."));
        foreach (var upozornenie in zdroj.Upozornenia)
        {
            info.Append("\n• ").Append(upozornenie);
        }

        InfoOSubore = info.ToString();
        Krok = 2;
    }

    private void PripravKontrolu()
    {
        if (_zdroj is null)
        {
            Krok = 1;
            return;
        }

        if (!Priradenia.Any(p => p.Kluc == StlpcePrenosu.Nazov))
        {
            Dialogy.Upozornenie(Preklad.T("Priraďte stĺpec, v ktorom je názov položky (pole Názov je povinné)."));
            return;
        }

        var priradenie = Priradenia.Select(p => p.Kluc.Length == 0 ? null : p.Kluc).ToList();
        List<RiadokImportu>? riadky = null;
        Dialogy.Cakaj(() =>
        {
            riadky = ImportService.Priprav(_zdroj, priradenie);
            _kontrola = new KontrolaImportu(Druh, _zdroj);
            _kontrola.SkontrolujVsetky(riadky);
        });

        foreach (var r in Riadky)
        {
            r.Zmeneny -= PriZmeneRiadku;
            r.ZmenenyVyber -= PriZmeneVyberu;
        }

        Riadky.Clear();
        foreach (var r in riadky!)
        {
            r.Zmeneny += PriZmeneRiadku;
            r.ZmenenyVyber += PriZmeneVyberu;
            Riadky.Add(r);
        }

        PohladRiadkov = new ListCollectionView(Riadky)
        {
            Filter = o => !LenProblemove || o is RiadokImportu { Vysledok: not StavRiadku.Ok },
        };
        LenProblemove = Riadky.Any(r => r.Vysledok != StavRiadku.Ok) && Riadky.Count > 50;
        PohladRiadkov.Refresh();
        AktualizujSuhrn();
        Krok = 3;
    }

    private void PriZmeneRiadku(RiadokImportu riadok)
    {
        if (_kontrolujem || _kontrola is null)
        {
            return;
        }

        _kontrolujem = true;
        try
        {
            _kontrola.SkontrolujVsetky(Riadky);
        }
        finally
        {
            _kontrolujem = false;
        }

        AktualizujSuhrn();
    }

    private void PriZmeneVyberu(RiadokImportu riadok)
    {
        if (!_kontrolujem)
        {
            AktualizujSuhrn();
        }
    }

    /// <summary>Znova skontroluje všetky riadky (po hromadnej oprave, napr. v automatickom teste).</summary>
    public void SkontrolujZnova()
    {
        if (_kontrola is null)
        {
            return;
        }

        _kontrolujem = true;
        try
        {
            _kontrola.SkontrolujVsetky(Riadky);
        }
        finally
        {
            _kontrolujem = false;
        }

        AktualizujSuhrn();
    }

    private void AktualizujSuhrn()
    {
        int Pocet(StavRiadku stav) => Riadky.Count(r => r.Vysledok == stav);
        var casti = new List<string> { Preklad.F($"{Riadky.Count} riadkov"), Preklad.F($"v poriadku {Pocet(StavRiadku.Ok)}") };
        if (Pocet(StavRiadku.Upozornenie) is > 0 and var u)
        {
            casti.Add(Preklad.F($"s upozornením {u}"));
        }

        if (Pocet(StavRiadku.MoznaDuplicita) is > 0 and var m)
        {
            casti.Add(Preklad.F($"možné duplicity {m}"));
        }

        if (Pocet(StavRiadku.Duplicita) is > 0 and var d)
        {
            casti.Add(Preklad.F($"už v evidencii {d}"));
        }

        if (Pocet(StavRiadku.Chyba) is > 0 and var ch)
        {
            casti.Add(Preklad.F($"s chybou {ch}"));
        }

        SuhrnKontroly = string.Join(" · ", casti) + Preklad.F($"  —  vybrané na import: {Riadky.Count(r => r.Importovat && r.MozeImportovat)}");
    }

    private void PripravNahlad()
    {
        if (_kontrola is null)
        {
            return;
        }

        var nahlad = _kontrola.Nahlad(Riadky);
        if (nahlad.Pridat == 0)
        {
            Dialogy.Info(Preklad.T("Na import nie je vybraná žiadna položka. Opravte chybné riadky alebo zaškrtnite riadky, ktoré chcete importovať."));
            return;
        }

        NahladPridat = Preklad.F($"Pribudne {ExportService.PocetPoloziek(nahlad.Pridat)}") +
                       (nahlad.CisloOd is { } od && nahlad.CisloDo is { } po
                           ? Druh == DruhImportu.Zlucenie
                               ? Preklad.F($" s prírastkovými číslami {od} – {po}.")
                               : Preklad.F($" s pôvodnými prírastkovými číslami {od} – {po}.")
                           : ".");

        NahladPreskocit = nahlad.Preskocit == 0
            ? Preklad.T("Nepreskočí sa žiadny riadok.")
            : Preklad.F($"Preskočí sa {nahlad.Preskocit} riadkov:\n") + string.Join("\n", nahlad.Dovody.Select(d => "• " + d));

        var ciselniky = new List<string>();
        if (nahlad.NoveKategorie.Count > 0)
        {
            ciselniky.Add(Preklad.F($"Nové kategórie ({nahlad.NoveKategorie.Count}): {string.Join(", ", nahlad.NoveKategorie)}"));
        }

        if (nahlad.NoveMiestnosti.Count > 0)
        {
            ciselniky.Add(Preklad.F($"Nové miestnosti ({nahlad.NoveMiestnosti.Count}): {string.Join(", ", nahlad.NoveMiestnosti)}"));
        }

        if (nahlad.NoveOsoby.Count > 0)
        {
            ciselniky.Add(Preklad.F($"Nové osoby ({nahlad.NoveOsoby.Count}): {string.Join(", ", nahlad.NoveOsoby)}"));
        }

        NahladCiselniky = ciselniky.Count == 0
            ? Preklad.T("Všetky kategórie, miestnosti a osoby už v evidencii sú.")
            : string.Join("\n", ciselniky);
        Krok = 4;
    }

    [RelayCommand]
    private void Importovat()
    {
        if (_zdroj is null || Krok != 4)
        {
            return;
        }

        try
        {
            VysledokImportu? vysledok = null;
            Dialogy.Cakaj(() => vysledok = _sluzba.Importuj(_zdroj, Riadky, Druh));
            Vysledok = vysledok;
            ZmenyVDatach = true;
            Protokol = vysledok!.Protokol;
            VysledokText = Preklad.F($"Import je hotový. Pribudlo {ExportService.PocetPoloziek(vysledok.Pridane)}") +
                           (vysledok.CisloOd is { } od ? Preklad.F($" (prírastkové čísla {od} – {vysledok.CisloDo})") : "") +
                           Preklad.F($", preskočených riadkov: {vysledok.Preskocene}.");
            Krok = 5;
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Import sa nepodaril. Databáza zostala bez zmeny."), ex);
        }
    }

    // ------------------------------------------------------------------ krok 5

    [RelayCommand]
    private void UlozitProtokol()
    {
        var dialog = new SaveFileDialog
        {
            Title = Preklad.T("Uložiť protokol importu"),
            Filter = Preklad.T("Textový súbor (*.txt)|*.txt"),
            FileName = $"EviTA_protokol_importu_{DateTime.Now:yyyy-MM-dd_HHmm}.txt",
        };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, Protokol, new UTF8Encoding(true));
        }
    }

    [RelayCommand]
    private void VytlacitProtokol() => Tlac.Text(Preklad.T("EviTA – protokol importu"), Protokol);

    [RelayCommand]
    private void VratitImport()
    {
        if (ImportVrateny)
        {
            return;
        }

        if (!Dialogy.Potvrd(Preklad.T("Vrátiť evidenciu do stavu pred týmto importom?\n\nDatabáza sa obnoví zo zálohy, ktorá sa urobila tesne pred importom.")))
        {
            return;
        }

        try
        {
            Dialogy.Cakaj(_sluzba.VratPoslednyImport);
            ImportVrateny = true;
            VysledokText = Preklad.T("Import je vrátený – evidencia je v stave pred importom.");
            Dialogy.Info(Preklad.T("Import je vrátený. Evidencia je v stave pred importom."));
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Import sa nepodarilo vrátiť."), ex);
        }
    }

    [RelayCommand]
    private void Zavriet() => Zatvorit?.Invoke();
}
