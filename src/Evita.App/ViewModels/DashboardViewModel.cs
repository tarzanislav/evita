using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Models;
using Evita.Services;

namespace Evita.ViewModels;

public partial class DashboardViewModel : ObservableObject, IObnovitelny
{
    private readonly MainViewModel _hlavne;
    private readonly DispatcherTimer _hodiny;

    public DashboardViewModel(MainViewModel hlavne)
    {
        _hlavne = hlavne;
        _hodiny = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _hodiny.Tick += (_, _) => AktualizujCas();
        _hodiny.Start();
        AktualizujCas();
    }

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

    [ObservableProperty] private List<PolozkaRiadok> _naposledyPridane = new();
    [ObservableProperty] private List<PolozkaRiadok> _naposledyZmenene = new();

    public void Obnov()
    {
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
        ZiadneUpozornenia = BezMiestnosti == 0 && ZarukyDo30 == 0 && RevizieDo30 == 0 && VOprave == 0;

        var riadky = new PolozkaService().NacitajRiadky();
        NaposledyPridane = riadky.OrderByDescending(r => r.DatumPridania).ThenByDescending(r => r.PrirastkoveCislo).Take(5).ToList();
        NaposledyZmenene = riadky.Where(r => r.DatumZmeny > r.DatumPridania)
            .OrderByDescending(r => r.DatumZmeny).Take(5).ToList();
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
    }
}
