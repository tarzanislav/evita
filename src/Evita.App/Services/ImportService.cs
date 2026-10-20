using System.ComponentModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Evita.Data;
using Evita.Models;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

public enum DruhImportu
{
    /// <summary>Doplní nové položky do existujúcej evidencie, dostanú nové prírastkové čísla.</summary>
    Zlucenie,

    /// <summary>Prenesie celú evidenciu do prázdnej databázy so zachovaním prírastkových čísel a histórie.</summary>
    CelaDatabaza,
}

public enum StavRiadku
{
    Ok,
    Upozornenie,
    MoznaDuplicita,
    Duplicita,
    Chyba,
}

/// <summary>Jeden riadok importovaného súboru. Hodnoty sa dajú v sprievodcovi opraviť, riadok sa potom znova skontroluje.</summary>
public sealed partial class RiadokImportu : ObservableObject
{
    private static readonly HashSet<string> Systemove = new()
    {
        nameof(Importovat), nameof(Vysledok), nameof(Problem), nameof(VysledokText), nameof(MozeImportovat),
    };

    public RiadokImportu(int cisloRiadku)
    {
        CisloRiadku = cisloRiadku;
    }

    /// <summary>Číslo riadku v pôvodnom súbore.</summary>
    public int CisloRiadku { get; }

    [ObservableProperty] private bool _importovat = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VysledokText), nameof(MozeImportovat))]
    private StavRiadku _vysledok;

    [ObservableProperty] private string _problem = "";

    [ObservableProperty] private string? _cislo;
    [ObservableProperty] private string? _nazov;
    [ObservableProperty] private string? _kategoria;
    [ObservableProperty] private string? _miestnost;
    [ObservableProperty] private string? _pridelene;
    [ObservableProperty] private string? _vyrobca;
    [ObservableProperty] private string? _model;
    [ObservableProperty] private string? _seriove;
    [ObservableProperty] private string? _inventarne;
    [ObservableProperty] private string? _stav;
    [ObservableProperty] private string? _datumNakupu;
    [ObservableProperty] private string? _cena;
    [ObservableProperty] private string? _zarukaDo;
    [ObservableProperty] private string? _dalsiaRevizia;
    [ObservableProperty] private string? _datumPridania;
    [ObservableProperty] private string? _datumZmeny;
    [ObservableProperty] private string? _datumVyradenia;
    [ObservableProperty] private string? _dovodVyradenia;
    [ObservableProperty] private string? _poznamka;
    [ObservableProperty] private string? _identifikator;

    /// <summary>Riadok už prešiel kontrolou (prvá kontrola nastaví predvolený výber).</summary>
    internal bool Skontrolovany { get; set; }

    public bool MozeImportovat => Vysledok is not (StavRiadku.Chyba or StavRiadku.Duplicita);

    public string VysledokText => Vysledok switch
    {
        StavRiadku.Ok => Preklad.T("v poriadku"),
        StavRiadku.Upozornenie => Preklad.T("upozornenie"),
        StavRiadku.MoznaDuplicita => Preklad.T("možná duplicita"),
        StavRiadku.Duplicita => Preklad.T("už je v evidencii"),
        _ => Preklad.T("chyba"),
    };

    /// <summary>Hodnota v riadku sa zmenila – treba ho znova skontrolovať.</summary>
    public event Action<RiadokImportu>? Zmeneny;

    /// <summary>Zmenil sa výber riadku na import.</summary>
    public event Action<RiadokImportu>? ZmenenyVyber;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Importovat))
        {
            ZmenenyVyber?.Invoke(this);
        }
        else if (e.PropertyName is { } nazov && !Systemove.Contains(nazov))
        {
            Zmeneny?.Invoke(this);
        }
    }

    public string? Hodnota(string kluc) => kluc switch
    {
        StlpcePrenosu.Cislo => Cislo,
        StlpcePrenosu.Nazov => Nazov,
        StlpcePrenosu.Kategoria => Kategoria,
        StlpcePrenosu.Miestnost => Miestnost,
        StlpcePrenosu.Pridelene => Pridelene,
        StlpcePrenosu.Vyrobca => Vyrobca,
        StlpcePrenosu.Model => Model,
        StlpcePrenosu.Seriove => Seriove,
        StlpcePrenosu.Inventarne => Inventarne,
        StlpcePrenosu.Stav => Stav,
        StlpcePrenosu.DatumNakupu => DatumNakupu,
        StlpcePrenosu.Cena => Cena,
        StlpcePrenosu.ZarukaDo => ZarukaDo,
        StlpcePrenosu.DalsiaRevizia => DalsiaRevizia,
        StlpcePrenosu.DatumPridania => DatumPridania,
        StlpcePrenosu.DatumZmeny => DatumZmeny,
        StlpcePrenosu.DatumVyradenia => DatumVyradenia,
        StlpcePrenosu.DovodVyradenia => DovodVyradenia,
        StlpcePrenosu.Poznamka => Poznamka,
        StlpcePrenosu.Identifikator => Identifikator,
        _ => null,
    };

    public void Nastav(string kluc, string? hodnota)
    {
        switch (kluc)
        {
            case StlpcePrenosu.Cislo: Cislo = hodnota; break;
            case StlpcePrenosu.Nazov: Nazov = hodnota; break;
            case StlpcePrenosu.Kategoria: Kategoria = hodnota; break;
            case StlpcePrenosu.Miestnost: Miestnost = hodnota; break;
            case StlpcePrenosu.Pridelene: Pridelene = hodnota; break;
            case StlpcePrenosu.Vyrobca: Vyrobca = hodnota; break;
            case StlpcePrenosu.Model: Model = hodnota; break;
            case StlpcePrenosu.Seriove: Seriove = hodnota; break;
            case StlpcePrenosu.Inventarne: Inventarne = hodnota; break;
            case StlpcePrenosu.Stav: Stav = hodnota; break;
            case StlpcePrenosu.DatumNakupu: DatumNakupu = hodnota; break;
            case StlpcePrenosu.Cena: Cena = hodnota; break;
            case StlpcePrenosu.ZarukaDo: ZarukaDo = hodnota; break;
            case StlpcePrenosu.DalsiaRevizia: DalsiaRevizia = hodnota; break;
            case StlpcePrenosu.DatumPridania: DatumPridania = hodnota; break;
            case StlpcePrenosu.DatumZmeny: DatumZmeny = hodnota; break;
            case StlpcePrenosu.DatumVyradenia: DatumVyradenia = hodnota; break;
            case StlpcePrenosu.DovodVyradenia: DovodVyradenia = hodnota; break;
            case StlpcePrenosu.Poznamka: Poznamka = hodnota; break;
            case StlpcePrenosu.Identifikator: Identifikator = hodnota; break;
        }
    }
}

/// <summary>Zhrnutie pred importom: čo pribudne a čo sa preskočí.</summary>
public sealed record NahladImportu(
    int Pridat,
    int Preskocit,
    IReadOnlyList<string> Dovody,
    IReadOnlyList<string> NoveKategorie,
    IReadOnlyList<string> NoveMiestnosti,
    IReadOnlyList<string> NoveOsoby,
    int? CisloOd,
    int? CisloDo);

public sealed record VysledokImportu(int Pridane, int Preskocene, string Zaloha, string Protokol, int? CisloOd, int? CisloDo);

/// <summary>
/// Kontrola riadkov pred importom: povinné údaje, dátumy, sumy, stav a duplicity
/// (podľa identifikátora GUID, potom sériového čísla, potom zhody názvu, kategórie a miestnosti).
/// </summary>
public sealed class KontrolaImportu
{
    public const string Nezaradene = "nezaradené";

    private readonly ZdrojImportu _zdroj;
    private readonly HashSet<Guid> _guidy = new();
    private readonly HashSet<string> _seriove = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _kluce = new(StringComparer.Ordinal);
    private readonly HashSet<string> _kategorie = new(Sk.Porovnanie);
    private readonly HashSet<string> _miestnosti = new(Sk.Porovnanie);
    private readonly HashSet<string> _osoby = new(Sk.Porovnanie);

    public KontrolaImportu(DruhImportu druh, ZdrojImportu zdroj)
    {
        Druh = druh;
        _zdroj = zdroj;
        using var db = Databaza.Otvor();
        foreach (var p in db.Polozky.AsNoTracking()
                     .Select(p => new { p.Guid, p.SerioveCislo, p.Nazov, p.PrirastkoveCislo, Kategoria = p.Kategoria!.Nazov, Miestnost = p.Miestnost != null ? p.Miestnost.Nazov : null })
                     .ToList())
        {
            _guidy.Add(p.Guid);
            if (NormalizujSeriove(p.SerioveCislo) is { } s)
            {
                _seriove.Add(s);
            }

            _kluce.TryAdd(Kluc(p.Nazov, p.Kategoria, p.Miestnost), p.PrirastkoveCislo);
        }

        _kategorie.UnionWith(db.Kategorie.Select(k => k.Nazov).ToList());
        _miestnosti.UnionWith(db.Miestnosti.Select(m => m.Nazov).ToList());
        foreach (var o in db.Osoby.Select(o => new { o.Meno, o.Priezvisko }).ToList())
        {
            _osoby.Add($"{o.Meno} {o.Priezvisko}".Trim());
            _osoby.Add($"{o.Priezvisko} {o.Meno}".Trim());
        }

        PocetVDatabaze = _guidy.Count;
        DalsieCislo = Databaza.DalsiePrirastkoveCislo(db);
    }

    public DruhImportu Druh { get; }
    public int PocetVDatabaze { get; }
    public int DalsieCislo { get; }

    public static string? NormalizujSeriove(string? seriove) =>
        string.IsNullOrWhiteSpace(seriove) ? null : seriove.Replace(" ", "").Trim().ToUpperInvariant();

    private static string Kluc(string? nazov, string? kategoria, string? miestnost) =>
        $"{Sk.BezDiakritiky(nazov?.Trim())}|{Sk.BezDiakritiky(kategoria?.Trim())}|{Sk.BezDiakritiky(miestnost?.Trim())}";

    /// <summary>Skontroluje všetky riadky (duplicity v rámci súboru závisia od poradia).</summary>
    public void SkontrolujVsetky(IReadOnlyList<RiadokImportu> riadky)
    {
        var guidy = new Dictionary<Guid, int>();
        var seriove = new Dictionary<string, int>(StringComparer.Ordinal);
        var cisla = new Dictionary<int, int>();
        foreach (var riadok in riadky)
        {
            Skontroluj(riadok, guidy, seriove, cisla);
        }
    }

    private void Skontroluj(RiadokImportu r, Dictionary<Guid, int> guidyVSubore, Dictionary<string, int> serioveVSubore, Dictionary<int, int> cislaVSubore)
    {
        var cela = Druh == DruhImportu.CelaDatabaza;
        var chyby = new List<string>();
        var upozornenia = new List<string>();
        string? duplicita = null;
        string? moznaDuplicita = null;

        if (string.IsNullOrWhiteSpace(r.Nazov))
        {
            chyby.Add(Preklad.T("chýba názov"));
        }

        if (!Prevod.SkusCeleCislo(r.Cislo, out var cislo))
        {
            if (cela)
            {
                chyby.Add(Preklad.F($"prírastkové číslo „{r.Cislo}“ nie je kladné celé číslo"));
            }
            else
            {
                upozornenia.Add(Preklad.F($"prírastkové číslo „{r.Cislo}“ sa nerozpoznalo"));
            }
        }
        else if (cela && cislo is null)
        {
            chyby.Add(Preklad.T("chýba prírastkové číslo (pri prenose celej databázy je povinné)"));
        }

        foreach (var stlpec in StlpcePrenosu.Vsetky.Where(s => s.Typ is TypStlpca.Datum or TypStlpca.DatumCas))
        {
            var hodnota = r.Hodnota(stlpec.Kluc);
            if (!Prevod.SkusDatum(hodnota, out _))
            {
                chyby.Add(Preklad.F($"nerozpoznaný dátum v stĺpci {stlpec.Nazov}: „{hodnota}“"));
            }
        }

        if (!Sk.SkusPrecitatSumu(r.Cena, out _))
        {
            chyby.Add(Preklad.F($"nerozpoznaná cena „{r.Cena}“"));
        }

        if (!Prevod.SkusStav(r.Stav, out var stav))
        {
            chyby.Add(Preklad.F($"neznámy stav „{r.Stav}“ (povolené: v prevádzke, v sklade, v oprave, vyradené)"));
        }

        Guid? guid = null;
        if (!string.IsNullOrWhiteSpace(r.Identifikator))
        {
            if (Guid.TryParse(r.Identifikator.Trim(), out var g))
            {
                guid = g;
            }
            else
            {
                upozornenia.Add(Preklad.T("identifikátor (GUID) sa nerozpoznal, položka dostane nový"));
            }
        }

        if (string.IsNullOrWhiteSpace(r.Kategoria))
        {
            upozornenia.Add(Preklad.F($"chýba kategória – položka sa zaradí do kategórie „{Nezaradene}“"));
        }

        if (stav != StavPolozky.Vyradene && (!string.IsNullOrWhiteSpace(r.DatumVyradenia) || !string.IsNullOrWhiteSpace(r.DovodVyradenia)))
        {
            upozornenia.Add(Preklad.T("dátum a dôvod vyradenia sa použijú len pri stave vyradené"));
        }

        var seriove = NormalizujSeriove(r.Seriove);
        if (chyby.Count == 0)
        {
            if (cela)
            {
                if (cislo is int c)
                {
                    if (cislaVSubore.TryGetValue(c, out var prvy))
                    {
                        chyby.Add(Preklad.F($"prírastkové číslo {c} je v súbore už v riadku {prvy}"));
                    }
                    else
                    {
                        cislaVSubore[c] = r.CisloRiadku;
                    }
                }

                if (guid is Guid g)
                {
                    if (guidyVSubore.TryGetValue(g, out var prvy))
                    {
                        chyby.Add(Preklad.F($"rovnaký identifikátor je v súbore už v riadku {prvy}"));
                    }
                    else
                    {
                        guidyVSubore[g] = r.CisloRiadku;
                    }
                }
            }
            else
            {
                if (guid is Guid g && _guidy.Contains(g))
                {
                    duplicita = Preklad.T("už je v evidencii (rovnaký identifikátor)");
                }
                else if (seriove is not null && _seriove.Contains(seriove))
                {
                    duplicita = Preklad.F($"už je v evidencii (sériové číslo {r.Seriove!.Trim()})");
                }
                else if (guid is Guid g2 && guidyVSubore.TryGetValue(g2, out var prvyG))
                {
                    duplicita = Preklad.F($"rovnaká položka je v súbore už v riadku {prvyG}");
                }
                else if (seriove is not null && serioveVSubore.TryGetValue(seriove, out var prvyS))
                {
                    duplicita = Preklad.F($"rovnaké sériové číslo je v súbore už v riadku {prvyS}");
                }
                else if (_kluce.TryGetValue(Kluc(r.Nazov, string.IsNullOrWhiteSpace(r.Kategoria) ? Nezaradene : r.Kategoria, r.Miestnost), out var existujuce))
                {
                    moznaDuplicita = Preklad.F($"pravdepodobná duplicita – rovnaký názov, kategória a miestnosť ako položka č. {existujuce}");
                }

                // do porovnania v rámci súboru idú len riadky, ktoré sa naozaj importujú
                if (duplicita is null && r.Importovat)
                {
                    if (guid is Guid g3)
                    {
                        guidyVSubore.TryAdd(g3, r.CisloRiadku);
                    }

                    if (seriove is not null)
                    {
                        serioveVSubore.TryAdd(seriove, r.CisloRiadku);
                    }
                }
            }
        }

        var novy = chyby.Count > 0 ? StavRiadku.Chyba
            : duplicita is not null ? StavRiadku.Duplicita
            : moznaDuplicita is not null ? StavRiadku.MoznaDuplicita
            : upozornenia.Count > 0 ? StavRiadku.Upozornenie
            : StavRiadku.Ok;

        var problemy = new List<string>(chyby);
        if (duplicita is not null)
        {
            problemy.Add(duplicita);
        }

        if (moznaDuplicita is not null)
        {
            problemy.Add(moznaDuplicita);
        }

        problemy.AddRange(upozornenia);

        var predtym = r.Vysledok;
        var prva = !r.Skontrolovany;
        r.Skontrolovany = true;
        r.Vysledok = novy;
        r.Problem = string.Join("; ", problemy);
        if (novy is StavRiadku.Chyba or StavRiadku.Duplicita)
        {
            r.Importovat = false;
        }
        else if (prva || predtym != novy)
        {
            r.Importovat = novy is StavRiadku.Ok or StavRiadku.Upozornenie;
        }
    }

    /// <summary>Koľko položiek pribudne, koľko sa preskočí a prečo, a ktoré číselníky sa vytvoria.</summary>
    public NahladImportu Nahlad(IReadOnlyList<RiadokImportu> riadky)
    {
        var pridat = riadky.Where(r => r.Importovat && r.MozeImportovat).ToList();
        var dovody = new List<string>();
        void Dovod(int pocet, string text)
        {
            if (pocet > 0)
            {
                dovody.Add($"{pocet} × {text}");
            }
        }

        Dovod(riadky.Count(r => r.Vysledok == StavRiadku.Chyba), Preklad.T("chyba v riadku (dá sa opraviť v kroku Kontrola)"));
        Dovod(riadky.Count(r => r.Vysledok == StavRiadku.Duplicita), Preklad.T("položka už je v evidencii alebo je v súbore dvakrát"));
        Dovod(riadky.Count(r => r.Vysledok == StavRiadku.MoznaDuplicita && !r.Importovat), Preklad.T("pravdepodobná duplicita, ktorú ste nevybrali"));
        Dovod(riadky.Count(r => r.Vysledok is StavRiadku.Ok or StavRiadku.Upozornenie && !r.Importovat), Preklad.T("vynechané ručne"));

        var kategorie = pridat.Select(r => string.IsNullOrWhiteSpace(r.Kategoria) ? Nezaradene : r.Kategoria.Trim());
        var miestnosti = pridat.Where(r => !string.IsNullOrWhiteSpace(r.Miestnost)).Select(r => r.Miestnost!.Trim());
        var osoby = pridat.Where(r => !string.IsNullOrWhiteSpace(r.Pridelene)).Select(r => ImportService.UpravMeno(r.Pridelene!));
        if (Druh == DruhImportu.CelaDatabaza && _zdroj.Balik is { } balik)
        {
            kategorie = kategorie.Concat(balik.Kategorie.Select(k => k.Nazov.Trim()));
            miestnosti = miestnosti.Concat(balik.Miestnosti.Select(m => m.Nazov.Trim()));
            osoby = osoby.Concat(balik.Osoby.Select(o => o.CeleMeno));
        }

        List<string> Nove(IEnumerable<string> nazvy, HashSet<string> existujuce) =>
            nazvy.Where(n => n.Length > 0 && !existujuce.Contains(n)).Distinct(Sk.Porovnanie).OrderBy(n => n, Sk.Porovnanie).ToList();

        int? od = null;
        int? po = null;
        if (pridat.Count > 0)
        {
            if (Druh == DruhImportu.Zlucenie)
            {
                od = DalsieCislo;
                po = DalsieCislo + pridat.Count - 1;
            }
            else
            {
                var cisla = pridat.Select(r => Prevod.SkusCeleCislo(r.Cislo, out var c) ? c : null).Where(c => c is not null).Select(c => c!.Value).ToList();
                od = cisla.Count > 0 ? cisla.Min() : null;
                po = cisla.Count > 0 ? cisla.Max() : null;
            }
        }

        return new NahladImportu(
            pridat.Count,
            riadky.Count - pridat.Count,
            dovody,
            Nove(kategorie, _kategorie),
            Nove(miestnosti, _miestnosti),
            Nove(osoby, _osoby),
            od,
            po);
    }
}

/// <summary>Import položiek zo súborov (Excel, CSV, JSON, XML) so zálohou pred importom a možnosťou vrátenia.</summary>
public sealed class ImportService
{
    public const string KlucPoslednaZaloha = "Import.PoslednaZaloha";
    public const string KlucPoslednyPopis = "Import.PoslednyPopis";

    public ZdrojImportu Nacitaj(string cesta) => CitacSuborov.Nacitaj(cesta);

    /// <summary>Priradenie stĺpcov súboru k poliam EviTA podľa názvov (každé pole najviac raz).</summary>
    public static List<string?> AutomatickePriradenie(IReadOnlyList<string> hlavicky)
    {
        var pouzite = new HashSet<string>();
        return hlavicky.Select(h => StlpcePrenosu.Rozpoznaj(h) is { } kluc && pouzite.Add(kluc) ? kluc : null).ToList();
    }

    /// <summary>
    /// Vytvorí riadky importu podľa priradenia stĺpcov. Ak je jedno pole priradené viacerým stĺpcom
    /// (napr. Meno a Priezvisko → Pridelené), hodnoty sa spoja medzerou.
    /// </summary>
    public static List<RiadokImportu> Priprav(ZdrojImportu zdroj, IReadOnlyList<string?> priradenie)
    {
        var riadky = new List<RiadokImportu>(zdroj.Riadky.Count);
        for (var i = 0; i < zdroj.Riadky.Count; i++)
        {
            var hodnoty = zdroj.Riadky[i];
            var riadok = new RiadokImportu(i < zdroj.CislaRiadkov.Count ? zdroj.CislaRiadkov[i] : i + 1);
            for (var c = 0; c < priradenie.Count && c < hodnoty.Length; c++)
            {
                if (priradenie[c] is not { } kluc || string.IsNullOrWhiteSpace(hodnoty[c]))
                {
                    continue;
                }

                var doterajsia = riadok.Hodnota(kluc);
                riadok.Nastav(kluc, string.IsNullOrEmpty(doterajsia) ? hodnoty[c] : $"{doterajsia} {hodnoty[c]}");
            }

            riadky.Add(riadok);
        }

        return riadky;
    }

    /// <summary>Prenesie typy udalostí a udalosti kalendára z úplného exportu. Vráti počet prenesených udalostí.</summary>
    private static int PrenesKalendar(EvitaDbContext db, BalikEviTA balik, List<Polozka> polozky,
        Dictionary<string, Miestnost> miestnosti, Dictionary<string, Osoba> osoby, DateTime teraz)
    {
        if (balik.TypyUdalosti is null && balik.Udalosti is null)
        {
            return 0;
        }

        var typy = new Dictionary<string, TypUdalosti>(Sk.Porovnanie);
        foreach (var t in db.TypyUdalosti.ToList())
        {
            typy.TryAdd(t.Nazov, t);
        }

        var poradie = typy.Count == 0 ? 0 : typy.Values.Max(t => t.Poradie) + 1;
        foreach (var t in balik.TypyUdalosti ?? new List<TypUdalostiPrenos>())
        {
            if (string.IsNullOrWhiteSpace(t.Nazov))
            {
                continue;
            }

            if (typy.TryGetValue(t.Nazov.Trim(), out var existujuci))
            {
                existujuci.Farba = t.Farba;
                existujuci.Aktivny = t.Aktivny;
            }
            else
            {
                var novy = new TypUdalosti { Nazov = t.Nazov.Trim(), Farba = t.Farba, Aktivny = t.Aktivny, Poradie = poradie++ };
                db.TypyUdalosti.Add(novy);
                typy[novy.Nazov] = novy;
            }
        }

        db.SaveChanges();
        var podlaGuid = polozky.ToDictionary(p => p.Guid);
        var podlaCisla = polozky.ToDictionary(p => p.PrirastkoveCislo);
        var pouziteGuidy = db.Udalosti.Select(u => u.Guid).ToHashSet();
        var pocet = 0;
        foreach (var u in balik.Udalosti ?? new List<UdalostPrenos>())
        {
            if (string.IsNullOrWhiteSpace(u.Nazov) || !Prevod.SkusDatum(u.Zaciatok, out var zaciatok) || zaciatok is null)
            {
                continue;
            }

            var nazovTypu = string.IsNullOrWhiteSpace(u.Typ) ? (KalendarService.NajdiTyp(typy.Values, "iné")?.Nazov ?? Preklad.T("iné")) : u.Typ.Trim();
            if (!typy.TryGetValue(nazovTypu, out var typ))
            {
                typ = new TypUdalosti { Nazov = nazovTypu, Poradie = poradie++ };
                db.TypyUdalosti.Add(typ);
                typy[typ.Nazov] = typ;
            }

            Polozka? polozka = null;
            if (Guid.TryParse(u.PolozkaGuid, out var gp) && podlaGuid.TryGetValue(gp, out var p1))
            {
                polozka = p1;
            }
            else if (u.PolozkaCislo is int c && podlaCisla.TryGetValue(c, out var p2))
            {
                polozka = p2;
            }

            Prevod.SkusDatum(u.Koniec, out var koniec);
            Prevod.SkusDatum(u.OpakovanieDo, out var opakovanieDo);
            var guid = Guid.TryParse(u.Guid, out var g) && pouziteGuidy.Add(g) ? g : Guid.NewGuid();
            db.Udalosti.Add(new Udalost
            {
                Guid = guid,
                Nazov = u.Nazov.Trim(),
                Popis = u.Popis,
                TypUdalosti = typ,
                Druh = Enum.TryParse<DruhUdalosti>(u.Druh, true, out var druh) ? druh : DruhUdalosti.Udalost,
                Zaciatok = zaciatok.Value,
                Koniec = koniec,
                CelyDen = u.CelyDen,
                Opakovanie = Enum.TryParse<OpakovanieUdalosti>(u.Opakovanie, true, out var opak) ? opak : OpakovanieUdalosti.Ziadne,
                OpakovanieDo = opakovanieDo,
                Pripomienka = Enum.TryParse<PripomienkaUdalosti>(u.Pripomienka, true, out var prip) ? prip : PripomienkaUdalosti.Ziadna,
                Polozka = polozka,
                Miestnost = !string.IsNullOrWhiteSpace(u.Miestnost) && miestnosti.TryGetValue(u.Miestnost.Trim(), out var m) ? m : null,
                Osoba = !string.IsNullOrWhiteSpace(u.Osoba) && osoby.TryGetValue(UpravMeno(u.Osoba), out var o) ? o : null,
                Stav = Enum.TryParse<StavUdalosti>(u.Stav, true, out var stav) ? stav : StavUdalosti.Naplanovane,
                Vytvorene = teraz,
                Zmenene = teraz,
            });
            pocet++;
        }

        db.SaveChanges();
        return pocet;
    }

    /// <summary>Prenesie úlohy a poznámky z úplného exportu. Udalosti už musia byť uložené (poznámky sa na ne viažu podľa identifikátora).</summary>
    private static (int Ulohy, int Poznamky) PrenesUlohyAPoznamky(EvitaDbContext db, BalikEviTA balik, List<Polozka> polozky, DateTime teraz)
    {
        if (balik.Ulohy is null && balik.Poznamky is null)
        {
            return (0, 0);
        }

        var podlaGuid = polozky.ToDictionary(p => p.Guid);
        var podlaCisla = polozky.ToDictionary(p => p.PrirastkoveCislo);
        Polozka? Najdi(string? guid, int? cislo) =>
            Guid.TryParse(guid, out var g) && podlaGuid.TryGetValue(g, out var p1) ? p1
            : cislo is int c && podlaCisla.TryGetValue(c, out var p2) ? p2
            : null;

        var ulohy = 0;
        var pouzite = db.Ulohy.Select(u => u.Guid).ToHashSet();
        var poradie = 0;
        foreach (var u in balik.Ulohy ?? new List<UlohaPrenos>())
        {
            if (string.IsNullOrWhiteSpace(u.Text))
            {
                continue;
            }

            Prevod.SkusDatum(u.Termin, out var termin);
            Prevod.SkusDatum(u.DatumHotovo, out var hotovo);
            Prevod.SkusDatum(u.Vytvorene, out var vytvorene);
            db.Ulohy.Add(new Uloha
            {
                Guid = Guid.TryParse(u.Guid, out var g) && pouzite.Add(g) ? g : Guid.NewGuid(),
                Text = u.Text.Trim(),
                Popis = u.Popis,
                Termin = termin?.Date,
                Priorita = Enum.TryParse<PrioritaUlohy>(u.Priorita, true, out var priorita) ? priorita : PrioritaUlohy.Normalna,
                Hotovo = u.Hotovo,
                DatumHotovo = u.Hotovo ? hotovo ?? teraz : null,
                Vytvorene = vytvorene ?? teraz,
                Poradie = poradie++,
                Polozka = Najdi(u.PolozkaGuid, u.PolozkaCislo),
            });
            ulohy++;
        }

        var udalosti = db.Udalosti.ToDictionary(x => x.Guid);
        var poznamky = 0;
        pouzite = db.Poznamky.Select(p => p.Guid).ToHashSet();
        foreach (var p in balik.Poznamky ?? new List<PoznamkaPrenos>())
        {
            if (string.IsNullOrWhiteSpace(p.Nazov) && string.IsNullOrWhiteSpace(p.Text))
            {
                continue;
            }

            Prevod.SkusDatum(p.Vytvorene, out var vytvorene);
            Prevod.SkusDatum(p.Zmenene, out var zmenene);
            Prevod.SkusDatum(p.Datum, out var datum);
            db.Poznamky.Add(new Poznamka
            {
                Guid = Guid.TryParse(p.Guid, out var g) && pouzite.Add(g) ? g : Guid.NewGuid(),
                Nazov = string.IsNullOrWhiteSpace(p.Nazov) ? PoznamkyService.NazovZTextu(p.Text) : p.Nazov.Trim(),
                Text = p.Text ?? "",
                Farba = PoznamkyService.JePlatnaFarba(p.Farba) ? p.Farba : PoznamkyService.Farby[0],
                Pripnuta = p.Pripnuta,
                Vytvorene = vytvorene ?? teraz,
                Zmenene = zmenene ?? vytvorene ?? teraz,
                Datum = datum?.Date,
                Polozka = Najdi(p.PolozkaGuid, p.PolozkaCislo),
                Udalost = Guid.TryParse(p.UdalostGuid, out var ug) && udalosti.TryGetValue(ug, out var udalost) ? udalost : null,
            });
            poznamky++;
        }

        db.SaveChanges();
        return (ulohy, poznamky);
    }

    /// <summary>Meno osoby bez nadbytočných medzier.</summary>
    public static string UpravMeno(string meno) =>
        string.Join(' ', meno.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Posledný import, ktorý sa dá vrátiť (záloha pred ním a popis), alebo null.</summary>
    public (string Zaloha, string Popis)? PoslednyImport()
    {
        using var db = Databaza.Otvor();
        var zaloha = Databaza.Citaj(db, KlucPoslednaZaloha);
        var popis = Databaza.Citaj(db, KlucPoslednyPopis);
        return !string.IsNullOrEmpty(zaloha) && File.Exists(zaloha) ? (zaloha, popis ?? "") : null;
    }

    /// <summary>Vráti databázu do stavu pred posledným importom (obnovou zo zálohy, ktorá sa urobila pred ním).</summary>
    public void VratPoslednyImport()
    {
        var posledny = PoslednyImport() ?? throw new EvitaException(Preklad.T("Nie je čo vrátiť – záloha pred posledným importom sa nenašla."));
        new ZalohaService().Obnov(posledny.Zaloha, null);
    }

    /// <summary>Zapíše vybrané riadky do databázy. Pred zápisom urobí zálohu, aby sa import dal vrátiť.</summary>
    public VysledokImportu Importuj(ZdrojImportu zdroj, IReadOnlyList<RiadokImportu> riadky, DruhImportu druh)
    {
        var vybrane = riadky.Where(r => r.Importovat && r.MozeImportovat).ToList();
        if (vybrane.Count == 0)
        {
            throw new EvitaException(Preklad.T("Nie je vybraná žiadna položka na import."));
        }

        if (druh == DruhImportu.CelaDatabaza)
        {
            using var kontrola = Databaza.Otvor();
            if (kontrola.Polozky.Any())
            {
                throw new EvitaException(
                    Preklad.T("Prenos celej databázy sa dá urobiť len do prázdnej evidencie. Najprv vymažte dáta (Servis → Vymazať dáta a začať nanovo), alebo zvoľte doplnenie položiek do evidencie."));
            }
        }

        var nazovSuboru = zdroj.NazovSuboru;
        var zaloha = new ZalohaService().Zalohuj(TypZalohy.PredImportom, poznamka: Preklad.F($"Automatická záloha pred importom zo súboru {nazovSuboru}"));
        var teraz = DateTime.Now;
        var pouzivatel = Environment.UserName;

        using var db = Databaza.Otvor();
        using var transakcia = db.Database.BeginTransaction();

        var kategorie = new Dictionary<string, Kategoria>(Sk.Porovnanie);
        foreach (var k in db.Kategorie.ToList())
        {
            kategorie.TryAdd(k.Nazov, k);
        }

        var miestnosti = new Dictionary<string, Miestnost>(Sk.Porovnanie);
        foreach (var m in db.Miestnosti.ToList())
        {
            miestnosti.TryAdd(m.Nazov, m);
        }

        var osoby = new Dictionary<string, Osoba>(Sk.Porovnanie);
        foreach (var o in db.Osoby.ToList())
        {
            osoby.TryAdd($"{o.Meno} {o.Priezvisko}".Trim(), o);
            osoby.TryAdd($"{o.Priezvisko} {o.Meno}".Trim(), o);
        }

        var noveKategorie = new List<string>();
        var noveMiestnosti = new List<string>();
        var noveOsoby = new List<string>();

        // pri prenose celej databázy sa prenesú aj číselníky s ich vlastnosťami (aj nepoužité)
        if (druh == DruhImportu.CelaDatabaza && zdroj.Balik is { } balik)
        {
            foreach (var k in balik.Kategorie.Where(k => !string.IsNullOrWhiteSpace(k.Nazov)))
            {
                if (!kategorie.ContainsKey(k.Nazov.Trim()))
                {
                    var nova = new Kategoria { Nazov = k.Nazov.Trim(), Aktivna = k.Aktivna };
                    db.Kategorie.Add(nova);
                    kategorie[nova.Nazov] = nova;
                    noveKategorie.Add(nova.Nazov);
                }
            }

            foreach (var m in balik.Miestnosti.Where(m => !string.IsNullOrWhiteSpace(m.Nazov)))
            {
                if (!miestnosti.ContainsKey(m.Nazov.Trim()))
                {
                    var nova = new Miestnost { Nazov = m.Nazov.Trim(), Budova = Sk.Prazdne(m.Budova), Poznamka = Sk.Prazdne(m.Poznamka), Aktivna = m.Aktivna };
                    db.Miestnosti.Add(nova);
                    miestnosti[nova.Nazov] = nova;
                    noveMiestnosti.Add(nova.Nazov);
                }
            }

            foreach (var o in balik.Osoby.Where(o => !string.IsNullOrWhiteSpace(o.Priezvisko)))
            {
                var meno = UpravMeno(o.CeleMeno);
                if (!osoby.ContainsKey(meno))
                {
                    var nova = new Osoba { Meno = o.Meno.Trim(), Priezvisko = o.Priezvisko.Trim(), Funkcia = Sk.Prazdne(o.Funkcia), Aktivna = o.Aktivna };
                    db.Osoby.Add(nova);
                    osoby.TryAdd(meno, nova);
                    osoby.TryAdd($"{nova.Priezvisko} {nova.Meno}".Trim(), nova);
                    noveOsoby.Add(meno);
                }
            }
        }

        Kategoria KategoriaPre(string? nazov)
        {
            var n = string.IsNullOrWhiteSpace(nazov) ? KontrolaImportu.Nezaradene : nazov.Trim();
            if (!kategorie.TryGetValue(n, out var k))
            {
                k = new Kategoria { Nazov = n };
                db.Kategorie.Add(k);
                kategorie[n] = k;
                noveKategorie.Add(n);
            }

            return k;
        }

        Miestnost? MiestnostPre(string? nazov)
        {
            if (string.IsNullOrWhiteSpace(nazov))
            {
                return null;
            }

            var n = nazov.Trim();
            if (!miestnosti.TryGetValue(n, out var m))
            {
                m = new Miestnost { Nazov = n };
                db.Miestnosti.Add(m);
                miestnosti[n] = m;
                noveMiestnosti.Add(n);
            }

            return m;
        }

        Osoba? OsobaPre(string? meno)
        {
            if (string.IsNullOrWhiteSpace(meno))
            {
                return null;
            }

            var cele = UpravMeno(meno);
            if (!osoby.TryGetValue(cele, out var o))
            {
                var casti = cele.Split(' ');
                o = new Osoba
                {
                    Meno = casti.Length > 1 ? string.Join(' ', casti[..^1]) : "",
                    Priezvisko = casti[^1],
                };
                db.Osoby.Add(o);
                osoby[cele] = o;
                osoby.TryAdd($"{o.Priezvisko} {o.Meno}".Trim(), o);
                noveOsoby.Add(cele);
            }

            return o;
        }

        static DateTime? Datum(string? text) => Prevod.SkusDatum(text, out var d) ? d : null;
        static int? Cislo(string? text) => Prevod.SkusCeleCislo(text, out var c) ? c : null;

        var poradie = druh == DruhImportu.Zlucenie
            ? vybrane.OrderBy(r => Cislo(r.Cislo) ?? int.MaxValue).ThenBy(r => r.CisloRiadku).ToList()
            : vybrane.OrderBy(r => Cislo(r.Cislo)).ToList();

        var dalsie = Databaza.DalsiePrirastkoveCislo(db);
        var pouziteGuidy = db.Polozky.Select(p => p.Guid).ToHashSet();
        var nove = new List<(RiadokImportu Riadok, Polozka Polozka, int? Povodne)>();

        foreach (var r in poradie)
        {
            var povodne = Cislo(r.Cislo);
            Prevod.SkusStav(r.Stav, out var stav);
            Sk.SkusPrecitatSumu(r.Cena, out var cena);

            Guid guid;
            if (!(Guid.TryParse(r.Identifikator?.Trim(), out guid) && pouziteGuidy.Add(guid)))
            {
                do
                {
                    guid = Guid.NewGuid();
                }
                while (!pouziteGuidy.Add(guid));
            }

            var pridane = Datum(r.DatumPridania) ?? teraz;
            var p = new Polozka
            {
                Guid = guid,
                PrirastkoveCislo = druh == DruhImportu.CelaDatabaza ? povodne!.Value : dalsie++,
                Nazov = r.Nazov!.Trim(),
                Kategoria = KategoriaPre(r.Kategoria),
                Miestnost = MiestnostPre(r.Miestnost),
                Osoba = OsobaPre(r.Pridelene),
                Vyrobca = Sk.Prazdne(r.Vyrobca),
                Model = Sk.Prazdne(r.Model),
                SerioveCislo = Sk.Prazdne(r.Seriove),
                InventarneCislo = Sk.Prazdne(r.Inventarne),
                Stav = stav,
                DatumNakupu = Datum(r.DatumNakupu)?.Date,
                Cena = cena,
                ZarukaDo = Datum(r.ZarukaDo)?.Date,
                DalsiaRevizia = Datum(r.DalsiaRevizia)?.Date,
                DatumPridania = pridane,
                DatumZmeny = druh == DruhImportu.CelaDatabaza ? Datum(r.DatumZmeny) ?? pridane : teraz,
                DatumVyradenia = stav == StavPolozky.Vyradene ? (Datum(r.DatumVyradenia) ?? teraz).Date : null,
                DovodVyradenia = stav == StavPolozky.Vyradene ? Sk.Prazdne(r.DovodVyradenia) : null,
                Poznamka = string.IsNullOrWhiteSpace(r.Poznamka) ? null : r.Poznamka.TrimEnd(),
                Verzia = 1,
            };
            db.Polozky.Add(p);
            nove.Add((r, p, povodne));
        }

        db.SaveChanges();

        // história: pôvodné prírastkové číslo, pri prenose celej databázy aj pôvodná história zmien
        var prenesenaHistoria = 0;
        if (druh == DruhImportu.CelaDatabaza && zdroj.Balik?.Historia is { Count: > 0 } historia)
        {
            var podlaGuid = nove.ToDictionary(x => x.Polozka.Guid, x => x.Polozka.Id);
            var podlaCisla = nove.ToDictionary(x => x.Polozka.PrirastkoveCislo, x => x.Polozka.Id);
            foreach (var h in historia)
            {
                var id = Guid.TryParse(h.GuidPolozky, out var g) && podlaGuid.TryGetValue(g, out var i) ? i
                    : podlaCisla.TryGetValue(h.PrirastkoveCislo, out var j) && h.GuidPolozky is null ? j
                    : 0;
                db.Historia.Add(new HistoriaZmeny
                {
                    PolozkaId = id,
                    PrirastkoveCislo = h.PrirastkoveCislo,
                    NazovPolozky = h.NazovPolozky,
                    Cas = Datum(h.Cas) ?? teraz,
                    Pouzivatel = h.Pouzivatel,
                    Akcia = h.Akcia,
                    Pole = h.Pole,
                    StaraHodnota = h.StaraHodnota,
                    NovaHodnota = h.NovaHodnota,
                });
                prenesenaHistoria++;
            }
        }

        // pri prenose celej databázy aj pracovný kalendár
        var preneseneUdalosti = 0;
        int preneseneUlohy = 0, prenesenePoznamky = 0;
        if (druh == DruhImportu.CelaDatabaza && zdroj.Balik is { } balikKalendara)
        {
            preneseneUdalosti = PrenesKalendar(db, balikKalendara, nove.Select(x => x.Polozka).ToList(), miestnosti, osoby, teraz);
            (preneseneUlohy, prenesenePoznamky) = PrenesUlohyAPoznamky(db, balikKalendara, nove.Select(x => x.Polozka).ToList(), teraz);
        }

        foreach (var (riadok, polozka, povodne) in nove)
        {
            db.Historia.Add(new HistoriaZmeny
            {
                PolozkaId = polozka.Id,
                PrirastkoveCislo = polozka.PrirastkoveCislo,
                NazovPolozky = polozka.Nazov,
                Cas = teraz,
                Pouzivatel = pouzivatel,
                Akcia = PolozkaService.AkciaImport,
                NovaHodnota = druh == DruhImportu.CelaDatabaza
                    ? Preklad.F($"prenos celej databázy zo súboru {nazovSuboru}")
                    : Preklad.F($"importované zo súboru {nazovSuboru}, riadok {riadok.CisloRiadku}") + (povodne is int c ? Preklad.F($", pôvodné prírastkové číslo {c}") : ""),
            });
        }

        var cisla = nove.Select(x => x.Polozka.PrirastkoveCislo).ToList();
        var posledne = druh == DruhImportu.CelaDatabaza
            ? Math.Max(zdroj.Balik?.PoslednePrirastkoveCislo ?? 0, cisla.Max())
            : dalsie - 1;
        Databaza.Zapis(db, Databaza.KlucPoslednePrirastkove, posledne.ToString());

        var popis = $"{Sk.DatumCas(teraz)} – súbor {nazovSuboru}, pridané {ExportService.PocetPoloziek(nove.Count)}";
        Databaza.Zapis(db, KlucPoslednaZaloha, zaloha);
        Databaza.Zapis(db, KlucPoslednyPopis, popis);
        db.SaveChanges();
        transakcia.Commit();

        // protokol
        var sb = new StringBuilder();
        sb.AppendLine(Preklad.T("EviTA – protokol importu"));
        sb.AppendLine(new string('=', 60));
        sb.AppendLine(Preklad.F($"Čas:                 {Sk.DatumCas(teraz)}"));
        sb.AppendLine(Preklad.F($"Súbor:               {zdroj.Cesta}"));
        sb.AppendLine(Preklad.F($"Druh importu:        {(druh == DruhImportu.Zlucenie ? Preklad.T("doplnenie položiek do evidencie") : Preklad.T("prenos celej databázy do prázdnej"))}"));
        sb.AppendLine(Preklad.F($"Záloha pred importom: {zaloha}"));
        sb.AppendLine(Preklad.F($"Riadkov v súbore:    {riadky.Count}"));
        sb.AppendLine(Preklad.F($"Pridané:             {ExportService.PocetPoloziek(nove.Count)} (prírastkové čísla {cisla.Min()} – {cisla.Max()})"));
        sb.AppendLine(Preklad.F($"Preskočené:          {riadky.Count - nove.Count}"));
        if (noveKategorie.Count > 0)
        {
            sb.AppendLine(Preklad.F($"Nové kategórie:      {string.Join(", ", noveKategorie)}"));
        }

        if (noveMiestnosti.Count > 0)
        {
            sb.AppendLine(Preklad.F($"Nové miestnosti:     {string.Join(", ", noveMiestnosti)}"));
        }

        if (noveOsoby.Count > 0)
        {
            sb.AppendLine(Preklad.F($"Nové osoby:          {string.Join(", ", noveOsoby)}"));
        }

        if (prenesenaHistoria > 0)
        {
            sb.AppendLine(Preklad.F($"Prenesená história:  {prenesenaHistoria} záznamov"));
        }

        if (preneseneUdalosti > 0)
        {
            sb.AppendLine(Preklad.F($"Udalosti kalendára:  {preneseneUdalosti}"));
        }

        if (preneseneUlohy > 0)
        {
            sb.AppendLine(Preklad.F($"Úlohy:               {preneseneUlohy}"));
        }

        if (prenesenePoznamky > 0)
        {
            sb.AppendLine(Preklad.F($"Poznámky:            {prenesenePoznamky}"));
        }

        foreach (var upozornenie in zdroj.Upozornenia)
        {
            sb.AppendLine(Preklad.F($"Upozornenie:         {upozornenie}"));
        }

        var preskocene = riadky.Where(r => !(r.Importovat && r.MozeImportovat)).ToList();
        if (preskocene.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Preklad.T("Preskočené riadky"));
            sb.AppendLine(new string('-', 60));
            foreach (var r in preskocene)
            {
                var dovod = r.Problem.Length > 0 ? r.Problem : Preklad.T("vynechané ručne");
                if (r.Vysledok is StavRiadku.MoznaDuplicita or StavRiadku.Ok or StavRiadku.Upozornenie)
                {
                    dovod = Preklad.F($"nevybrané – {dovod}");
                }

                sb.AppendLine(Preklad.F($"riadok {r.CisloRiadku}: {r.Nazov} – {dovod}"));
            }
        }

        sb.AppendLine();
        sb.AppendLine(Preklad.T("Pridané položky"));
        sb.AppendLine(new string('-', 60));
        foreach (var (riadok, polozka, povodne) in nove.OrderBy(x => x.Polozka.PrirastkoveCislo))
        {
            var text = Preklad.F($"č. {polozka.PrirastkoveCislo}  {polozka.Nazov}  (riadok {riadok.CisloRiadku}");
            if (druh == DruhImportu.Zlucenie && povodne is int c)
            {
                text += Preklad.F($", pôvodné č. {c}");
            }

            text += ")";
            if (riadok.Vysledok == StavRiadku.Upozornenie || riadok.Vysledok == StavRiadku.MoznaDuplicita)
            {
                text += " – " + riadok.Problem;
            }

            sb.AppendLine(text);
        }

        return new VysledokImportu(nove.Count, riadky.Count - nove.Count, zaloha, sb.ToString(), cisla.Min(), cisla.Max());
    }
}
