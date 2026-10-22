using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;

namespace Evita.ViewModels;

/// <summary>Blok štatistiky (karta s riadkami).</summary>
public sealed record BlokStatistiky(string Kluc, string Nadpis, string Popis, string Ikona, IReadOnlyList<RiadokStatistiky> Riadky, string? Suhrn = null)
{
    public bool JePrazdny => Riadky.Count == 0;
}

/// <summary>Časť štatistiky na výber do tlače (súhrn alebo blok).</summary>
public sealed partial class CastTlaceStatistiky : ObservableObject
{
    public CastTlaceStatistiky(string kluc, string nazov, bool vybrana)
    {
        Kluc = kluc;
        _nazov = nazov;
        _vybrana = vybrana;
    }

    public string Kluc { get; }
    [ObservableProperty] private string _nazov;
    [ObservableProperty] private bool _vybrana;
}

/// <summary>Dlaždica súhrnu (číslo s popisom).</summary>
public sealed record DlazdicaStatistiky(string Nazov, string Hodnota, string Popis, bool Upozornit = false);

/// <summary>Riadok v podokne s položkami.</summary>
public sealed record PolozkaPodokna(int Id, int Cislo, string Nazov, string Kategoria, string Miestnost, string Nakup, string Vek, string Cena, string Poznamka);

/// <summary>Obrazovka Štatistika: súhrn, bloky (kategórie, obnova, plán obnovy, miestnosti …) a podokno s položkami a sumami.</summary>
public partial class StatistikaViewModel : ObservableObject, IObnovitelny
{
    private readonly MainViewModel _hlavne;
    private bool _nacitavam;

    /// <summary>Kľúče častí vybraných do tlače (oddelené čiarkou; prázdne = všetky).</summary>
    public const string KlucCastiTlace = "Statistika.TlacCasti";

    public StatistikaViewModel(MainViewModel hlavne)
    {
        _hlavne = hlavne;
        _hranica = StatistikaEvidencie.NacitajHranicu();
        string? ulozene = null;
        try
        {
            using var db = Data.Databaza.Otvor();
            ulozene = Data.Databaza.Citaj(db, KlucCastiTlace);
        }
        catch (Exception)
        {
            // predvolene sa tlačí všetko
        }

        var vybrane = string.IsNullOrWhiteSpace(ulozene) ? null : ulozene.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        CastiTlace = StatistikaZostava.Casti(_hranica)
            .Select(c => new CastTlaceStatistiky(c.Kluc, c.Nazov, vybrane is null || vybrane.Contains(c.Kluc)))
            .ToList();
    }

    /// <summary>Výber častí do tlače (súhrn a bloky).</summary>
    public IReadOnlyList<CastTlaceStatistiky> CastiTlace { get; }

    /// <summary>Otvorená ponuka „Čo tlačiť“.</summary>
    [ObservableProperty] private bool _vyberTlaceOtvoreny;

    public IReadOnlyList<int> HraniceVolby { get; } = new[] { 3, 4, 5, 6, 7, 8, 10 };

    [ObservableProperty] private StatistikaEvidencie? _statistika;
    [ObservableProperty] private IReadOnlyList<DlazdicaStatistiky> _dlazdice = Array.Empty<DlazdicaStatistiky>();
    [ObservableProperty] private IReadOnlyList<BlokStatistiky> _bloky = Array.Empty<BlokStatistiky>();

    /// <summary>Počítať aj vyradené položky.</summary>
    [ObservableProperty] private bool _aVyradene;

    /// <summary>Od koľkých rokov je položka kandidát na obnovu (predvolene 5).</summary>
    [ObservableProperty] private int _hranica;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JeVybrany))]
    private RiadokStatistiky? _vybrany;

    [ObservableProperty] private string _detailNadpis = "";
    [ObservableProperty] private string _detailSuhrn = "";
    [ObservableProperty] private IReadOnlyList<PolozkaPodokna> _detailPolozky = Array.Empty<PolozkaPodokna>();

    public bool JeVybrany => Vybrany is not null;

    partial void OnAVyradeneChanged(bool value)
    {
        if (!_nacitavam)
        {
            Obnov();
        }
    }

    partial void OnHranicaChanged(int value)
    {
        if (_nacitavam)
        {
            return;
        }

        try
        {
            StatistikaEvidencie.UlozHranicu(value);
        }
        catch (Exception)
        {
            // hranica sa neuloží, štatistika sa aj tak prepočíta
        }

        Obnov();
    }

    public void Obnov()
    {
        try
        {
            var s = StatistikaEvidencie.Vypocitaj(StatistikaEvidencie.NacitajPolozky(AVyradene), DateTime.Today, Hranica);
            Statistika = s;
            foreach (var (kluc, nazov) in StatistikaZostava.Casti(Hranica))
            {
                if (CastiTlace.FirstOrDefault(c => c.Kluc == kluc) is { } cast)
                {
                    cast.Nazov = nazov;
                }
            }

            var stare = s.Stare;
            var bezDatumu = s.Polozky.Count(p => p.DatumNakupu is null);
            var bezCeny = s.Polozky.Count(p => p.Cena is null);
            Dlazdice = new[]
            {
                new DlazdicaStatistiky(Preklad.T("Položky"), s.Spolu.ToString("N0", Sk.Kultura),
                    AVyradene ? Preklad.T("vrátane vyradených") : Preklad.T("v evidencii (bez vyradených)")),
                new DlazdicaStatistiky(Preklad.T("Hodnota"), Sk.Suma(s.Hodnota),
                    bezCeny > 0 ? Preklad.F($"súčet nákupných cien · {bezCeny} bez ceny") : Preklad.T("súčet nákupných cien")),
                new DlazdicaStatistiky(Preklad.T("Priemerný vek"), s.PriemernyVek is { } v ? Preklad.F($"{v:0.0} roka") : "–",
                    bezDatumu > 0 ? Preklad.F($"z dátumu nákupu · {bezDatumu} bez dátumu") : Preklad.T("z dátumu nákupu")),
                new DlazdicaStatistiky(Preklad.F($"Na obnovu ({Hranica}+ rokov)"), stare.Count.ToString("N0", Sk.Kultura),
                    Preklad.F($"{(s.Spolu > 0 ? 100.0 * stare.Count / s.Spolu : 0):0} % položiek · {Sk.Suma(stare.Sum(p => p.Cena ?? 0))}"), stare.Count > 0),
                new DlazdicaStatistiky(Preklad.T("Záruka do 90 dní"), s.ZarukyRevizie[0].Pocet.ToString("N0", Sk.Kultura),
                    Preklad.F($"revízia po termíne: {s.ZarukyRevizie[3].Pocet}"), s.ZarukyRevizie[0].Pocet + s.ZarukyRevizie[3].Pocet > 0),
            };

            string Suhrn(IEnumerable<RiadokStatistiky> riadky) =>
                Preklad.F($"spolu {riadky.Sum(r => r.Pocet)} ks · {Sk.Suma(riadky.Sum(r => r.Suma))}");
            Bloky = new[]
            {
                new BlokStatistiky("kategorie", Preklad.T("Kategórie"),
                    Preklad.T("Koľko kusov a akú hodnotu má každá kategória. Kliknutím zobrazíte položky s cenami."), "", s.Kategorie, Suhrn(s.Kategorie)),
                new BlokStatistiky("vek", Preklad.T("Obnova – vek techniky"),
                    Preklad.F($"Položky podľa veku z dátumu nákupu. Od {Hranica} rokov sú kandidátmi na obnovu (zvýraznené)."), "", s.VekoveSkupiny),
                new BlokStatistiky("stare", Preklad.F($"Obnova – {Hranica}+ ročné podľa kategórií"),
                    Preklad.T("Čo treba obnoviť a za koľko boli položky kúpené. Percento = podiel starých kusov v kategórii."), "", s.StareKategorie, Suhrn(s.StareKategorie)),
                new BlokStatistiky("plan", Preklad.T("Plán obnovy na 5 rokov"),
                    Preklad.F($"V ktorom roku položky dosiahnu {Hranica} rokov – odhad rozpočtu podľa pôvodných nákupných cien."), "", s.PlanObnovy, Suhrn(s.PlanObnovy)),
                new BlokStatistiky("miestnosti", Preklad.T("Miestnosti"),
                    Preklad.F($"Kusy a hodnota v miestnosti, koľko je {Hranica}+ ročných, v oprave a ktorá kategória prevláda."), "", s.Miestnosti),
                new BlokStatistiky("stav", Preklad.T("Stav"), Preklad.T("V prevádzke, v sklade, v oprave (a vyradené, ak sú zapnuté)."), "", s.Stavy),
                new BlokStatistiky("zaruky", Preklad.T("Záruky a revízie"), Preklad.T("Čo končí v záruke, čo je po záruke a ktoré revízie treba urobiť."), "", s.ZarukyRevizie),
                new BlokStatistiky("nakupy", Preklad.T("Nákupy po rokoch"), Preklad.T("Koľko a za koľko sa kúpilo v jednotlivých rokoch (podľa dátumu nákupu)."), "", s.Nakupy, Suhrn(s.Nakupy)),
                new BlokStatistiky("osoby", Preklad.T("Pridelené osobám"), Preklad.T("Kto má pridelenú najviac techniky (podľa hodnoty)."), "", s.Osoby),
                new BlokStatistiky("kvalita", Preklad.T("Úplnosť údajov"), Preklad.T("Chýbajúce údaje skresľujú štatistiku – kliknite a doplňte ich v Evidencii."), "", s.Kvalita),
            };

            if (Vybrany is { } vybrany)
            {
                Vybrat(Bloky.SelectMany(b => b.Riadky).FirstOrDefault(r => r.Nazov == vybrany.Nazov));
            }
        }
        catch (Exception ex)
        {
            Dlazdice = new[] { new DlazdicaStatistiky(Preklad.T("Chyba"), "–", ex.GetBaseException().Message, true) };
            Bloky = Array.Empty<BlokStatistiky>();
        }
    }

    /// <summary>Podokno s položkami vybraného riadku (ceny, dátum nákupu, vek).</summary>
    [RelayCommand]
    public void Vybrat(RiadokStatistiky? riadok)
    {
        Vybrany = riadok;
        if (riadok is null || Statistika is not { } s)
        {
            DetailPolozky = Array.Empty<PolozkaPodokna>();
            return;
        }

        var blok = Bloky.FirstOrDefault(b => b.Riadky.Contains(riadok));
        DetailNadpis = blok is null ? riadok.Nazov : $"{blok.Nadpis}: {riadok.Nazov}";
        DetailSuhrn = Preklad.F($"{riadok.Pocet} ks · spolu {Sk.Suma(riadok.Suma)}")
                      + (riadok.BezCeny > 0 ? " · " + Preklad.F($"{riadok.BezCeny} bez ceny") : "")
                      + (riadok.Pocet > riadok.BezCeny ? " · " + Preklad.F($"priemer {Sk.Suma(riadok.Suma / (riadok.Pocet - riadok.BezCeny))}") : "");
        // poznámka: pri zárukách a revíziách termín, inak stav (ak nie je v prevádzke) a pridelenie
        var zaruka = blok?.Kluc == "zaruky" && blok.Riadky.ToList().IndexOf(riadok) < 3;
        var revizia = blok?.Kluc == "zaruky" && !zaruka;
        string Poznamka(PolozkaStatistiky p) => zaruka
            ? Preklad.F($"záruka do {Sk.Datum(p.ZarukaDo)}")
            : revizia
                ? Preklad.F($"revízia {Sk.Datum(p.DalsiaRevizia)}")
                : string.Join(" · ", new[] { p.Stav != Models.StavPolozky.VPrevadzke ? Models.StavyPoloziek.Nazov(p.Stav) : null, p.Osoba?.Trim() }
                    .Where(t => !string.IsNullOrEmpty(t)));
        DetailPolozky = riadok.Polozky.Select(p => new PolozkaPodokna(p.Id, p.Cislo, p.Nazov, p.Kategoria, p.Miestnost ?? "", Sk.Datum(p.DatumNakupu),
                p.VekText(s.Dnes), Sk.Suma(p.Cena), Poznamka(p)))
            .ToList();
    }

    [RelayCommand]
    private void ZavrietPodokno() => Vybrat(null);

    [RelayCommand]
    private void Prepocitat() => Obnov();

    /// <summary>Položky z podokna v Evidencii (napr. na doplnenie ceny alebo hromadnú zmenu).</summary>
    [RelayCommand]
    private void ZobrazitVEvidencii()
    {
        if (Vybrany is not { } riadok)
        {
            return;
        }

        _hlavne.PrejdiNaSekciu("evidencia");
        _hlavne.Evidencia.ZobrazPolozky(riadok.Polozky.Select(p => p.Id), DetailNadpis);
    }

    /// <summary>Ponuka „Čo tlačiť“ – súhrn a jednotlivé bloky.</summary>
    [RelayCommand]
    private void OtvoritVyberTlace() => VyberTlaceOtvoreny = true;

    [RelayCommand]
    private void VsetkyCasti()
    {
        foreach (var c in CastiTlace)
        {
            c.Vybrana = true;
        }
    }

    [RelayCommand]
    private void ZiadneCasti()
    {
        foreach (var c in CastiTlace)
        {
            c.Vybrana = false;
        }
    }

    /// <summary>Vybrané časti štatistiky ako zostava (náhľad, tlač, PDF vrátane PDF/A-3b s dátami v Exceli).</summary>
    [RelayCommand]
    private void Tlacit()
    {
        var vybrane = CastiTlace.Where(c => c.Vybrana).Select(c => c.Kluc).ToList();
        if (vybrane.Count == 0)
        {
            Dialogy.Info(Preklad.T("Vyberte aspoň jednu časť štatistiky na tlač."));
            return;
        }

        VyberTlaceOtvoreny = false;
        try
        {
            using var db = Data.Databaza.Otvor();
            Data.Databaza.Zapis(db, KlucCastiTlace, vybrane.Count == CastiTlace.Count ? "" : string.Join(",", vybrane));
            db.SaveChanges();
        }
        catch (Exception)
        {
            // výber sa nezapamätá, tlač pokračuje
        }

        Zostava(vybrane.Count == CastiTlace.Count ? null : vybrane);
    }

    /// <summary>Jeden blok štatistiky samostatne (tlačidlo tlačiarne na karte bloku).</summary>
    [RelayCommand]
    private void TlacitBlok(string? kluc)
    {
        if (!string.IsNullOrEmpty(kluc))
        {
            Zostava(new[] { kluc });
        }
    }

    private void Zostava(IReadOnlyCollection<string>? casti)
    {
        if (Statistika is not { } s)
        {
            return;
        }

        var aVyradene = AVyradene;
        var nazov = casti is { Count: 1 } && CastiTlace.FirstOrDefault(c => casti.Contains(c.Kluc)) is { } jedna
            ? Preklad.T("Štatistika evidencie") + " – " + jedna.Nazov
            : Preklad.T("Štatistika evidencie");
        var subor = casti is { Count: 1 } ? $"Statistika_EviTA_{casti.First()}_{DateTime.Today:yyyy-MM-dd}.pdf" : $"Statistika_EviTA_{DateTime.Today:yyyy-MM-dd}.pdf";
        var okno = new NahladWindow(() => StatistikaZostava.Vytvor(StatistikaEvidencie.Vypocitaj(StatistikaEvidencie.NacitajPolozky(aVyradene), DateTime.Today, s.Hranica), aVyradene, casti),
            false, nazov, subor) { Owner = Dialogy.Vlastnik };
        if (Dialogy.TichyRezim)
        {
            okno.Show();
            Posledny = okno;
            return;
        }

        okno.ShowDialog();
    }

    /// <summary>Zoznam položiek z podokna ako zostava.</summary>
    [RelayCommand]
    private void TlacitPodokno()
    {
        if (Vybrany is not { } riadok || Statistika is not { } s)
        {
            return;
        }

        var nadpis = DetailNadpis;
        var okno = new NahladWindow(() => StatistikaZostava.Zoznam(nadpis, riadok, s.Dnes), false, nadpis,
            $"Statistika_EviTA_{DateTime.Today:yyyy-MM-dd}.pdf") { Owner = Dialogy.Vlastnik };
        if (Dialogy.TichyRezim)
        {
            okno.Show();
            Posledny = okno;
            return;
        }

        okno.ShowDialog();
    }

    /// <summary>Samotest: posledné okno náhľadu.</summary>
    public NahladWindow? Posledny { get; private set; }

    /// <summary>Samotest: nastaví voľby bez prepočítavania po každej zmene.</summary>
    public void Nastav(bool aVyradene, int hranica)
    {
        _nacitavam = true;
        AVyradene = aVyradene;
        Hranica = hranica;
        _nacitavam = false;
        Obnov();
    }
}
