using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Models;
using Evita.Services;
using Kalendarium;

namespace Evita.ViewModels;

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

    private void NacitajPanely()
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
