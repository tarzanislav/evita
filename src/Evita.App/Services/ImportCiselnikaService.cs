using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Evita.Data;
using Evita.Models;

namespace Evita.Services;

/// <summary>Pole číselníka pri importe (napr. Názov, Budova, Priezvisko).</summary>
public sealed record PoleCiselnika(string Kluc, string NazovSk, bool Povinne, params string[] Aliasy)
{
    public string Nazov => Preklad.T(NazovSk);
}

/// <summary>Polia číselníkov pri importe a rozpoznanie stĺpcov a hárkov podľa názvu (vo všetkých jazykoch).</summary>
public static class PolaCiselnika
{
    public const string Nazov = "nazov";
    public const string Budova = "budova";
    public const string Poznamka = "poznamka";
    public const string Meno = "meno";
    public const string Priezvisko = "priezvisko";
    public const string CeleMeno = "celemeno";
    public const string Funkcia = "funkcia";
    public const string Aktivna = "aktivna";

    private static readonly Dictionary<TypCiselnika, Dictionary<string, string>> Aliasy = new();

    public static IReadOnlyList<PoleCiselnika> Pre(TypCiselnika typ) => typ switch
    {
        TypCiselnika.Kategorie => new PoleCiselnika[]
        {
            new(Nazov, Preklad.N("Názov"), true, "kategoria", "nazovkategorie", "category", "categoryname", "name"),
            new(Aktivna, Preklad.N("Aktívna"), false, "aktivny", "aktivne", "active"),
        },
        TypCiselnika.Miestnosti => new PoleCiselnika[]
        {
            new(Nazov, Preklad.N("Názov"), true, "miestnost", "nazovmiestnosti", "room", "roomname", "name"),
            new(Budova, Preklad.N("Budova"), false, "poschodie", "budovaaleboposchodie", "building", "floor"),
            new(Poznamka, Preklad.N("Poznámka"), false, "note", "notes", "popis"),
            new(Aktivna, Preklad.N("Aktívna"), false, "aktivny", "aktivne", "active"),
        },
        _ => new PoleCiselnika[]
        {
            new(Meno, Preklad.N("Meno"), false, "krstnemeno", "firstname", "givenname"),
            new(Priezvisko, Preklad.N("Priezvisko"), false, "surname", "lastname", "familyname", "priezviskoalebonazovutvaru"),
            new(CeleMeno, Preklad.N("Meno a priezvisko"), false, "osoba", "pridelene", "menoapriezvisko", "fullname", "name", "person"),
            new(Funkcia, Preklad.N("Funkcia"), false, "utvar", "funkciaaleboutvar", "position", "department", "jobtitle"),
            new(Aktivna, Preklad.N("Aktívna"), false, "aktivny", "aktivne", "active"),
        },
    };

    /// <summary>Názov číselníka (Kategórie, Miestnosti, Osoby).</summary>
    public static string NazovSk(TypCiselnika typ) => typ switch
    {
        TypCiselnika.Kategorie => Preklad.N("Kategórie"),
        TypCiselnika.Miestnosti => Preklad.N("Miestnosti"),
        _ => Preklad.N("Osoby"),
    };

    public static string NazovCiselnika(TypCiselnika typ) => Preklad.T(NazovSk(typ));

    /// <summary>Kľúč poľa číselníka pre názov stĺpca, alebo null.</summary>
    public static string? Rozpoznaj(TypCiselnika typ, string? hlavicka)
    {
        var normalizovany = StlpcePrenosu.Normalizuj(hlavicka);
        if (normalizovany.Length == 0)
        {
            return null;
        }

        Dictionary<string, string> aliasy;
        lock (Aliasy)
        {
            if (!Aliasy.TryGetValue(typ, out aliasy!))
            {
                aliasy = new Dictionary<string, string>(StringComparer.Ordinal);
                var slovniky = Preklad.Jazyky.Where(j => j.Kod != "sk").Select(j => Preklad.NacitajSlovnik(j.Kod)).ToList();
                foreach (var pole in Pre(typ))
                {
                    aliasy.TryAdd(StlpcePrenosu.Normalizuj(pole.NazovSk), pole.Kluc);
                    aliasy.TryAdd(pole.Kluc, pole.Kluc);
                    foreach (var slovnik in slovniky)
                    {
                        if (slovnik.TryGetValue(pole.NazovSk, out var preklad))
                        {
                            aliasy.TryAdd(StlpcePrenosu.Normalizuj(preklad), pole.Kluc);
                        }
                    }

                    foreach (var alias in pole.Aliasy)
                    {
                        aliasy.TryAdd(alias, pole.Kluc);
                    }
                }

                Aliasy[typ] = aliasy;
            }
        }

        return aliasy.GetValueOrDefault(normalizovany);
    }

    /// <summary>Priradenie stĺpcov súboru k poliam číselníka (každé pole najviac raz).</summary>
    public static List<string?> AutomatickePriradenie(TypCiselnika typ, IReadOnlyList<string> hlavicky)
    {
        var pouzite = new HashSet<string>();
        return hlavicky.Select(h => Rozpoznaj(typ, h) is { } kluc && pouzite.Add(kluc) ? kluc : null).ToList();
    }

    /// <summary>Hárok zošita s názvom číselníka (v ktoromkoľvek jazyku programu).</summary>
    public static bool JeHarok(TypCiselnika typ, string nazovHarku)
    {
        var nazov = StlpcePrenosu.Normalizuj(nazovHarku);
        var sk = NazovSk(typ);
        if (nazov == StlpcePrenosu.Normalizuj(sk))
        {
            return true;
        }

        return Preklad.Jazyky.Where(j => j.Kod != "sk")
            .Any(j => Preklad.NacitajSlovnik(j.Kod).TryGetValue(sk, out var preklad) && StlpcePrenosu.Normalizuj(preklad) == nazov);
    }

    /// <summary>Zo súboru EviTA (JSON, XML) vytvorí tabuľku so zoznamom číselníka namiesto položiek.</summary>
    public static void NaplnZBaliku(ZdrojImportu zdroj, BalikEviTA balik, TypCiselnika typ)
    {
        zdroj.Hlavicky.Clear();
        zdroj.Riadky.Clear();
        zdroj.CislaRiadkov.Clear();
        var ano = Preklad.T("áno");
        var nie = Preklad.T("nie");
        string Aktivny(bool a) => a ? ano : nie;

        IEnumerable<string?[]> riadky;
        switch (typ)
        {
            case TypCiselnika.Kategorie:
                zdroj.Hlavicky.AddRange(new[] { Preklad.T("Názov"), Preklad.T("Aktívna") });
                riadky = balik.Kategorie.Select(k => new string?[] { k.Nazov, Aktivny(k.Aktivna) });
                break;
            case TypCiselnika.Miestnosti:
                zdroj.Hlavicky.AddRange(new[] { Preklad.T("Názov"), Preklad.T("Budova"), Preklad.T("Poznámka"), Preklad.T("Aktívna") });
                riadky = balik.Miestnosti.Select(m => new string?[] { m.Nazov, m.Budova, m.Poznamka, Aktivny(m.Aktivna) });
                break;
            default:
                zdroj.Hlavicky.AddRange(new[] { Preklad.T("Meno"), Preklad.T("Priezvisko"), Preklad.T("Funkcia"), Preklad.T("Aktívna") });
                riadky = balik.Osoby.Select(o => new string?[] { o.Meno, o.Priezvisko, o.Funkcia, Aktivny(o.Aktivna) });
                break;
        }

        var cislo = 1;
        foreach (var riadok in riadky)
        {
            zdroj.Riadky.Add(riadok);
            zdroj.CislaRiadkov.Add(cislo++);
        }
    }
}

/// <summary>Jeden riadok pri importe číselníka. Pole1–3 podľa číselníka: názov, budova, poznámka / meno, priezvisko, funkcia.</summary>
public sealed partial class RiadokCiselnika : ObservableObject
{
    private static readonly HashSet<string> Systemove = new()
    {
        nameof(Importovat), nameof(Vysledok), nameof(Problem), nameof(VysledokText), nameof(MozeImportovat),
    };

    public RiadokCiselnika(int cisloRiadku)
    {
        CisloRiadku = cisloRiadku;
    }

    public int CisloRiadku { get; }

    [ObservableProperty] private bool _importovat = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VysledokText), nameof(MozeImportovat))]
    private StavRiadku _vysledok;

    [ObservableProperty] private string _problem = "";
    [ObservableProperty] private string? _pole1;
    [ObservableProperty] private string? _pole2;
    [ObservableProperty] private string? _pole3;
    [ObservableProperty] private string? _aktivna;

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

    /// <summary>Text riadku do hlásení (názov alebo meno osoby).</summary>
    public string Popis(TypCiselnika typ) =>
        typ == TypCiselnika.Osoby ? ImportService.UpravMeno($"{Pole1} {Pole2}") : (Pole1 ?? "").Trim();

    public event Action<RiadokCiselnika>? Zmeneny;

    public event Action<RiadokCiselnika>? ZmenenyVyber;

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
}

/// <summary>Import kategórií, miestností alebo osôb zo súboru: príprava, kontrola a zápis.</summary>
public sealed class ImportCiselnikaService
{
    public ImportCiselnikaService(TypCiselnika typ)
    {
        Typ = typ;
    }

    public TypCiselnika Typ { get; }

    /// <summary>Riadky podľa priradenia stĺpcov (viac stĺpcov k jednému poľu sa spojí medzerou).</summary>
    public List<RiadokCiselnika> Priprav(ZdrojImportu zdroj, IReadOnlyList<string?> priradenie)
    {
        var riadky = new List<RiadokCiselnika>(zdroj.Riadky.Count);
        for (var i = 0; i < zdroj.Riadky.Count; i++)
        {
            var hodnoty = zdroj.Riadky[i];
            var polia = new Dictionary<string, string>();
            for (var c = 0; c < priradenie.Count && c < hodnoty.Length; c++)
            {
                if (priradenie[c] is not { } kluc || string.IsNullOrWhiteSpace(hodnoty[c]))
                {
                    continue;
                }

                var hodnota = hodnoty[c]!.Trim();
                polia[kluc] = polia.TryGetValue(kluc, out var doteraz) ? $"{doteraz} {hodnota}" : hodnota;
            }

            if (polia.Count == 0)
            {
                continue;
            }

            var riadok = new RiadokCiselnika(i < zdroj.CislaRiadkov.Count ? zdroj.CislaRiadkov[i] : i + 1)
            {
                Aktivna = polia.GetValueOrDefault(PolaCiselnika.Aktivna),
            };

            if (Typ == TypCiselnika.Osoby)
            {
                var meno = polia.GetValueOrDefault(PolaCiselnika.Meno);
                var priezvisko = polia.GetValueOrDefault(PolaCiselnika.Priezvisko);
                if (string.IsNullOrWhiteSpace(priezvisko) && polia.GetValueOrDefault(PolaCiselnika.CeleMeno) is { } cele)
                {
                    // „Meno Priezvisko“ – posledné slovo je priezvisko
                    var slova = cele.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    priezvisko = slova.Length > 0 ? slova[^1] : "";
                    if (string.IsNullOrWhiteSpace(meno))
                    {
                        meno = string.Join(' ', slova.Take(slova.Length - 1));
                    }
                }

                riadok.Pole1 = meno;
                riadok.Pole2 = priezvisko;
                riadok.Pole3 = polia.GetValueOrDefault(PolaCiselnika.Funkcia);
            }
            else
            {
                riadok.Pole1 = polia.GetValueOrDefault(PolaCiselnika.Nazov);
                riadok.Pole2 = polia.GetValueOrDefault(PolaCiselnika.Budova);
                riadok.Pole3 = polia.GetValueOrDefault(PolaCiselnika.Poznamka);
            }

            riadky.Add(riadok);
        }

        return riadky;
    }

    /// <summary>Existujúce záznamy číselníka (názvy, pri osobách „Meno Priezvisko“ aj „Priezvisko Meno“).</summary>
    public HashSet<string> Existujuce()
    {
        using var db = Databaza.Otvor();
        var mnozina = new HashSet<string>(Sk.Porovnanie);
        switch (Typ)
        {
            case TypCiselnika.Kategorie:
                mnozina.UnionWith(db.Kategorie.Select(k => k.Nazov).ToList().Select(n => n.Trim()));
                break;
            case TypCiselnika.Miestnosti:
                mnozina.UnionWith(db.Miestnosti.Select(m => m.Nazov).ToList().Select(n => n.Trim()));
                break;
            default:
                foreach (var o in db.Osoby.Select(o => new { o.Meno, o.Priezvisko }).ToList())
                {
                    mnozina.Add(ImportService.UpravMeno($"{o.Meno} {o.Priezvisko}"));
                    mnozina.Add(ImportService.UpravMeno($"{o.Priezvisko} {o.Meno}"));
                }

                break;
        }

        return mnozina;
    }

    /// <summary>Hodnota stĺpca Aktívna: áno/nie vo všetkých jazykoch, 1/0, true/false; prázdna = áno.</summary>
    public static bool? Aktivny(string? text)
    {
        var t = StlpcePrenosu.Normalizuj(text);
        if (t.Length == 0)
        {
            return true;
        }

        var ano = new HashSet<string> { "ano", "a", "yes", "y", "true", "1", "x", "aktivna", "aktivny", "active" };
        var nie = new HashSet<string> { "nie", "n", "no", "false", "0", "neaktivna", "neaktivny", "inactive" };
        foreach (var jazyk in Preklad.Jazyky.Where(j => j.Kod != "sk"))
        {
            var slovnik = Preklad.NacitajSlovnik(jazyk.Kod);
            if (slovnik.TryGetValue("áno", out var a))
            {
                ano.Add(StlpcePrenosu.Normalizuj(a));
            }

            if (slovnik.TryGetValue("nie", out var n))
            {
                nie.Add(StlpcePrenosu.Normalizuj(n));
            }
        }

        return ano.Contains(t) ? true : nie.Contains(t) ? false : null;
    }

    /// <summary>Skontroluje všetky riadky: povinné údaje, záznamy už v číselníku a opakovanie v súbore.</summary>
    public void SkontrolujVsetky(IReadOnlyList<RiadokCiselnika> riadky, HashSet<string> existujuce)
    {
        var vSubore = new Dictionary<string, int>(Sk.Porovnanie);
        foreach (var r in riadky)
        {
            var predtym = r.MozeImportovat;
            var problemy = new List<string>();
            var stav = StavRiadku.Ok;
            var kluc = r.Popis(Typ);

            if (Typ == TypCiselnika.Osoby ? string.IsNullOrWhiteSpace(r.Pole2) : kluc.Length == 0)
            {
                stav = StavRiadku.Chyba;
                problemy.Add(Typ == TypCiselnika.Osoby ? Preklad.T("chýba priezvisko") : Preklad.T("chýba názov"));
            }
            else if (existujuce.Contains(kluc))
            {
                stav = StavRiadku.Duplicita;
                problemy.Add(Preklad.T("už je v evidencii"));
            }
            else if (vSubore.TryGetValue(kluc, out var prvy))
            {
                stav = StavRiadku.Duplicita;
                problemy.Add(Preklad.F($"rovnaký záznam je v súbore už v riadku {prvy}"));
            }
            else
            {
                vSubore[kluc] = r.CisloRiadku;
                if (Typ == TypCiselnika.Osoby)
                {
                    vSubore.TryAdd(ImportService.UpravMeno($"{r.Pole2} {r.Pole1}"), r.CisloRiadku);
                }
            }

            if (stav == StavRiadku.Ok && Aktivny(r.Aktivna) is null)
            {
                stav = StavRiadku.Upozornenie;
                problemy.Add(Preklad.F($"nerozpoznaná hodnota „{r.Aktivna}“ v stĺpci Aktívna – záznam bude aktívny"));
            }

            r.Vysledok = stav;
            r.Problem = string.Join("; ", problemy);
            if (!r.Skontrolovany || (!predtym && r.MozeImportovat))
            {
                r.Importovat = r.MozeImportovat;
            }
            else if (!r.MozeImportovat)
            {
                r.Importovat = false;
            }

            r.Skontrolovany = true;
        }
    }

    /// <summary>Zapíše vybrané riadky. Pred zápisom urobí zálohu, aby sa import dal vrátiť.</summary>
    public VysledokImportu Importuj(ZdrojImportu zdroj, IReadOnlyList<RiadokCiselnika> riadky)
    {
        var vybrane = riadky.Where(r => r.Importovat && r.MozeImportovat).ToList();
        if (vybrane.Count == 0)
        {
            throw new EvitaException(Preklad.T("Nie je vybraná žiadna položka na import."));
        }

        var nazovSuboru = zdroj.NazovSuboru;
        var zaloha = new ZalohaService().Zalohuj(TypZalohy.PredImportom, poznamka: Preklad.F($"Automatická záloha pred importom zo súboru {nazovSuboru}"));
        var teraz = DateTime.Now;
        var existujuce = Existujuce();
        var pridane = new List<string>();
        var preskocene = new List<string>();

        using var db = Databaza.Otvor();
        using var transakcia = db.Database.BeginTransaction();
        foreach (var r in riadky)
        {
            var popis = r.Popis(Typ);
            if (!(r.Importovat && r.MozeImportovat))
            {
                preskocene.Add(Preklad.F($"riadok {r.CisloRiadku}: {popis} – {(r.Problem.Length > 0 ? r.Problem : Preklad.T("vynechané ručne"))}"));
                continue;
            }

            if (!existujuce.Add(popis))
            {
                preskocene.Add(Preklad.F($"riadok {r.CisloRiadku}: {popis} – {Preklad.T("už je v evidencii")}"));
                continue;
            }

            var aktivny = Aktivny(r.Aktivna) ?? true;
            switch (Typ)
            {
                case TypCiselnika.Kategorie:
                    db.Kategorie.Add(new Kategoria { Nazov = popis, Aktivna = aktivny });
                    break;
                case TypCiselnika.Miestnosti:
                    db.Miestnosti.Add(new Miestnost { Nazov = popis, Budova = Sk.Prazdne(r.Pole2), Poznamka = Sk.Prazdne(r.Pole3), Aktivna = aktivny });
                    break;
                default:
                    var osoba = new Osoba { Meno = (r.Pole1 ?? "").Trim(), Priezvisko = (r.Pole2 ?? "").Trim(), Funkcia = Sk.Prazdne(r.Pole3), Aktivna = aktivny };
                    db.Osoby.Add(osoba);
                    existujuce.Add(ImportService.UpravMeno($"{osoba.Priezvisko} {osoba.Meno}"));
                    break;
            }

            pridane.Add(popis);
        }

        var nazovCiselnika = PolaCiselnika.NazovCiselnika(Typ);
        var pocet = PocetZaznamov(pridane.Count);
        Databaza.Zapis(db, ImportService.KlucPoslednaZaloha, zaloha);
        Databaza.Zapis(db, ImportService.KlucPoslednyPopis,
            Preklad.F($"{Sk.DatumCas(teraz)} – súbor {nazovSuboru}, pridané {pocet} ({nazovCiselnika})"));
        db.SaveChanges();
        transakcia.Commit();

        var sb = new StringBuilder();
        sb.AppendLine(Preklad.T("EviTA – protokol importu"));
        sb.AppendLine(new string('=', 60));
        sb.AppendLine(Preklad.F($"Čas:                 {Sk.DatumCas(teraz)}"));
        sb.AppendLine(Preklad.F($"Súbor:               {zdroj.Cesta}"));
        sb.AppendLine(Preklad.F($"Druh importu:        {Preklad.F($"doplnenie číselníka {nazovCiselnika}")}"));
        sb.AppendLine(Preklad.F($"Záloha pred importom: {zaloha}"));
        sb.AppendLine(Preklad.F($"Riadkov v súbore:    {riadky.Count}"));
        sb.AppendLine(Preklad.F($"Pridané:             {pocet}"));
        sb.AppendLine(Preklad.F($"Preskočené:          {preskocene.Count}"));
        if (preskocene.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Preklad.T("Preskočené riadky"));
            sb.AppendLine(new string('-', 60));
            foreach (var p in preskocene)
            {
                sb.AppendLine(p);
            }
        }

        sb.AppendLine();
        sb.AppendLine(Preklad.T("Pridané záznamy"));
        sb.AppendLine(new string('-', 60));
        foreach (var p in pridane)
        {
            sb.AppendLine(p);
        }

        return new VysledokImportu(pridane.Count, preskocene.Count, zaloha, sb.ToString(), null, null);
    }

    public static string PocetZaznamov(int pocet) => Preklad.Pocet(pocet, "{0} záznam", "{0} záznamy", "{0} záznamov");
}
