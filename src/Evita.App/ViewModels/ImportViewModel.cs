using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;
using Microsoft.Win32;

namespace Evita.ViewModels;

/// <summary>Pole EviTA vo výbere pri priradení stĺpcov ("" = stĺpec sa nepoužije).</summary>
public sealed record PoleEviTA(string Kluc, string Nazov);

/// <summary>Číselník vo výbere v 1. kroku.</summary>
public sealed record VolbaCiselnika(TypCiselnika Typ, string Nazov);

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
    private ImportCiselnikaService? _importCiselnika;
    private HashSet<string> _existujuce = new();

    public ImportViewModel()
    {
        _polaEviTA = PolaPoloziek();
    }

    private static List<PoleEviTA> PolaPoloziek() =>
        new List<PoleEviTA> { new("", Preklad.T("(nepoužiť)")) }
            .Concat(StlpcePrenosu.Vsetky.Select(s => new PoleEviTA(s.Kluc, s.Povinny ? s.Nazov + " *" : s.Nazov)))
            .ToList();

    private static List<PoleEviTA> PolaCiselnikaPre(TypCiselnika typ) =>
        new List<PoleEviTA> { new("", Preklad.T("(nepoužiť)")) }
            .Concat(PolaCiselnika.Pre(typ).Select(p => new PoleEviTA(p.Kluc, p.Povinne ? p.Nazov + " *" : p.Nazov)))
            .ToList();

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

    /// <summary>Import číselníka (kategórie, miestnosti alebo osoby) namiesto položiek.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JeCiselnik), nameof(JePolozky))]
    private bool _druhCiselnik;

    // voľby druhu importu sa vylučujú (aj keď ich nastaví kód, nielen prepínač v okne)
    partial void OnDruhZlucenieChanged(bool value)
    {
        if (value)
        {
            DruhCela = false;
            DruhCiselnik = false;
        }
    }

    partial void OnDruhCelaChanged(bool value)
    {
        if (value)
        {
            DruhZlucenie = false;
            DruhCiselnik = false;
        }
    }

    partial void OnDruhCiselnikChanged(bool value)
    {
        if (value)
        {
            DruhZlucenie = false;
            DruhCela = false;
        }
    }

    public bool JeCiselnik => DruhCiselnik;
    public bool JePolozky => !DruhCiselnik;

    public IReadOnlyList<VolbaCiselnika> Ciselniky { get; } = new VolbaCiselnika[]
    {
        new(TypCiselnika.Kategorie, Preklad.T("Kategórie")),
        new(TypCiselnika.Miestnosti, Preklad.T("Miestnosti")),
        new(TypCiselnika.Osoby, Preklad.T("Osoby")),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NazovCiselnika))]
    private TypCiselnika _typCiselnika = TypCiselnika.Kategorie;

    public string NazovCiselnika => PolaCiselnika.NazovCiselnika(TypCiselnika);

    /// <summary>Číselník, pre ktorý sú pripravené riadky kontroly (podľa neho sa nastavia stĺpce tabuľky).</summary>
    [ObservableProperty] private TypCiselnika? _typRiadkovCiselnika;

    public IReadOnlyList<VzorovySubor> Vzory => VzoryService.Zoznam;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StiahnutyText))]
    private string _stiahnutyVzor = "";

    public string StiahnutyText => StiahnutyVzor.Length > 0 ? Preklad.F($"Uložené: {StiahnutyVzor}") : "";

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

    [ObservableProperty] private List<PoleEviTA> _polaEviTA;

    [ObservableProperty] private List<PriradenieStlpca> _priradenia = new();
    [ObservableProperty] private string _infoOSubore = "";

    // ------------------------------------------------------------------ krok 3

    public ObservableCollection<RiadokImportu> Riadky { get; } = new();

    public ObservableCollection<RiadokCiselnika> RiadkyCiselnika { get; } = new();

    [ObservableProperty] private ListCollectionView? _pohladRiadkov;
    [ObservableProperty] private ListCollectionView? _pohladCiselnika;
    [ObservableProperty] private bool _lenProblemove;
    [ObservableProperty] private string _suhrnKontroly = "";

    partial void OnLenProblemoveChanged(bool value)
    {
        PohladRiadkov?.Refresh();
        PohladCiselnika?.Refresh();
    }

    [RelayCommand]
    private void VybratVsetky()
    {
        if (JeCiselnik)
        {
            foreach (var r in RiadkyCiselnika.Where(r => r.MozeImportovat))
            {
                r.Importovat = true;
            }

            return;
        }

        foreach (var r in Riadky.Where(r => r.MozeImportovat))
        {
            r.Importovat = true;
        }
    }

    [RelayCommand]
    private void ZrusitVyber()
    {
        if (JeCiselnik)
        {
            foreach (var r in RiadkyCiselnika)
            {
                r.Importovat = false;
            }

            return;
        }

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

        if (JeCiselnik)
        {
            NacitajSuborCiselnika();
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
        PolaEviTA = PolaPoloziek();
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

    private void NacitajSuborCiselnika()
    {
        var typ = TypCiselnika;
        ZdrojImportu? zdroj = null;
        Dialogy.Cakaj(() => zdroj = CitacSuborov.Nacitaj(Cesta, typ));
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
        PolaEviTA = PolaCiselnikaPre(typ);
        var priradenie = PolaCiselnika.AutomatickePriradenie(typ, zdroj.Hlavicky);
        Priradenia = zdroj.Hlavicky.Select((h, i) => new PriradenieStlpca(h, zdroj.Ukazka(i), priradenie[i])).ToList();

        var info = new StringBuilder();
        info.Append(Preklad.F($"{zdroj.Format}, {zdroj.Riadky.Count} riadkov, {zdroj.Hlavicky.Count} stĺpcov"));
        if (zdroj.Balik is not null)
        {
            info.Append(Preklad.F($" – číselník {NazovCiselnika} zo súboru EviTA"));
        }

        info.Append('.');
        info.Append(Preklad.F($" Rozpoznaných stĺpcov: {priradenie.Count(k => k is not null)} z {zdroj.Hlavicky.Count}."));
        foreach (var upozornenie in zdroj.Upozornenia)
        {
            info.Append("\n• ").Append(upozornenie);
        }

        InfoOSubore = info.ToString();
        Krok = 2;
    }

    private void PripravKontroluCiselnika()
    {
        var typ = TypCiselnika;
        var kluce = Priradenia.Select(p => p.Kluc).ToHashSet();
        if (typ == TypCiselnika.Osoby
                ? !kluce.Contains(PolaCiselnika.Priezvisko) && !kluce.Contains(PolaCiselnika.CeleMeno)
                : !kluce.Contains(PolaCiselnika.Nazov))
        {
            Dialogy.Upozornenie(typ == TypCiselnika.Osoby
                ? Preklad.T("Priraďte stĺpec s priezviskom, alebo stĺpec s menom a priezviskom.")
                : Preklad.T("Priraďte stĺpec s názvom (pole Názov je povinné)."));
            return;
        }

        var priradenie = Priradenia.Select(p => p.Kluc.Length == 0 ? null : p.Kluc).ToList();
        var sluzba = new ImportCiselnikaService(typ);
        List<RiadokCiselnika>? riadky = null;
        Dialogy.Cakaj(() =>
        {
            riadky = sluzba.Priprav(_zdroj!, priradenie);
            _existujuce = sluzba.Existujuce();
            sluzba.SkontrolujVsetky(riadky, _existujuce);
        });

        if (riadky!.Count == 0)
        {
            Dialogy.Info(Preklad.T("V súbore sa nenašli žiadne riadky s údajmi."));
            return;
        }

        _importCiselnika = sluzba;
        foreach (var r in RiadkyCiselnika)
        {
            r.Zmeneny -= PriZmeneRiadkuCiselnika;
            r.ZmenenyVyber -= PriZmeneVyberuCiselnika;
        }

        RiadkyCiselnika.Clear();
        foreach (var r in riadky)
        {
            r.Zmeneny += PriZmeneRiadkuCiselnika;
            r.ZmenenyVyber += PriZmeneVyberuCiselnika;
            RiadkyCiselnika.Add(r);
        }

        TypRiadkovCiselnika = typ;
        PohladCiselnika = new ListCollectionView(RiadkyCiselnika)
        {
            Filter = o => !LenProblemove || o is RiadokCiselnika { Vysledok: not StavRiadku.Ok },
        };
        LenProblemove = false;
        PohladCiselnika.Refresh();
        AktualizujSuhrn();
        Krok = 3;
    }

    private void PriZmeneRiadkuCiselnika(RiadokCiselnika riadok)
    {
        if (_kontrolujem || _importCiselnika is null)
        {
            return;
        }

        _kontrolujem = true;
        try
        {
            _importCiselnika.SkontrolujVsetky(RiadkyCiselnika, _existujuce);
        }
        finally
        {
            _kontrolujem = false;
        }

        AktualizujSuhrn();
    }

    private void PriZmeneVyberuCiselnika(RiadokCiselnika riadok)
    {
        if (!_kontrolujem)
        {
            AktualizujSuhrn();
        }
    }

    private void PripravNahladCiselnika()
    {
        var vybrane = RiadkyCiselnika.Where(r => r.Importovat && r.MozeImportovat).ToList();
        if (vybrane.Count == 0)
        {
            Dialogy.Info(Preklad.T("Na import nie je vybraná žiadna položka. Opravte chybné riadky alebo zaškrtnite riadky, ktoré chcete importovať."));
            return;
        }

        var typ = _importCiselnika!.Typ;
        NahladPridat = Preklad.F($"Do číselníka {PolaCiselnika.NazovCiselnika(typ)} pribudne {ImportCiselnikaService.PocetZaznamov(vybrane.Count)}.");
        var preskocene = RiadkyCiselnika.Where(r => !(r.Importovat && r.MozeImportovat)).ToList();
        NahladPreskocit = preskocene.Count == 0
            ? Preklad.T("Nepreskočí sa žiadny riadok.")
            : Preklad.F($"Preskočí sa {preskocene.Count} riadkov:\n") + string.Join("\n", preskocene.Take(50).Select(r =>
                "• " + Preklad.F($"riadok {r.CisloRiadku}: {r.Popis(typ)} – {(r.Problem.Length > 0 ? r.Problem : Preklad.T("vynechané ručne"))}")))
              + (preskocene.Count > 50 ? "\n…" : "");
        NahladCiselniky = string.Join(", ", vybrane.Take(200).Select(r => r.Popis(typ))) + (vybrane.Count > 200 ? ", …" : "");
        Krok = 4;
    }

    private void PripravKontrolu()
    {
        if (_zdroj is null)
        {
            Krok = 1;
            return;
        }

        if (JeCiselnik)
        {
            PripravKontroluCiselnika();
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
        var stavy = JeCiselnik
            ? RiadkyCiselnika.Select(r => (r.Vysledok, Vybrany: r.Importovat && r.MozeImportovat)).ToList()
            : Riadky.Select(r => (r.Vysledok, Vybrany: r.Importovat && r.MozeImportovat)).ToList();
        int Pocet(StavRiadku stav) => stavy.Count(r => r.Vysledok == stav);
        var casti = new List<string> { Preklad.F($"{stavy.Count} riadkov"), Preklad.F($"v poriadku {Pocet(StavRiadku.Ok)}") };
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

        SuhrnKontroly = string.Join(" · ", casti) + Preklad.F($"  —  vybrané na import: {stavy.Count(r => r.Vybrany)}");
    }

    private void PripravNahlad()
    {
        if (JeCiselnik)
        {
            if (_importCiselnika is not null)
            {
                PripravNahladCiselnika();
            }

            return;
        }

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

        if (JeCiselnik)
        {
            ImportovatCiselnik();
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

    private void ImportovatCiselnik()
    {
        if (_importCiselnika is null || _zdroj is null)
        {
            return;
        }

        try
        {
            VysledokImportu? vysledok = null;
            Dialogy.Cakaj(() => vysledok = _importCiselnika.Importuj(_zdroj, RiadkyCiselnika));
            Vysledok = vysledok;
            ZmenyVDatach = true;
            Protokol = vysledok!.Protokol;
            VysledokText = Preklad.F($"Import je hotový. Do číselníka {PolaCiselnika.NazovCiselnika(_importCiselnika.Typ)} pribudlo {ImportCiselnikaService.PocetZaznamov(vysledok.Pridane)}, preskočených riadkov: {vysledok.Preskocene}.");
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
