using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Models;
using Evita.Services;
using Kalendarium;

namespace Evita.ViewModels;

/// <summary>Deň v malom kalendári na dashboarde.</summary>
public sealed record MiniDen(DateTime Datum, bool MimoMesiaca, bool Dnes, bool Vikend, bool Volno, bool MaUdalost)
{
    public int Cislo => Datum.Day;
}

/// <summary>Jeden stĺpec grafu „položky podľa kategórií“.</summary>
public sealed record StlpecGrafu(int KategoriaId, string Nazov, int Pocet, int Maximum);

public partial class DashboardViewModel : ObservableObject, IObnovitelny
{
    private readonly MainViewModel _hlavne;
    private readonly DispatcherTimer _hodiny;
    private DateTime _zobrazenyDen;
    private bool _nacitavamPanely;

    public DashboardViewModel(MainViewModel hlavne)
    {
        _hlavne = hlavne;
        _hodiny = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _hodiny.Tick += (_, _) => AktualizujCas();
        _hodiny.Start();
        AktualizujCas();
        NacitajPanely();
    }

    // dnešný deň – meniny a sviatky
    [ObservableProperty] private string _meninyDnes = "";
    [ObservableProperty] private string _meninyZajtra = "";
    [ObservableProperty] private string _meninyOsob = "";
    [ObservableProperty] private string _sviatokDnes = "";
    [ObservableProperty] private bool _jeDnesVolno;
    [ObservableProperty] private string _najblizsiVolnyDen = "";

    // graf
    [ObservableProperty] private List<StlpecGrafu> _graf = new();

    // o databáze
    [ObservableProperty] private string _velkostDatabazy = "";
    [ObservableProperty] private string _poslednaKontrola = "";
    [ObservableProperty] private string _kalendariumText = "";
    public string VerziaProgramu => $"EviTA {AktualizaciaService.AktualnaVerzia}, verzia dátovej štruktúry {Databaza.VerziaSchemy}";

    // ktoré panely sú zobrazené
    [ObservableProperty] private bool _ukazDnes = true;
    [ObservableProperty] private bool _ukazPocty = true;
    [ObservableProperty] private bool _ukazGraf = true;
    [ObservableProperty] private bool _ukazUpozornenia = true;
    [ObservableProperty] private bool _ukazNaposledy = true;
    [ObservableProperty] private bool _ukazDatabazu = true;
    [ObservableProperty] private bool _ukazKalendar = true;
    [ObservableProperty] private bool _ukazUlohy = true;
    [ObservableProperty] private bool _ukazPocasie = true;
    [ObservableProperty] private bool _ukazKurzy = true;
    [ObservableProperty] private bool _ukazPoznamky = true;

    partial void OnUkazKalendarChanged(bool value) => UlozPanely();
    partial void OnUkazUlohyChanged(bool value) => UlozPanely();
    partial void OnUkazPocasieChanged(bool value) => UlozPanely();
    partial void OnUkazKurzyChanged(bool value) => UlozPanely();
    partial void OnUkazPoznamkyChanged(bool value) => UlozPanely();

    // úlohy (To Do)
    [ObservableProperty] private List<UlohaRiadok> _ulohy = new();
    [ObservableProperty] private string _ulohySuhrn = "";
    [ObservableProperty] private string _novaUloha = "";
    [ObservableProperty] private string _dalsieUlohy = "";
    private const int UlohNaPaneli = 7;

    // poznámky
    [ObservableProperty] private List<PoznamkaRiadok> _pripnutePoznamky = new();

    // počasie
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JePocasie), nameof(PocasieAktualizovane))]
    private Pocasie? _pocasie;

    [ObservableProperty] private bool _jeMiestoPocasia;
    [ObservableProperty] private string _pocasieStav = "";
    public bool JePocasie => Pocasie is not null;
    public string PocasieAktualizovane => Pocasie is null ? "" : Preklad.F($"{PocasieService.Zdroj} · aktualizované {Sk.Cas(Pocasie.Stiahnute)}");

    // kurzy
    [ObservableProperty] private List<KurzMeny> _kurzy = new();
    [ObservableProperty] private string _kurzyDatum = "";
    [ObservableProperty] private string _kurzyStav = "";
    [ObservableProperty] private string _sumaPrepoctu = "100";
    [ObservableProperty] private string _menaZ = "EUR";
    [ObservableProperty] private string _menaNa = "USD";
    [ObservableProperty] private string _vysledokPrepoctu = "";
    public List<string> MenyPrepoctu { get; } = new[] { "EUR" }.Concat(KurzyService.VsetkyMeny).ToList();

    partial void OnSumaPrepoctuChanged(string value) => Prepocitaj();
    partial void OnMenaZChanged(string value) => Prepocitaj();
    partial void OnMenaNaChanged(string value) => Prepocitaj();

    // kalendár
    [ObservableProperty] private List<MiniDen> _miniKalendar = new();
    [ObservableProperty] private string _nazovMesiaca = "";
    [ObservableProperty] private List<VyskytUdalosti> _najblizsieUdalosti = new();
    [ObservableProperty] private List<VyskytUdalosti> _ulohyPoTermine = new();
    [ObservableProperty] private string _kalendarSuhrn = "";

    partial void OnUkazDnesChanged(bool value) => UlozPanely();
    partial void OnUkazPoctyChanged(bool value) => UlozPanely();
    partial void OnUkazGrafChanged(bool value) => UlozPanely();
    partial void OnUkazUpozorneniaChanged(bool value) => UlozPanely();
    partial void OnUkazNaposledyChanged(bool value) => UlozPanely();
    partial void OnUkazDatabazuChanged(bool value) => UlozPanely();

    [ObservableProperty] private string _datumText = "";
    [ObservableProperty] private string _casText = "";
    [ObservableProperty] private string _tyzdenText = "";

    [ObservableProperty] private int _pocetAktivnych;
    [ObservableProperty] private int _pocetVyradenych;
    [ObservableProperty] private int _pocetMiestnosti;
    [ObservableProperty] private int _pocetOsob;
    [ObservableProperty] private int _pocetKategorii;
    [ObservableProperty] private int _pridaneMesiac;
    [ObservableProperty] private int _pridaneRok;

    [ObservableProperty] private int _bezMiestnosti;
    [ObservableProperty] private int _bezPridelenia;
    [ObservableProperty] private int _zarukyDo30;
    [ObservableProperty] private int _revizieDo30;
    [ObservableProperty] private int _vOprave;
    [ObservableProperty] private bool _ziadneUpozornenia;
    [ObservableProperty] private string _zalohaText = "";
    [ObservableProperty] private bool _zalohaVarovanie;

    [ObservableProperty] private List<PolozkaRiadok> _naposledyPridane = new();
    [ObservableProperty] private List<PolozkaRiadok> _naposledyZmenene = new();

    public void Obnov()
    {
        AktualizujDen();
        AktualizujGrafADatabazu();
        var dnes = DateTime.Today;
        var o30 = dnes.AddDays(30);
        var zaciatokMesiaca = new DateTime(dnes.Year, dnes.Month, 1);
        var zaciatokRoka = new DateTime(dnes.Year, 1, 1);

        using var db = Databaza.Otvor();
        var aktivne = db.Polozky.Where(p => p.Stav != StavPolozky.Vyradene);

        PocetAktivnych = aktivne.Count();
        PocetVyradenych = db.Polozky.Count(p => p.Stav == StavPolozky.Vyradene);
        PocetMiestnosti = db.Miestnosti.Count(m => m.Aktivna);
        PocetOsob = db.Osoby.Count(o => o.Aktivna);
        PocetKategorii = db.Kategorie.Count(k => k.Aktivna);
        PridaneMesiac = db.Polozky.Count(p => p.DatumPridania >= zaciatokMesiaca);
        PridaneRok = db.Polozky.Count(p => p.DatumPridania >= zaciatokRoka);

        BezMiestnosti = aktivne.Count(p => p.MiestnostId == null);
        BezPridelenia = aktivne.Count(p => p.OsobaId == null);
        ZarukyDo30 = aktivne.Count(p => p.ZarukaDo != null && p.ZarukaDo >= dnes && p.ZarukaDo <= o30);
        RevizieDo30 = aktivne.Count(p => p.DalsiaRevizia != null && p.DalsiaRevizia <= o30);
        VOprave = aktivne.Count(p => p.Stav == StavPolozky.VOprave);
        AktualizujZalohy();
        ZiadneUpozornenia = BezMiestnosti == 0 && ZarukyDo30 == 0 && RevizieDo30 == 0 && VOprave == 0 && !ZalohaVarovanie;

        AktualizujKalendar();
        AktualizujUlohy();
        AktualizujPoznamky();
        PosledneNacitanieZoSiete = NacitajZoSieteAsync(vynutit: false);

        var riadky = new PolozkaService().NacitajRiadky();
        NaposledyPridane = riadky.OrderByDescending(r => r.DatumPridania).ThenByDescending(r => r.PrirastkoveCislo).Take(5).ToList();
        NaposledyZmenene = riadky.Where(r => r.DatumZmeny > r.DatumPridania)
            .OrderByDescending(r => r.DatumZmeny).Take(5).ToList();
    }

    private void AktualizujZalohy()
    {
        var zalohy = new ZalohaService();
        var nastavenia = zalohy.NacitajNastavenia();
        var posledna = zalohy.PoslednaUspesna();
        var poslednyPokus = zalohy.PoslednyZaznam();

        if (poslednyPokus is { Uspech: false })
        {
            ZalohaText = $"Posledná záloha sa nepodarila ({Sk.DatumCas(poslednyPokus.Cas)}): {poslednyPokus.Chyba}";
            ZalohaVarovanie = true;
        }
        else if (posledna is null)
        {
            ZalohaText = "Zatiaľ nebola urobená žiadna záloha.";
            ZalohaVarovanie = true;
        }
        else
        {
            var dni = (int)(DateTime.Now - posledna.Value).TotalDays;
            var vek = dni switch { 0 => "dnes", 1 => "včera", _ => $"pred {dni} dňami" };
            ZalohaText = $"Posledná záloha: {Sk.DatumCas(posledna.Value)} ({vek})" + (nastavenia.Zapnuta ? "" : ", pravidelná záloha je vypnutá");
            // varovanie, ak je záloha staršia ako nastavená perióda (s rezervou jedného dňa)
            var limit = ZalohaService.DalsiTermin(posledna.Value, nastavenia.Zapnuta ? nastavenia.Perioda : PeriodaZaloh.Mesacne).AddDays(1);
            ZalohaVarovanie = DateTime.Now > limit;
        }
    }

    [RelayCommand]
    private void OtvorServis() => _hlavne.PrejdiNaSekciu("servis");

    [RelayCommand]
    private void OtvorKalendar() => _hlavne.PrejdiNaSekciu("kalendar");

    /// <summary>Malý kalendár mesiaca, udalosti na dnes a 7 dní dopredu a úlohy po termíne.</summary>
    private void AktualizujKalendar()
    {
        try
        {
            var dnes = DateTime.Today;
            var prvy = new DateTime(dnes.Year, dnes.Month, 1);
            var od = prvy.AddDays(-(((int)prvy.DayOfWeek + 6) % 7));
            var sluzba = new KalendarService();
            var vyskyty = sluzba.Vyskyty(od, od.AddDays(41));
            var volne = new HashSet<DateOnly>();
            try
            {
                foreach (var rok in new[] { od.Year, od.AddDays(41).Year }.Distinct())
                {
                    volne.UnionWith(KalendariumDb.Spolocna.Sviatky(rok).Where(s => s.DenPracovnehoPokoja).Select(s => s.Datum));
                }
            }
            catch (Exception)
            {
                // bez sviatkov
            }

            MiniKalendar = Enumerable.Range(0, 42).Select(i =>
            {
                var d = od.AddDays(i);
                return new MiniDen(d, d.Month != dnes.Month, d == dnes, d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                    volne.Contains(DateOnly.FromDateTime(d)),
                    vyskyty.Any(v => v.Stav != StavUdalosti.Zrusene && v.Zaciatok.Date <= d && v.PoslednyDen >= d));
            }).ToList();
            var nazov = dnes.ToString("MMMM yyyy", Sk.Kultura);
            NazovMesiaca = char.ToUpper(nazov[0], Sk.Kultura) + nazov[1..];

            NajblizsieUdalosti = sluzba.Vyskyty(dnes, dnes.AddDays(7))
                .Where(v => v.Stav == StavUdalosti.Naplanovane && v.PoslednyDen >= dnes)
                .Take(8)
                .ToList();
            UlohyPoTermine = sluzba.Prehlad(dnes).PoTermine;
            var pocet = sluzba.Vyskyty(dnes, dnes.AddDays(7)).Count(v => v.Stav == StavUdalosti.Naplanovane);
            KalendarSuhrn = pocet switch
            {
                0 => "Na najbližších 7 dní nie je nič naplánované.",
                _ => $"Na dnes a najbližších 7 dní: {pocet}" + (pocet > NajblizsieUdalosti.Count ? $" (zobrazených {NajblizsieUdalosti.Count})" : ""),
            };
        }
        catch (Exception ex)
        {
            KalendarSuhrn = $"Kalendár sa nepodarilo načítať: {ex.GetBaseException().Message}";
        }
    }

    // ------------------------------------------------------------------ úlohy

    private void AktualizujUlohy()
    {
        try
        {
            var sluzba = new UlohyService();
            var otvorene = sluzba.Nacitaj(ajHotove: false);
            Ulohy = otvorene.Take(UlohNaPaneli).ToList();
            var poTermine = otvorene.Count(u => u.PoTermine);
            UlohySuhrn = otvorene.Count == 0
                ? Preklad.T("Žiadne otvorené úlohy.")
                : Preklad.Pocet(otvorene.Count, "{0} otvorená úloha", "{0} otvorené úlohy", "{0} otvorených úloh")
                  + (poTermine > 0 ? " · " + Preklad.F($"po termíne: {poTermine}") : "");
            DalsieUlohy = otvorene.Count > UlohNaPaneli ? Preklad.F($"a ďalšie ({otvorene.Count - UlohNaPaneli})…") : "";
        }
        catch (Exception ex)
        {
            UlohySuhrn = Preklad.F($"Úlohy sa nepodarilo načítať: {ex.GetBaseException().Message}");
        }
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
            new UlohyService().Pridaj(NovaUloha);
            NovaUloha = "";
            AktualizujUlohy();
        }
        catch (EvitaException ex)
        {
            Pomocne.Dialogy.Upozornenie(ex.Message);
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
        AktualizujUlohy();
    }

    [RelayCommand]
    private void OtvorUlohy() => _hlavne.PrejdiNaSekciu("ulohy");

    // ------------------------------------------------------------------ poznámky

    private void AktualizujPoznamky()
    {
        try
        {
            PripnutePoznamky = new PoznamkyService().Pripnute(6);
        }
        catch (Exception)
        {
            PripnutePoznamky = new List<PoznamkaRiadok>();
        }
    }

    [RelayCommand]
    private void OtvorPoznamku(PoznamkaRiadok? poznamka)
    {
        _hlavne.PrejdiNaSekciu("poznamky");
        if (poznamka is not null)
        {
            _hlavne.Poznamky.Vyber(poznamka.Id);
        }
    }

    [RelayCommand]
    private void NovaPoznamka()
    {
        _hlavne.PrejdiNaSekciu("poznamky");
        _hlavne.Poznamky.NovaPoznamkaCommand.Execute(null);
    }

    // ------------------------------------------------------------------ počasie a kurzy (internet)

    /// <summary>Posledné načítanie počasia a kurzov (kvôli automatickému testu).</summary>
    public Task PosledneNacitanieZoSiete { get; private set; } = Task.CompletedTask;

    private bool _nacitavamZoSiete;

    /// <summary>Ukáže počasie a kurzy z vyrovnávacej pamäte a ak sú staré, stiahne nové (na pozadí, bez čakania).</summary>
    public async Task NacitajZoSieteAsync(bool vynutit)
    {
        var pocasieSluzba = new PocasieService();
        var kurzySluzba = new KurzyService();
        var miesto = pocasieSluzba.NacitajMiesto();
        JeMiestoPocasia = miesto is not null;
        Pocasie = miesto is null ? null : pocasieSluzba.ZVyrovnavacejPamate();
        PocasieStav = miesto is null ? Preklad.T("Nastavte mesto alebo obec, pre ktorú sa má zobrazovať počasie.") : Pocasie is null ? Preklad.T("Načítavam počasie…") : "";
        var listok = kurzySluzba.ZVyrovnavacejPamate();
        ZobrazKurzy(listok);
        if (listok is null)
        {
            KurzyStav = Preklad.T("Načítavam kurzy…");
        }

        if (_nacitavamZoSiete)
        {
            return;
        }

        _nacitavamZoSiete = true;
        try
        {
            if (miesto is not null && UkazPocasie && (vynutit || pocasieSluzba.TrebaObnovit(Pocasie)))
            {
                try
                {
                    Pocasie = await pocasieSluzba.StiahniAsync(miesto);
                    PocasieStav = "";
                }
                catch (Exception ex)
                {
                    PocasieStav = Pocasie is null ? Chyba(ex) : Preklad.T("Zobrazené sú posledné stiahnuté údaje – nové sa nepodarilo stiahnuť.");
                }
            }

            if (UkazKurzy && (vynutit || kurzySluzba.TrebaObnovit(listok)))
            {
                try
                {
                    ZobrazKurzy(await kurzySluzba.StiahniAsync());
                }
                catch (Exception ex)
                {
                    KurzyStav = listok is null ? Chyba(ex) : Preklad.T("Zobrazené sú posledné stiahnuté kurzy – nové sa nepodarilo stiahnuť.");
                }
            }
        }
        finally
        {
            _nacitavamZoSiete = false;
        }
    }

    private static string Chyba(Exception ex) => ex is EvitaException ? ex.Message : ex.GetBaseException().Message;

    private void ZobrazKurzy(KurzovyListok? listok)
    {
        Kurzy = listok?.Meny ?? new List<KurzMeny>();
        KurzyDatum = listok is null ? "" : Preklad.F($"ECB · kurzy z {Sk.Datum(listok.Datum)}");
        KurzyStav = listok is not null && listok.Meny.Count == 0 ? Preklad.T("Nie je zvolená žiadna mena – vyberte ich v Nastaveniach.") : "";
        Prepocitaj();
    }

    private void Prepocitaj()
    {
        if (!Sk.SkusPrecitatSumu(SumaPrepoctu, out var suma) || suma is null)
        {
            VysledokPrepoctu = "";
            return;
        }

        var vysledok = KurzyService.Prepocitaj(suma.Value, MenaZ, MenaNa, new KurzyService().PosledneKurzy());
        VysledokPrepoctu = vysledok is { } v ? $"{v.ToString(v >= 1000 ? "N2" : "N4", Sk.Kultura)} {MenaNa}" : "";
    }

    [RelayCommand]
    private void VymenitMeny() => (MenaZ, MenaNa) = (MenaNa, MenaZ);

    [RelayCommand]
    private async Task ObnovitZoSiete() => await NacitajZoSieteAsync(vynutit: true);

    /// <summary>Po zmene miesta v nastaveniach.</summary>
    public void ObnovPocasie() => PosledneNacitanieZoSiete = NacitajZoSieteAsync(vynutit: true);

    /// <summary>Po zmene mien v nastaveniach (stačí prepočítať z vyrovnávacej pamäte).</summary>
    public void ObnovKurzy(bool stiahnut)
    {
        if (stiahnut)
        {
            PosledneNacitanieZoSiete = NacitajZoSieteAsync(vynutit: true);
        }
        else
        {
            ZobrazKurzy(new KurzyService().ZVyrovnavacejPamate());
        }
    }

    [RelayCommand]
    private void OtvorNastavenia() => _hlavne.PrejdiNaSekciu("nastavenia");

    [RelayCommand]
    private void OtvorKategoriu(StlpecGrafu? stlpec)
    {
        if (stlpec is null)
        {
            return;
        }

        _hlavne.PrejdiNaSekciu("evidencia");
        _hlavne.Evidencia.ZobrazKategoriu(stlpec.KategoriaId);
    }

    [RelayCommand]
    private void OtvorSviatky() => _hlavne.PrejdiNaSekciu("sviatky");

    /// <summary>Meniny, sviatky a najbližší voľný deň pre dnešok.</summary>
    private void AktualizujDen()
    {
        var dnes = DateOnly.FromDateTime(DateTime.Today);
        _zobrazenyDen = DateTime.Today;
        try
        {
            var kalendar = KalendariumDb.Spolocna;
            var mena = kalendar.Meniny(dnes);
            MeninyDnes = mena.Count > 0 ? string.Join(", ", mena) : "dnes nemá meniny nikto";
            var zajtra = kalendar.Meniny(dnes.AddDays(1));
            MeninyZajtra = zajtra.Count > 0 ? string.Join(", ", zajtra) : "nikto";

            // osoby z evidencie, ktoré majú dnes meniny
            var hladane = mena.Select(Sk.BezDiakritiky).ToHashSet();
            using (var db = Databaza.Otvor())
            {
                var oslavenci = db.Osoby.Where(o => o.Aktivna).AsEnumerable()
                    .Where(o => hladane.Contains(Sk.BezDiakritiky(o.Meno.Trim())))
                    .Select(o => o.CeleMeno)
                    .ToList();
                MeninyOsob = oslavenci.Count > 0 ? "Dnes má meniny aj: " + string.Join(", ", oslavenci) : "";
            }

            var sviatky = kalendar.SviatkyDna(dnes);
            JeDnesVolno = sviatky.Any(sv => sv.DenPracovnehoPokoja);
            SviatokDnes = string.Join("\n", sviatky.Select(sv =>
                $"{char.ToUpper(sv.DruhText[0])}{sv.DruhText[1..]}: {sv.Nazov}" +
                (sv.Druh != DruhSviatku.PamatnyDen && !sv.DenPracovnehoPokoja ? " (nie je deň pracovného pokoja)" : "")));

            var najblizsi = kalendar.NajblizsiDenPracovnehoPokoja(dnes.AddDays(JeDnesVolno ? 1 : 0));
            if (najblizsi is null)
            {
                NajblizsiVolnyDen = "";
            }
            else
            {
                var dni = najblizsi.Datum.DayNumber - dnes.DayNumber;
                var kedy = dni switch { 0 => "dnes", 1 => "zajtra", _ => $"o {dni} dní" };
                NajblizsiVolnyDen = $"Najbližší deň pracovného pokoja: {najblizsi.Nazov}, " +
                                    $"{najblizsi.Datum.ToString("dddd d. MMMM", Sk.Kultura)} ({kedy})";
            }
        }
        catch (Exception)
        {
            MeninyDnes = "Kalendárium nie je dostupné";
            MeninyZajtra = "";
            MeninyOsob = "";
            SviatokDnes = "";
            NajblizsiVolnyDen = "";
        }
    }

    private void AktualizujGrafADatabazu()
    {
        using (var db = Databaza.Otvor())
        {
            var pocty = db.Polozky.Where(p => p.Stav != StavPolozky.Vyradene)
                .GroupBy(p => p.KategoriaId)
                .Select(g => new { g.Key, Pocet = g.Count() })
                .ToList();
            var nazvy = db.Kategorie.ToDictionary(k => k.Id, k => k.Nazov);
            var maximum = pocty.Count > 0 ? pocty.Max(p => p.Pocet) : 0;
            Graf = pocty
                .Select(p => new StlpecGrafu(p.Key, nazvy.GetValueOrDefault(p.Key, "?"), p.Pocet, maximum))
                .OrderByDescending(p => p.Pocet).ThenBy(p => p.Nazov, Sk.Porovnanie)
                .ToList();

            var kontrola = Databaza.Citaj(db, DiagnostikaService.KlucPoslednaKontrola)?.Split('|');
            PoslednaKontrola = kontrola is { Length: 3 } && DateTime.TryParse(kontrola[0], out var cas)
                ? $"Posledná kontrola: {Sk.DatumCas(cas)} ({kontrola[1].ToLower(Sk.Kultura)}) – {(kontrola[2] == "ok" ? "v poriadku" : "našli sa problémy")}"
                : "Kontrola databázy sa ešte nerobila.";
        }

        var velkost = ServisService.VelkostDatabazy();
        VelkostDatabazy = velkost < 1024 * 1024 ? $"{velkost / 1024.0:0.0} kB" : $"{velkost / 1024.0 / 1024.0:0.0} MB";

        try
        {
            var kalendar = KalendariumDb.Spolocna;
            var rok = DateTime.Today.Year;
            KalendariumText = $"Kalendárium: {kalendar.PocetMien()} mien, {kalendar.Sviatky(rok).Count(sv => sv.Druh != DruhSviatku.PamatnyDen)} sviatkov a " +
                              $"{kalendar.Sviatky(rok).Count(sv => sv.Druh == DruhSviatku.PamatnyDen)} pamätných dní";
        }
        catch (Exception)
        {
            KalendariumText = "Kalendárium nie je dostupné.";
        }
    }

    /// <summary>Načíta, ktoré panely sú zobrazené (aj po importe konfigurácie).</summary>
    public void NacitajPanely()
    {
        _nacitavamPanely = true;
        try
        {
            using var db = Databaza.Otvor();
            var skryte = (Databaza.Citaj(db, "Dashboard.Skryte") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            UkazDnes = !skryte.Contains("dnes");
            UkazPocty = !skryte.Contains("pocty");
            UkazGraf = !skryte.Contains("graf");
            UkazUpozornenia = !skryte.Contains("upozornenia");
            UkazNaposledy = !skryte.Contains("naposledy");
            UkazDatabazu = !skryte.Contains("databaza");
            UkazKalendar = !skryte.Contains("kalendar");
            UkazUlohy = !skryte.Contains("ulohy");
            UkazPocasie = !skryte.Contains("pocasie");
            UkazKurzy = !skryte.Contains("kurzy");
            UkazPoznamky = !skryte.Contains("poznamky");
        }
        catch (Exception)
        {
            // pri chybe zostanú zobrazené všetky panely
        }
        finally
        {
            _nacitavamPanely = false;
        }
    }

    private void UlozPanely()
    {
        if (_nacitavamPanely)
        {
            return;
        }

        var skryte = new List<string>();
        if (!UkazDnes) skryte.Add("dnes");
        if (!UkazPocty) skryte.Add("pocty");
        if (!UkazGraf) skryte.Add("graf");
        if (!UkazUpozornenia) skryte.Add("upozornenia");
        if (!UkazNaposledy) skryte.Add("naposledy");
        if (!UkazDatabazu) skryte.Add("databaza");
        if (!UkazKalendar) skryte.Add("kalendar");
        if (!UkazUlohy) skryte.Add("ulohy");
        if (!UkazPocasie) skryte.Add("pocasie");
        if (!UkazKurzy) skryte.Add("kurzy");
        if (!UkazPoznamky) skryte.Add("poznamky");

        using var db = Databaza.Otvor();
        Databaza.Zapis(db, "Dashboard.Skryte", string.Join(',', skryte));
        db.SaveChanges();
    }

    [RelayCommand]
    private void OtvorEvidenciu(string? filter)
    {
        _hlavne.PrejdiNaSekciu("evidencia");
        _hlavne.Evidencia.PouziRychlyFilter(filter);
    }

    private void AktualizujCas()
    {
        var teraz = DateTime.Now;
        var datum = teraz.ToString("dddd d. MMMM yyyy", Sk.Kultura);
        DatumText = char.ToUpper(datum[0], Sk.Kultura) + datum[1..];
        CasText = teraz.ToString("H:mm:ss", Sk.Kultura);
        TyzdenText = $"{ISOWeek.GetWeekOfYear(teraz)}. týždeň";
        if (_zobrazenyDen != default && _zobrazenyDen != teraz.Date)
        {
            // o polnoci sa zmenia meniny aj sviatky
            AktualizujDen();
        }
    }
}
