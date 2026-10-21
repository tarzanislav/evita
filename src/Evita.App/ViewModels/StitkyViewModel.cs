using System.Globalization;
using System.IO;
using System.Printing;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;
using Microsoft.Win32;

namespace Evita.ViewModels;

public sealed record VolbaKodu(DruhKodu Kod, string Nazov);

public sealed record VolbaObsahu(ObsahKodu Obsah, string Nazov);

public sealed record VolbaPoradia(PoradieStitkov Poradie, string Nazov);

/// <summary>Okno tlače štítkov: formát, obsah, náhľad, tlač a PDF.</summary>
public partial class StitkyViewModel : ObservableObject
{
    private const int NajviacStranNahladu = 12;
    private const string KlucPriecinok = "Tlac.PriecinokPdf";

    private readonly StitkyService _sluzba = new();
    private readonly List<UdajeStitku> _udaje;
    private readonly string? _organizacia;
    private readonly DispatcherTimer _oneskorenie;
    private bool _nacitavam = true;

    public StitkyViewModel(IReadOnlyCollection<int> idPoloziek, string popisVyberu)
    {
        _udaje = _sluzba.NacitajUdaje(idPoloziek);
        _organizacia = NastaveniaTlace.Nacitaj().Nazov;
        PopisVyberu = popisVyberu;
        _oneskorenie = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _oneskorenie.Tick += (_, _) =>
        {
            _oneskorenie.Stop();
            ObnovNahlad();
        };

        Sablony = StitkyService.Sablony.ToList();
        Kody = new List<VolbaKodu>
        {
            new(DruhKodu.Qr, Preklad.T("QR kód")),
            new(DruhKodu.Ciarovy, Preklad.T("čiarový kód (Code 128)")),
            new(DruhKodu.Ziadny, Preklad.T("bez kódu")),
        };
        Obsahy = new List<VolbaObsahu>
        {
            new(ObsahKodu.KodEvita, Preklad.T("štítok EviTA (EVITA-číslo)")),
            new(ObsahKodu.Inventarne, Preklad.T("inventárne číslo")),
            new(ObsahKodu.Seriove, Preklad.T("sériové číslo")),
        };

        Poradia = new List<VolbaPoradia>
        {
            new(PoradieStitkov.Cislo, Preklad.T("podľa prírastkového čísla")),
            new(PoradieStitkov.Miestnost, Preklad.T("podľa miestností")),
            new(PoradieStitkov.Kategoria, Preklad.T("podľa kategórií")),
        };
        _ulozene = new System.Collections.ObjectModel.ObservableCollection<UlozeneStitky>(StitkyService.NacitajUlozene());

        var m = _sluzba.NacitajMoznosti();
        _sablona = Sablony.FirstOrDefault(s => s.Kluc == m.Sablona) ?? Sablony[0];
        _kod = m.Kod;
        _obsahKodu = m.ObsahKodu;
        _organizaciaNaStitku = m.Organizacia;
        _nazov = m.Nazov;
        _cislo = m.Cislo;
        _inventarne = m.Inventarne;
        _seriove = m.Seriove;
        _miestnost = m.Miestnost;
        _ramcek = m.Ramcek;
        _posunX = Text(m.PosunX);
        _posunY = Text(m.PosunY);
        _vlastnyHarok = m.VlastnyHarok;
        _vlastnaSirka = Text(m.VlastnaSirka);
        _vlastnaVyska = Text(m.VlastnaVyska);
        _vlastneStlpce = m.VlastneStlpce.ToString(CultureInfo.InvariantCulture);
        _vlastneRiadky = m.VlastneRiadky.ToString(CultureInfo.InvariantCulture);
        _vlastnyOkrajHore = Text(m.VlastnyOkrajHore);
        _vlastnyOkrajVlavo = Text(m.VlastnyOkrajVlavo);
        _vlastnaMedzeraVodorovne = Text(m.VlastnaMedzeraVodorovne);
        _vlastnaMedzeraZvisle = Text(m.VlastnaMedzeraZvisle);
        _poradie = m.Poradie;
        _novaStranaPreSkupinu = m.NovaStranaPreSkupinu;
        _nacitavam = false;
        ObnovNahlad();
    }

    public string PopisVyberu { get; }
    public int PocetPoloziek => _udaje.Count;
    public List<SablonaStitkov> Sablony { get; }
    public List<VolbaKodu> Kody { get; }
    public List<VolbaObsahu> Obsahy { get; }
    public List<VolbaPoradia> Poradia { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaSkupiny))]
    private PoradieStitkov _poradie;

    [ObservableProperty] private bool _novaStranaPreSkupinu;

    /// <summary>Poradie podľa miestností alebo kategórií – dá sa začať každú skupinu na novom hárku.</summary>
    public bool MaSkupiny => Poradie != PoradieStitkov.Cislo;

    /// <summary>Pomenované nastavenia štítkov.</summary>
    [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<UlozeneStitky> _ulozene;

    [ObservableProperty] private UlozeneStitky? _vybraneUlozene;

    partial void OnVybraneUlozeneChanged(UlozeneStitky? value)
    {
        if (value is not null)
        {
            Pouzi(value.Moznosti);
        }
    }

    /// <summary>Nastaví okno podľa uložených možností (napr. pomenovaného nastavenia).</summary>
    public void Pouzi(MoznostiStitkov m)
    {
        _nacitavam = true;
        Sablona = Sablony.FirstOrDefault(s => s.Kluc == m.Sablona) ?? Sablony[0];
        Kod = m.Kod;
        ObsahKodu = m.ObsahKodu;
        OrganizaciaNaStitku = m.Organizacia;
        Nazov = m.Nazov;
        Cislo = m.Cislo;
        Inventarne = m.Inventarne;
        Seriove = m.Seriove;
        Miestnost = m.Miestnost;
        Ramcek = m.Ramcek;
        Kopie = Math.Max(1, m.Kopie).ToString(CultureInfo.InvariantCulture);
        PosunX = Text(m.PosunX);
        PosunY = Text(m.PosunY);
        VlastnyHarok = m.VlastnyHarok;
        VlastnaSirka = Text(m.VlastnaSirka);
        VlastnaVyska = Text(m.VlastnaVyska);
        VlastneStlpce = m.VlastneStlpce.ToString(CultureInfo.InvariantCulture);
        VlastneRiadky = m.VlastneRiadky.ToString(CultureInfo.InvariantCulture);
        VlastnyOkrajHore = Text(m.VlastnyOkrajHore);
        VlastnyOkrajVlavo = Text(m.VlastnyOkrajVlavo);
        VlastnaMedzeraVodorovne = Text(m.VlastnaMedzeraVodorovne);
        VlastnaMedzeraZvisle = Text(m.VlastnaMedzeraZvisle);
        Poradie = m.Poradie;
        NovaStranaPreSkupinu = m.NovaStranaPreSkupinu;
        _nacitavam = false;
        ObnovNahlad();
    }

    /// <summary>Uloží aktuálne nastavenie pod názvom (rovnaký názov sa prepíše).</summary>
    [RelayCommand]
    private void UlozitNastavenie()
    {
        var nazov = Dialogy.Vstup(Preklad.T("Uložiť nastavenie štítkov"), Preklad.T("Názov nastavenia (napr. „Hárok 3 × 8 po miestnostiach“):"),
            VybraneUlozene?.Nazov ?? "");
        if (string.IsNullOrWhiteSpace(nazov))
        {
            return;
        }

        UlozNastavenie(nazov.Trim());
    }

    /// <summary>Uloží nastavenie pod daným názvom (aj samotest).</summary>
    public void UlozNastavenie(string nazov)
    {
        var moznosti = Moznosti();
        moznosti.ZacatOd = 1;
        var zoznam = Ulozene.Where(u => !u.Nazov.Equals(nazov, StringComparison.CurrentCultureIgnoreCase)).Append(new UlozeneStitky(nazov, moznosti)).ToList();
        StitkyService.UlozUlozene(zoznam);
        Ulozene = new System.Collections.ObjectModel.ObservableCollection<UlozeneStitky>(StitkyService.NacitajUlozene());
        _vybraneUlozene = Ulozene.FirstOrDefault(u => u.Nazov == nazov);
        OnPropertyChanged(nameof(VybraneUlozene));
    }

    [RelayCommand]
    private void VymazatNastavenie()
    {
        if (VybraneUlozene is not { } vybrane || !Dialogy.Potvrd(Preklad.F($"Vymazať nastavenie štítkov „{vybrane.Nazov}“?")))
        {
            return;
        }

        StitkyService.UlozUlozene(Ulozene.Where(u => u != vybrane));
        Ulozene = new System.Collections.ObjectModel.ObservableCollection<UlozeneStitky>(StitkyService.NacitajUlozene());
        _vybraneUlozene = null;
        OnPropertyChanged(nameof(VybraneUlozene));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JeVlastna), nameof(JeHarok))]
    private SablonaStitkov _sablona;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaKod))]
    private DruhKodu _kod;

    [ObservableProperty] private ObsahKodu _obsahKodu;
    [ObservableProperty] private bool _organizaciaNaStitku;
    [ObservableProperty] private bool _nazov;
    [ObservableProperty] private bool _cislo;
    [ObservableProperty] private bool _inventarne;
    [ObservableProperty] private bool _seriove;
    [ObservableProperty] private bool _miestnost;
    [ObservableProperty] private bool _ramcek;
    [ObservableProperty] private string _kopie = "1";
    [ObservableProperty] private string _zacatOd = "1";
    [ObservableProperty] private string _posunX;
    [ObservableProperty] private string _posunY;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JeHarok))]
    private bool _vlastnyHarok;

    [ObservableProperty] private string _vlastnaSirka;
    [ObservableProperty] private string _vlastnaVyska;
    [ObservableProperty] private string _vlastneStlpce;
    [ObservableProperty] private string _vlastneRiadky;
    [ObservableProperty] private string _vlastnyOkrajHore;
    [ObservableProperty] private string _vlastnyOkrajVlavo;
    [ObservableProperty] private string _vlastnaMedzeraVodorovne;
    [ObservableProperty] private string _vlastnaMedzeraZvisle;

    [ObservableProperty] private List<NahladWindow.StranaNahladu> _strany = new();
    [ObservableProperty] private string _infoText = "";
    [ObservableProperty] private string _rozmerText = "";
    [ObservableProperty] private string _upozornenie = "";
    [ObservableProperty] private double _mierkaNahladu = 1;

    public bool JeVlastna => Sablona.Kluc == StitkyService.KlucVlastna;
    public bool JeHarok => JeVlastna ? VlastnyHarok : Sablona.Harok;
    public bool MaKod => Kod != DruhKodu.Ziadny;

    /// <summary>Pre automatický test: cesta k PDF bez okna Uložiť ako.</summary>
    public string? CestaBezDialogu { get; set; }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_nacitavam && e.PropertyName is not (nameof(Strany) or nameof(InfoText) or nameof(RozmerText) or nameof(Upozornenie)
                or nameof(MierkaNahladu) or nameof(JeVlastna) or nameof(JeHarok) or nameof(MaKod) or nameof(MaSkupiny)
                or nameof(Ulozene) or nameof(VybraneUlozene)))
        {
            _oneskorenie.Stop();
            _oneskorenie.Start();
        }
    }

    private static string Text(double cislo) => cislo.ToString("0.##", Sk.Kultura);

    private static double CisloZTextu(string? text, double predvolene)
    {
        var t = (text ?? "").Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var c) ? c : predvolene;
    }

    private static int CeleCislo(string? text, int predvolene) =>
        int.TryParse((text ?? "").Trim(), out var c) ? c : predvolene;

    public MoznostiStitkov Moznosti() => new()
    {
        Sablona = Sablona.Kluc,
        Kod = Kod,
        ObsahKodu = ObsahKodu,
        Organizacia = OrganizaciaNaStitku,
        Nazov = Nazov,
        Cislo = Cislo,
        Inventarne = Inventarne,
        Seriove = Seriove,
        Miestnost = Miestnost,
        Ramcek = Ramcek,
        Kopie = Math.Clamp(CeleCislo(Kopie, 1), 1, 20),
        ZacatOd = Math.Max(1, CeleCislo(ZacatOd, 1)),
        PosunX = Math.Clamp(CisloZTextu(PosunX, 0), -20, 20),
        PosunY = Math.Clamp(CisloZTextu(PosunY, 0), -20, 20),
        VlastnyHarok = VlastnyHarok,
        VlastnaSirka = CisloZTextu(VlastnaSirka, 70),
        VlastnaVyska = CisloZTextu(VlastnaVyska, 37),
        VlastneStlpce = CeleCislo(VlastneStlpce, 3),
        VlastneRiadky = CeleCislo(VlastneRiadky, 8),
        VlastnyOkrajHore = CisloZTextu(VlastnyOkrajHore, 0),
        VlastnyOkrajVlavo = CisloZTextu(VlastnyOkrajVlavo, 0),
        VlastnaMedzeraVodorovne = CisloZTextu(VlastnaMedzeraVodorovne, 0),
        VlastnaMedzeraZvisle = CisloZTextu(VlastnaMedzeraZvisle, 0),
        Poradie = Poradie,
        NovaStranaPreSkupinu = NovaStranaPreSkupinu,
    };

    public RozlozenieStitkov Rozlozenie() => StitkyService.Priprav(Moznosti(), _udaje, _organizacia);

    public void ObnovNahlad()
    {
        try
        {
            var r = Rozlozenie();
            var strany = new List<NahladWindow.StranaNahladu>();
            for (var i = 0; i < Math.Min(r.PocetStran, NajviacStranNahladu); i++)
            {
                var kresba = StitkyService.Vizual(r, i, out var velkost);
                var obrazok = new DrawingImage(kresba.Drawing);
                obrazok.Freeze();
                strany.Add(new NahladWindow.StranaNahladu(obrazok, velkost.Width, velkost.Height));
            }

            MierkaNahladu = r.Sablona.Harok || strany.Count == 0 ? 1 : Math.Clamp(420 / strany[0].Sirka, 1, 4);
            Strany = strany;
            RozmerText = r.Sablona.Rozmer;
            var info = Preklad.Pocet(r.PocetStitkov, "{0} štítok", "{0} štítky", "{0} štítkov") + " · "
                       + Preklad.Pocet(r.PocetStran, "{0} strana", "{0} strany", "{0} strán");
            if (r.PocetStran > NajviacStranNahladu)
            {
                info += " · " + Preklad.F($"náhľad ukazuje prvých {NajviacStranNahladu} strán");
            }

            InfoText = info;
            Upozornenie = KontrolaKodov(r);
        }
        catch (Exception ex)
        {
            Strany = new List<NahladWindow.StranaNahladu>();
            InfoText = Preklad.F($"Náhľad sa nepodarilo vytvoriť: {ex.GetBaseException().Message}");
        }
    }

    /// <summary>Upozornenie, ak niektoré položky nemajú inventárne/sériové číslo alebo je čiarový kód príliš hustý.</summary>
    private string KontrolaKodov(RozlozenieStitkov r)
    {
        var m = r.Moznosti;
        if (m.Kod == DruhKodu.Ziadny)
        {
            return "";
        }

        var bezCisla = m.ObsahKodu switch
        {
            ObsahKodu.Inventarne => _udaje.Count(u => string.IsNullOrWhiteSpace(u.Inventarne)),
            ObsahKodu.Seriove => _udaje.Count(u => string.IsNullOrWhiteSpace(u.Seriove)),
            _ => 0,
        };
        var texty = new List<string>();
        if (bezCisla > 0)
        {
            texty.Add(Preklad.F($"{bezCisla} položiek nemá zvolené číslo – ich kód bude štítok EviTA."));
        }

        if (m.Kod == DruhKodu.Ciarovy && _udaje.Count > 0)
        {
            var najdlhsi = _udaje.Select(u => StitkyService.TextKodu(u, m)).MaxBy(t => t.Length) ?? "";
            var moduly = CiarovyKod.Code128(najdlhsi).Length + 20;
            if (r.Sablona.Sirka * 0.86 / moduly < 0.19)
            {
                texty.Add(Preklad.T("Čiarový kód je na tento štítok príliš dlhý – čítačka ho nemusí prečítať. Zvoľte QR kód, kratší obsah alebo širší štítok."));
            }
        }

        return string.Join(" ", texty);
    }

    [RelayCommand]
    private void Vytlacit()
    {
        var r = Rozlozenie();
        if (r.PocetStitkov == 0)
        {
            return;
        }

        UlozNastavenia();
        if (Dialogy.TichyRezim)
        {
            Dialogy.TicheHlasenia.Add($"[Tlač] {Preklad.T("Štítky EviTA")}");
            return;
        }

        new TlacStitkovWindow(r) { Owner = Dialogy.Vlastnik }.ShowDialog();
    }

    [RelayCommand]
    private void UlozitPdf() => UlozPdf();

    /// <summary>Uloží štítky do PDF. Vráti cestu alebo null.</summary>
    public string? UlozPdf()
    {
        var r = Rozlozenie();
        if (r.PocetStitkov == 0)
        {
            return null;
        }

        var cesta = CestaBezDialogu;
        if (cesta is null)
        {
            string? priecinok;
            using (var db = Databaza.Otvor())
            {
                priecinok = Databaza.Citaj(db, KlucPriecinok);
            }

            if (string.IsNullOrEmpty(priecinok) || !Directory.Exists(priecinok))
            {
                priecinok = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            var dialog = new SaveFileDialog
            {
                Title = Preklad.T("Uložiť štítky ako PDF"),
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = $"Stitky_EviTA_{DateTime.Today:yyyy-MM-dd}.pdf",
                InitialDirectory = priecinok,
                OverwritePrompt = true,
            };
            var vlastnik = Dialogy.Vlastnik;
            if ((vlastnik is null ? dialog.ShowDialog() : dialog.ShowDialog(vlastnik)) != true)
            {
                return null;
            }

            cesta = dialog.FileName;
        }

        try
        {
            Dialogy.Cakaj(() => StitkyService.UlozPdf(r, cesta));
            UlozNastavenia();
            using (var db = Databaza.Otvor())
            {
                Databaza.Zapis(db, KlucPriecinok, Path.GetDirectoryName(cesta));
                db.SaveChanges();
            }

            Tlac.OtvorSubor(cesta);
            return cesta;
        }
        catch (IOException ex)
        {
            Dialogy.Upozornenie(Preklad.F($"PDF sa nedá uložiť. Ak je súbor otvorený v inom programe, zatvorte ho a skúste znova.\n\n{ex.Message}"));
            return null;
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("PDF sa nepodarilo uložiť."), ex);
            return null;
        }
    }

    private void UlozNastavenia()
    {
        try
        {
            _sluzba.UlozMoznosti(Moznosti());
        }
        catch (Exception)
        {
            // nastavenia sa neuložia – štítky sa aj tak vytlačia
        }
    }

    /// <summary>Pri zatvorení okna sa nastavenia zapamätajú.</summary>
    public void PriZatvoreni() => UlozNastavenia();
}
