using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;
using Kalendarium;
using Microsoft.Win32;

namespace Evita.ViewModels;

public enum RezimKalendara
{
    Mesiac,
    Tyzden,
    Zoznam,
}

/// <summary>Jeden deň v kalendári (bunka mesiaca, stĺpec týždňa alebo deň v zozname).</summary>
public sealed class DenKalendara
{
    public const int NajviacVBunke = 4;

    public DateTime Datum { get; init; }
    public bool MimoMesiaca { get; init; }
    public bool Dnes { get; init; }
    public bool Vikend { get; init; }
    public bool Volno { get; init; }
    public string? Sviatok { get; init; }
    public string Meniny { get; init; } = "";
    public List<VyskytUdalosti> Udalosti { get; init; } = new();
    public List<PoznamkaRiadok> Poznamky { get; init; } = new();

    public int Cislo => Datum.Day;
    public bool MaPoznamky => Poznamky.Count > 0;
    public string PoznamkyTip => string.Join("\n", Poznamky.Select(p => "• " + p.Nazov));
    public string NazovDna => Datum.ToString("dddd", Sk.Kultura);
    public string KratkyNazovDna => Datum.ToString("ddd", Sk.Kultura);
    public string DatumText => Sk.KratkyDatum(Datum);
    public string DlhyDatum => Sk.DlhyDatum(Datum);
    public bool MaSviatok => !string.IsNullOrEmpty(Sviatok);
    public bool Zvyraznit => Volno || Vikend;
    public List<VyskytUdalosti> Zobrazene => Udalosti.Take(NajviacVBunke).ToList();
    public int Dalsie => Math.Max(0, Udalosti.Count - NajviacVBunke);
    public string DalsieText => Dalsie > 0 ? Preklad.F($"+ {Dalsie} ďalšie") : "";
}

public sealed class TyzdenKalendara
{
    public int Cislo { get; init; }
    public List<DenKalendara> Dni { get; init; } = new();
}

/// <summary>Položka legendy farieb.</summary>
public sealed record PolozkaLegendy(string Nazov, string Farba);

/// <summary>Pracovný kalendár: mesiac, týždeň a zoznam udalostí so sviatkami, menami a automatickými termínmi.</summary>
public partial class KalendarViewModel : ObservableObject, IObnovitelny
{
    private const int PocetDniZoznamu = 60;

    private readonly MainViewModel _hlavne;
    private readonly KalendarService _sluzba = new();

    public KalendarViewModel(MainViewModel hlavne)
    {
        _hlavne = hlavne;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JeMesiac), nameof(JeTyzden), nameof(JeZoznam))]
    private RezimKalendara _rezim = RezimKalendara.Mesiac;

    [ObservableProperty] private DateTime _aktualnyDatum = DateTime.Today;
    [ObservableProperty] private string _titulok = "";
    [ObservableProperty] private List<TyzdenKalendara> _tyzdne = new();
    [ObservableProperty] private List<DenKalendara> _dniTyzdna = new();
    [ObservableProperty] private List<DenKalendara> _dniZoznamu = new();
    [ObservableProperty] private List<PolozkaLegendy> _legenda = new();
    [ObservableProperty] private bool _ukazAutomaticke = true;
    [ObservableProperty] private bool _ukazVybavene = true;
    [ObservableProperty] private string _suhrn = "";

    public bool JeMesiac
    {
        get => Rezim == RezimKalendara.Mesiac;
        set
        {
            if (value)
            {
                Rezim = RezimKalendara.Mesiac;
            }
        }
    }

    public bool JeTyzden
    {
        get => Rezim == RezimKalendara.Tyzden;
        set
        {
            if (value)
            {
                Rezim = RezimKalendara.Tyzden;
            }
        }
    }

    public bool JeZoznam
    {
        get => Rezim == RezimKalendara.Zoznam;
        set
        {
            if (value)
            {
                Rezim = RezimKalendara.Zoznam;
            }
        }
    }

    public string[] NazvyDni { get; } = { Preklad.T("Po"), Preklad.T("Ut"), Preklad.T("St"), Preklad.T("Št"), Preklad.T("Pi"), Preklad.T("So"), Preklad.T("Ne") };

    /// <summary>Výber dátumu „skočiť na“.</summary>
    public DateTime? SkocNa
    {
        get => AktualnyDatum;
        set
        {
            if (value is { } datum)
            {
                AktualnyDatum = datum.Date;
            }
        }
    }

    partial void OnRezimChanged(RezimKalendara value) => Obnov();

    partial void OnAktualnyDatumChanged(DateTime value)
    {
        OnPropertyChanged(nameof(SkocNa));
        Obnov();
    }

    partial void OnUkazAutomatickeChanged(bool value) => Obnov();

    partial void OnUkazVybaveneChanged(bool value) => Obnov();

    // ------------------------------------------------------------------ načítanie

    public void Obnov()
    {
        try
        {
            switch (Rezim)
            {
                case RezimKalendara.Mesiac:
                    NacitajMesiac();
                    break;
                case RezimKalendara.Tyzden:
                    NacitajTyzden();
                    break;
                default:
                    NacitajZoznam();
                    break;
            }

            NacitajLegendu();
        }
        catch (Exception ex)
        {
            Suhrn = Preklad.F($"Kalendár sa nepodarilo načítať: {ex.GetBaseException().Message}");
        }
    }

    private static DateTime Pondelok(DateTime datum) => datum.Date.AddDays(-(((int)datum.DayOfWeek + 6) % 7));

    private void NacitajMesiac()
    {
        var prvy = new DateTime(AktualnyDatum.Year, AktualnyDatum.Month, 1);
        var od = Pondelok(prvy);
        var po = od.AddDays(41);
        var dni = VytvorDni(od, po, prvy.Month);
        Tyzdne = Enumerable.Range(0, 6)
            .Select(t => new TyzdenKalendara { Cislo = KalendarService.CisloTyzdna(od.AddDays(t * 7)), Dni = dni.Skip(t * 7).Take(7).ToList() })
            .ToList();
        Titulok = Mesiac(prvy);
        var vMesiaci = dni.Where(d => !d.MimoMesiaca).SelectMany(d => d.Udalosti).Select(u => u.Kluc).Distinct().Count();
        Suhrn = Preklad.F($"{Titulok}: {vMesiaci} udalostí a termínov. Dvojklikom na deň pridáte udalosť.");
    }

    private void NacitajTyzden()
    {
        var od = Pondelok(AktualnyDatum);
        var po = od.AddDays(6);
        DniTyzdna = VytvorDni(od, po, null);
        Titulok = Preklad.F($"{Sk.KratkyDatum(od)} – {Sk.Datum(po)} · {KalendarService.CisloTyzdna(od)}. týždeň");
        Suhrn = Preklad.F($"{DniTyzdna.SelectMany(d => d.Udalosti).Select(u => u.Kluc).Distinct().Count()} udalostí a termínov v týždni.");
    }

    private void NacitajZoznam()
    {
        var od = AktualnyDatum.Date;
        var po = od.AddDays(PocetDniZoznamu - 1);
        DniZoznamu = VytvorDni(od, po, null).Where(d => d.Udalosti.Count > 0 || d.MaSviatok || d.MaPoznamky).ToList();
        Titulok = $"{Sk.Datum(od)} – {Sk.Datum(po)}";
        Suhrn = Preklad.F($"Zoznam na {PocetDniZoznamu} dní od zvoleného dátumu: {DniZoznamu.Sum(d => d.Udalosti.Count)} udalostí a termínov.");
    }

    public static string Mesiac(DateTime datum) => Sk.MesiacRok(datum);

    private List<DenKalendara> VytvorDni(DateTime od, DateTime po, int? mesiac)
    {
        var vyskyty = _sluzba.Vyskyty(od, po, automaticke: UkazAutomaticke)
            .Where(v => UkazVybavene || v.Stav == StavUdalosti.Naplanovane)
            .ToList();

        // sviatky z Kalendária (ak nie je dostupné, kalendár funguje bez nich)
        var sviatky = new Dictionary<DateOnly, List<SviatokDna>>();
        try
        {
            for (var rok = od.Year; rok <= po.Year; rok++)
            {
                foreach (var s in KalendariumDb.Spolocna.Sviatky(rok))
                {
                    if (!sviatky.TryGetValue(s.Datum, out var zoznam))
                    {
                        sviatky[s.Datum] = zoznam = new List<SviatokDna>();
                    }

                    zoznam.Add(s);
                }
            }
        }
        catch (Exception)
        {
            // bez sviatkov
        }

        var poznamky = new List<PoznamkaRiadok>();
        try
        {
            poznamky = new PoznamkyService().PreDni(od, po);
        }
        catch (Exception)
        {
            // kalendár funguje aj bez poznámok
        }

        var dnes = DateTime.Today;
        var dni = new List<DenKalendara>();
        for (var d = od.Date; d <= po.Date; d = d.AddDays(1))
        {
            var den = DateOnly.FromDateTime(d);
            var sviatkyDna = sviatky.TryGetValue(den, out var s) ? s : new List<SviatokDna>();
            var meniny = "";
            try
            {
                meniny = string.Join(", ", KalendariumDb.Spolocna.Meniny(den));
            }
            catch (Exception)
            {
                // bez menín
            }

            var datum = d;
            dni.Add(new DenKalendara
            {
                Datum = d,
                MimoMesiaca = mesiac is int m && d.Month != m,
                Dnes = d == dnes,
                Vikend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                Volno = sviatkyDna.Any(x => x.DenPracovnehoPokoja),
                Sviatok = sviatkyDna.Count == 0 ? null : string.Join(", ", sviatkyDna.Where(x => x.Druh != DruhSviatku.PamatnyDen || sviatkyDna.Count == 1).Select(NazvySviatkov.Nazov)),
                Meniny = meniny,
                Udalosti = vyskyty.Where(v => v.Zaciatok.Date <= datum && v.PoslednyDen >= datum).ToList(),
                Poznamky = poznamky.Where(p => p.Datum?.Date == datum).ToList(),
            });
        }

        return dni;
    }

    private void NacitajLegendu()
    {
        var legenda = _sluzba.Typy(lenAktivne: true).Select(t => new PolozkaLegendy(t.Nazov, t.Farba)).ToList();
        if (UkazAutomaticke)
        {
            legenda.Add(new PolozkaLegendy(Preklad.T("koniec záruky"), KalendarService.FarbaZaruky));
            legenda.Add(new PolozkaLegendy(Preklad.T("záloha"), KalendarService.FarbaZalohy));
        }

        Legenda = legenda;
    }

    // ------------------------------------------------------------------ navigácia

    [RelayCommand]
    private void Predchadzajuce() => AktualnyDatum = Rezim switch
    {
        RezimKalendara.Mesiac => new DateTime(AktualnyDatum.Year, AktualnyDatum.Month, 1).AddMonths(-1),
        RezimKalendara.Tyzden => AktualnyDatum.AddDays(-7),
        _ => AktualnyDatum.AddDays(-PocetDniZoznamu),
    };

    [RelayCommand]
    private void Nasledujuce() => AktualnyDatum = Rezim switch
    {
        RezimKalendara.Mesiac => new DateTime(AktualnyDatum.Year, AktualnyDatum.Month, 1).AddMonths(1),
        RezimKalendara.Tyzden => AktualnyDatum.AddDays(7),
        _ => AktualnyDatum.AddDays(PocetDniZoznamu),
    };

    [RelayCommand]
    private void Dnes()
    {
        if (AktualnyDatum == DateTime.Today)
        {
            Obnov();
        }
        else
        {
            AktualnyDatum = DateTime.Today;
        }
    }

    /// <summary>Klik na „+ N ďalšie“ v mesiaci – zobrazí týždeň s týmto dňom.</summary>
    [RelayCommand]
    private void ZobrazDen(DenKalendara? den)
    {
        if (den is null)
        {
            return;
        }

        Rezim = RezimKalendara.Tyzden;
        AktualnyDatum = den.Datum;
    }

    // ------------------------------------------------------------------ udalosti

    [RelayCommand]
    private void NovaUdalost(DenKalendara? den) => OtvorEditor(new UdalostEditViewModel(null, den?.Datum ?? AktualnyDatum, null));

    /// <summary>Otvorí udalosť: vlastnú na úpravu, automatický termín položky v karte položky.</summary>
    [RelayCommand]
    private void OtvorUdalost(VyskytUdalosti? vyskyt)
    {
        if (vyskyt is null)
        {
            return;
        }

        if (vyskyt.UdalostId is int id)
        {
            OtvorEditor(new UdalostEditViewModel(id, null, null));
            return;
        }

        if (vyskyt.PolozkaId is int polozka)
        {
            try
            {
                var okno = new PolozkaEditWindow(new PolozkaEditViewModel(polozka, null)) { Owner = Dialogy.Vlastnik };
                if (okno.ShowDialog() == true)
                {
                    _hlavne.DataZmenene();
                    Obnov();
                }
            }
            catch (EvitaException ex)
            {
                Dialogy.Upozornenie(ex.Message);
            }

            return;
        }

        if (vyskyt.Zdroj == "záloha")
        {
            Dialogy.Info($"{vyskyt.Nazov}\n\n{vyskyt.Popis}");
            return;
        }

        Nastavenia();
    }

    // ------------------------------------------------------------------ poznámky

    [RelayCommand]
    private void NovaPoznamka(DenKalendara? den)
    {
        if (PoznamkaWindow.Otvor(new PoznamkaEditViewModel(null, den?.Datum ?? AktualnyDatum)))
        {
            Obnov();
        }
    }

    [RelayCommand]
    private void OtvorPoznamku(PoznamkaRiadok? poznamka)
    {
        if (poznamka is null)
        {
            return;
        }

        try
        {
            PoznamkaWindow.Otvor(new PoznamkaEditViewModel(poznamka.Id));
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }

        Obnov();
    }

    /// <summary>Klik na značku poznámok v mesiaci: jedna poznámka sa otvorí, viac sa ukáže v týždni.</summary>
    [RelayCommand]
    private void PoznamkyDna(DenKalendara? den)
    {
        if (den is null || den.Poznamky.Count == 0)
        {
            return;
        }

        if (den.Poznamky.Count == 1)
        {
            OtvorPoznamku(den.Poznamky[0]);
        }
        else
        {
            ZobrazDen(den);
        }
    }

    private void OtvorEditor(UdalostEditViewModel model)
    {
        var okno = new UdalostWindow(model) { Owner = Dialogy.Vlastnik };
        if (okno.ShowDialog() == true)
        {
            Obnov();
        }
    }

    [RelayCommand]
    private void Typy()
    {
        new TypyUdalostiWindow { Owner = Dialogy.Vlastnik }.ShowDialog();
        Obnov();
    }

    [RelayCommand]
    private void Nastavenia()
    {
        if (new NastaveniaKalendaraWindow { Owner = Dialogy.Vlastnik }.ShowDialog() == true)
        {
            Obnov();
        }
    }

    /// <summary>Pre automatický test: cesta k .ics bez okna Uložiť ako.</summary>
    public string? CestaIcsBezDialogu { get; set; }

    [RelayCommand]
    private void ExportIcs()
    {
        var od = Rezim == RezimKalendara.Mesiac ? new DateTime(AktualnyDatum.Year, AktualnyDatum.Month, 1) : AktualnyDatum.Date;
        var cesta = CestaIcsBezDialogu;
        if (cesta is null)
        {
            var dialog = new SaveFileDialog
            {
                Title = Preklad.T("Export kalendára pre Outlook alebo Google Kalendár"),
                Filter = Preklad.T("Kalendár iCalendar (*.ics)|*.ics"),
                FileName = $"EviTA_kalendar_{od:yyyy-MM}.ics",
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            cesta = dialog.FileName;
        }

        try
        {
            // od začiatku zobrazeného mesiaca na rok dopredu (opakované udalosti idú s pravidlom opakovania)
            var text = _sluzba.Ics(od, od.AddYears(1).AddDays(-1), UkazAutomaticke);
            File.WriteAllText(cesta, text, new UTF8Encoding(false));
            Dialogy.Info(Preklad.F($"Kalendár je uložený na rok dopredu od {Sk.Datum(od)}.\n\n{cesta}\n\nSúbor otvorí Outlook (dvojklik) a v Google Kalendári ho naimportujete cez Nastavenia → Import a export."));
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Kalendár sa nepodarilo uložiť."), ex);
        }
    }

    [RelayCommand]
    private void Tlacit()
    {
        var od = Rezim == RezimKalendara.Mesiac
            ? new DateTime(AktualnyDatum.Year, AktualnyDatum.Month, 1)
            : Pondelok(AktualnyDatum);
        var po = Rezim == RezimKalendara.Mesiac ? od.AddMonths(1).AddDays(-1) : Rezim == RezimKalendara.Tyzden ? od.AddDays(6) : AktualnyDatum.AddDays(PocetDniZoznamu - 1);
        var moznosti = new MoznostiZostavy
        {
            Druh = DruhZostavy.Kalendar,
            NaSirku = Rezim == RezimKalendara.Mesiac,
            Od = od,
            Do = po,
            KalendarZoznam = Rezim != RezimKalendara.Mesiac,
            AutomatickeTerminy = UkazAutomaticke,
        };
        var zostavy = new ZostavyService();
        try
        {
            new NahladWindow(() => zostavy.Vytvor(moznosti), moznosti.NaSirku, Preklad.T("Kalendár"), $"EviTA_kalendar_{od:yyyy-MM-dd}.pdf")
            {
                Owner = Dialogy.Vlastnik,
            }.ShowDialog();
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Kalendár sa nepodarilo pripraviť na tlač."), ex);
        }
    }
}
