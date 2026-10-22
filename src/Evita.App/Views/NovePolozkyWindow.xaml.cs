using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;
using Evita.ViewModels;
using Microsoft.Win32;

namespace Evita.Views;

/// <summary>Riadok budúcej položky (z faktúry, objednávky, diktovania).</summary>
public sealed partial class RiadokNovejPolozky : ObservableObject
{
    [ObservableProperty] private bool _pridat = true;
    [ObservableProperty] private string _nazov = "";
    [ObservableProperty] private int _pocet = 1;
    [ObservableProperty] private int _kategoriaId;
    [ObservableProperty] private string? _vyrobca;
    [ObservableProperty] private string? _model;
    [ObservableProperty] private string? _ean;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CenaText))]
    private decimal? _cena;

    /// <summary>Sadzba DPH z elektronickej faktúry (percentá) – cena je bez DPH.</summary>
    public decimal? Dph { get; set; }

    public string CenaText
    {
        get => Cena is { } c ? c.ToString("N2", Sk.Kultura) : "";
        set => Cena = decimal.TryParse((value ?? "").Replace("€", "").Replace(" ", "").Replace(' ', ' ').Trim().Replace('.', ','),
            NumberStyles.Number, Sk.Kultura, out var c) ? c : null;
    }
}

/// <summary>
/// Nové položky z dokladu: faktúra (PDF s vloženou e-faktúrou alebo OCR, XML/ISDOC, sken, fotka), skopírovaná webová
/// objednávka, diktovanie alebo EAN z ručnej čítačky. Rozpoznané riadky sa skontrolujú a vytvoria sa z nich položky.
/// </summary>
public partial class NovePolozkyWindow : Window
{
    private readonly DispatcherTimer _oneskorenie = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly List<ZnamyVyrobok> _zname = VyrobcaModelService.NacitajZEvidencie();
    private readonly DiktovanieDoPola _diktovanie;
    private bool _nacitavam = true;

    public NovePolozkyWindow()
    {
        var ciselniky = new CiselnikService();
        Kategorie = ciselniky.Volby(TypCiselnika.Kategorie, null, null);
        InitializeComponent();
        Riadky.ItemsSource = Polozky;
        Miestnost.ItemsSource = ciselniky.Volby(TypCiselnika.Miestnosti, null, Preklad.T("(bez miestnosti)"));
        Miestnost.SelectedValue = 0;
        PredvolenaKategoria.ItemsSource = Kategorie;
        PredvolenaKategoria.SelectedItem = Kategorie.FirstOrDefault(k => Sk.BezDiakritiky(k.Nazov) is "prislusenstvo" or "ine" or "ostatne")
                                           ?? Kategorie.FirstOrDefault();
        DatumNakupu.SelectedDate = DateTime.Today;
        PripocitatDph.Visibility = Visibility.Collapsed;
        _diktovanie = new DiktovanieDoPola(Text, Diktovat, IkonaDiktovania, TextDiktovania, Stav, Hlasitost);
        _oneskorenie.Tick += (_, _) => RozpoznajText();
        _diktovanie.Vlozene += RozpoznajText;
        Polozky.CollectionChanged += (_, _) => AktualizujSuhrn();
        Closing += (_, _) => _diktovanie.ZrusNahravanie();
        _nacitavam = false;
        AktualizujSuhrn();
    }

    public List<Volba> Kategorie { get; }

    public ObservableCollection<RiadokNovejPolozky> Polozky { get; } = new();

    /// <summary>Id vytvorených položiek (po potvrdení okna).</summary>
    public List<int> Vytvorene { get; } = new();

    public string StavText => Stav.Text;

    private void UkazStav(string text, bool? uspech = null) => _diktovanie.UkazStav(text, uspech);

    // ------------------------------------------------------------------ zdroje

    private async void Otvorit_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Preklad.T("Otvoriť faktúru alebo objednávku"),
            Filter = Preklad.T("Faktúry a doklady") + " (*.pdf;*.xml;*.isdoc;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.txt)|*.pdf;*.xml;*.isdoc;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.txt",
        };
        if (dialog.ShowDialog(this) == true)
        {
            await OtvorSuborAsync(dialog.FileName);
        }
    }

    /// <summary>Načíta doklad zo súboru: e-faktúra (XML, aj vložená v PDF), inak text z PDF alebo obrázka cez OCR.</summary>
    public async Task OtvorSuborAsync(string cesta)
    {
        var pripona = Path.GetExtension(cesta).ToLowerInvariant();
        var nazov = Path.GetFileName(cesta);
        try
        {
            if (pripona is ".xml" or ".isdoc" or ".txt")
            {
                var obsah = await File.ReadAllTextAsync(cesta);
                if (pripona != ".txt" && DokladService.ZXml(obsah, nazov) is { } xml)
                {
                    Rozpoznaj(xml, true, Preklad.F($"{nazov} – elektronická faktúra (presné údaje)"));
                    return;
                }

                NastavText(obsah, Preklad.F($"{nazov} – text"));
                return;
            }

            if (pripona == ".pdf" && DokladService.XmlZPdf(cesta) is { } vlozene && DokladService.ZXml(vlozene, nazov) is { Riadky.Count: > 0 } eFaktura)
            {
                Rozpoznaj(eFaktura, true, Preklad.F($"{nazov} – elektronická faktúra vložená v PDF (presné údaje)"));
                return;
            }

            UkazStav(Preklad.F($"Rozpoznávam text v {nazov}…"));
            IsEnabled = false;
            var (text, jazyk) = await OcrDokladu.PrecitajAsync(cesta);
            NastavText(text, Preklad.F($"{nazov} – rozpoznaný text (OCR, {jazyk}) – skontrolujte názvy a ceny"));
        }
        catch (EvitaException ex)
        {
            UkazStav(ex.Message, false);
        }
        catch (Exception ex)
        {
            UkazStav(Preklad.F($"Doklad sa nepodarilo prečítať: {ex.GetBaseException().Message}"), false);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void Vlozit_Click(object sender, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsText())
        {
            UkazStav(Preklad.T("Schránka neobsahuje text – v prehliadači označte položky objednávky a stlačte Ctrl+C."), false);
            return;
        }

        var text = Clipboard.GetText();
        NastavText(Text.Text.Trim().Length == 0 ? text : Text.Text.TrimEnd() + Environment.NewLine + text, Preklad.T("text zo schránky"));
    }

    private async void Diktovat_Click(object sender, RoutedEventArgs e) => await _diktovanie.KlikAsync(novyRiadok: true);

    private void NastavenieDiktovania_Click(object sender, RoutedEventArgs e)
    {
        _diktovanie.ZrusNahravanie();
        new DiktovanieWindow { Owner = this }.ShowDialog();
    }

    /// <summary>Vloží text dokladu a rozpozná položky (aj samotest).</summary>
    public void NastavText(string text, string zdroj)
    {
        _nacitavam = true;
        Text.Text = text;
        _nacitavam = false;
        Rozpoznaj(DokladService.ZTextu(text, zdroj), false, zdroj);
    }

    /// <summary>Prepíše zvuk (samotest so súborom WAV) a rozpozná položky.</summary>
    public Task DiktovanieAsync(float[] zvuk) => _diktovanie.PrepisAVlozAsync(zvuk, true);

    /// <summary>Rozpozná položky z aktuálneho textu okna (po úprave textu alebo diktovaní).</summary>
    public void RozpoznajText()
    {
        _oneskorenie.Stop();
        Rozpoznaj(DokladService.ZTextu(Text.Text, Preklad.T("text")), false);
    }

    private void Text_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_nacitavam)
        {
            return;
        }

        _oneskorenie.Stop();
        _oneskorenie.Start();
    }

    private void Rozpoznaj(Doklad doklad, bool xml, string? zdroj = null)
    {
        if (xml)
        {
            // text okna ukáže riadky e-faktúry (dajú sa upraviť – potom sa rozpoznajú z textu)
            _nacitavam = true;
            Text.Text = string.Join(Environment.NewLine, doklad.Riadky.Select(r =>
                $"{r.Nazov}\t{r.Pocet} ks\t{(r.CenaKus is { } c ? c.ToString("N2", Sk.Kultura) + " €" : "")}"));
            _nacitavam = false;
        }

        // úpravy v tabuľke (kategória, výrobca, model) zostanú pri opätovnom rozpoznaní rovnakého riadku
        var predtym = Polozky.GroupBy(r => r.Nazov.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        Polozky.Clear();
        foreach (var r in doklad.Riadky)
        {
            var navrh = VyrobcaModelService.ZNazvu(r.Nazov, _zname);
            var riadok = new RiadokNovejPolozky
            {
                Nazov = r.Nazov,
                Pocet = Math.Clamp(r.Pocet, 1, 500),
                Cena = r.CenaKus,
                Dph = r.Dph,
                Ean = r.Ean,
                Vyrobca = navrh?.Vyrobca,
                Model = navrh?.Model,
                KategoriaId = UhadniKategoriu(r.Nazov) ?? 0,
            };
            if (predtym.TryGetValue(r.Nazov.Trim(), out var stary))
            {
                riadok.Pridat = stary.Pridat;
                riadok.KategoriaId = stary.KategoriaId != 0 ? stary.KategoriaId : riadok.KategoriaId;
                riadok.Vyrobca = stary.Vyrobca ?? riadok.Vyrobca;
                riadok.Model = stary.Model ?? riadok.Model;
                riadok.Ean ??= stary.Ean;
            }

            riadok.PropertyChanged += (_, _) => AktualizujSuhrn();
            Polozky.Add(riadok);
        }

        if (doklad.Datum is { } datum)
        {
            DatumNakupu.SelectedDate = datum;
        }

        var poznamka = string.Join(", ", new[]
        {
            doklad.Cislo is { } cislo ? Preklad.F($"doklad č. {cislo}") : null,
            doklad.Dodavatel is { } dodavatel ? Preklad.F($"dodávateľ {dodavatel}") : null,
        }.Where(t => t is not null));
        if (poznamka.Length > 0)
        {
            Poznamka.Text = Sk.VelkePrve(poznamka);
        }

        var bezDph = xml && doklad.CenyBezDph && doklad.Riadky.Any(r => r.CenaKus is not null);
        PripocitatDph.Visibility = bezDph ? Visibility.Visible : Visibility.Collapsed;
        PripocitatDph.IsChecked = bezDph;
        var sadzby = doklad.Riadky.Select(r => r.Dph).Where(d => d is not null).Distinct().ToList();
        TextDph.Text = sadzby.Count == 1
            ? Preklad.F($"Pripočítať DPH k cenám ({sadzby[0]:0.##} %)")
            : Preklad.T("Pripočítať DPH k cenám (sadzba podľa riadku faktúry)");
        if (zdroj is not null)
        {
            Zdroj.Text = zdroj;
        }

        UkazStav(Polozky.Count == 0
            ? Preklad.T("Nenašiel som žiadne riadky s názvom a cenou. Upravte text vľavo alebo pridajte položky do tabuľky ručne.")
            : Preklad.Pocet(Polozky.Count, "Rozpoznaná {0} položka", "Rozpoznané {0} položky", "Rozpoznaných {0} položiek"),
            Polozky.Count > 0);
        AktualizujSuhrn();
    }

    /// <summary>Typové rady výrobkov, z ktorých sa dá určiť druh (ProBook = notebook …).</summary>
    private static readonly (string Slovo, string Druh)[] Rady =
    {
        ("thinkpad", "notebook"), ("probook", "notebook"), ("elitebook", "notebook"), ("latitude", "notebook"), ("ideapad", "notebook"),
        ("vivobook", "notebook"), ("zenbook", "notebook"), ("travelmate", "notebook"), ("inspiron", "notebook"), ("vostro", "notebook"),
        ("expertbook", "notebook"), ("matebook", "notebook"), ("optiplex", "pocitac"), ("prodesk", "pocitac"), ("elitedesk", "pocitac"),
        ("thinkcentre", "pocitac"), ("laserjet", "tlaciaren"), ("i-sensys", "tlaciaren"), ("deskjet", "tlaciaren"), ("officejet", "tlaciaren"),
        ("ecotank", "tlaciaren"), ("pixma", "tlaciaren"), ("galaxy tab", "tablet"), ("keyboard", "klavesnica"), ("mouse", "mys"),
        ("logitech", "prislusenstvo"), ("headset", "sluchadla"), ("webcam", "webkamera"), ("ubiquiti", "access point"),
    };

    /// <summary>
    /// Kategória podľa názvu: názov kategórie v texte, inak druh výrobku podľa kľúčových slov ikon kategórií
    /// (notebook, monitor, tlačiareň, myš = príslušenstvo …) a typových radov (ProBook, OptiPlex, LaserJet …).
    /// </summary>
    public int? UhadniKategoriu(string nazov)
    {
        var text = " " + Sk.BezDiakritiky(nazov) + " ";
        foreach (var k in Kategorie)
        {
            var meno = Sk.BezDiakritiky(k.Nazov).Trim();
            if (meno.Length >= 2 && (text.Contains(" " + meno + " ") || (meno.Length >= 5 && text.Contains(" " + meno[..5]))))
            {
                return k.Id;
            }
        }

        var doplnok = string.Join(" ", Rady.Where(r => text.Contains(r.Slovo)).Select(r => r.Druh));
        var druh = IkonyKategorii.PodlaNazvu(text + " " + doplnok).Kluc;
        if (druh == IkonyKategorii.Predvolena)
        {
            return null;
        }

        return Kategorie.FirstOrDefault(k => IkonyKategorii.PodlaNazvu(k.Nazov).Kluc == druh)?.Id;
    }

    private async void PodlaEan_Click(object sender, RoutedEventArgs e) => await DoplnPodlaEanAsync();

    /// <summary>Riadky s EAN doplní o názov, výrobcu, model a kategóriu (evidencia, Open Icecat, UPCitemdb).</summary>
    public async Task DoplnPodlaEanAsync()
    {
        var sEan = Polozky.Where(r => !string.IsNullOrWhiteSpace(r.Ean)).ToList();
        if (sEan.Count == 0)
        {
            UkazStav(Preklad.T("Žiadny riadok nemá EAN. Naskenujte čiarové kódy z krabíc do textu vľavo (každý na nový riadok)."), false);
            return;
        }

        var sluzba = new EanService();
        var doplnene = 0;
        foreach (var r in sEan)
        {
            UkazStav(Preklad.F($"Hľadám EAN {r.Ean}…"));
            try
            {
                var vysledok = await sluzba.HladajAsync(r.Ean!);
                if (vysledok.Najdene.FirstOrDefault() is { } u)
                {
                    if (!string.IsNullOrWhiteSpace(u.Nazov) && (r.Nazov.StartsWith("EAN ", StringComparison.Ordinal) || r.Nazov.Length == 0))
                    {
                        r.Nazov = u.Nazov!;
                    }

                    r.Vyrobca ??= u.Vyrobca;
                    r.Model ??= u.Model;
                    if (u.KategoriaId is int id && Kategorie.Any(k => k.Id == id))
                    {
                        r.KategoriaId = id;
                    }
                    else if (r.KategoriaId == 0 && UhadniKategoriu(r.Nazov) is int odhad)
                    {
                        r.KategoriaId = odhad;
                    }

                    doplnene++;
                }
            }
            catch (Exception)
            {
                // nedostupná služba – riadok zostane bez zmeny
            }
        }

        UkazStav(Preklad.F($"Podľa EAN doplnené: {doplnene} z {sEan.Count}."), doplnene > 0);
    }

    // ------------------------------------------------------------------ súhrn a vytvorenie

    private void Spolocne_Changed(object sender, RoutedEventArgs e) => AktualizujSuhrn();

    private void Riadky_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(AktualizujSuhrn), DispatcherPriority.Background);

    private decimal? CenaSDph(RiadokNovejPolozky r) =>
        r.Cena is { } c && PripocitatDph.IsChecked == true && PripocitatDph.Visibility == Visibility.Visible
            ? Math.Round(c * (1 + (r.Dph ?? 23m) / 100m), 2)
            : r.Cena;

    private void AktualizujSuhrn()
    {
        if (_nacitavam || Suhrn is null)
        {
            return;
        }

        var vybrane = Polozky.Where(r => r.Pridat && r.Nazov.Trim().Length > 0).ToList();
        var kusov = vybrane.Sum(r => Math.Max(1, r.Pocet));
        var suma = vybrane.Sum(r => (CenaSDph(r) ?? 0) * Math.Max(1, r.Pocet));
        var bezKategorie = vybrane.Count(r => r.KategoriaId == 0);
        Suhrn.Text = Preklad.Pocet(kusov, "Vytvorí sa {0} položka", "Vytvoria sa {0} položky", "Vytvorí sa {0} položiek")
                     + $" · {Sk.Suma(suma)}"
                     + (bezKategorie > 0
                         ? " · " + Preklad.Pocet(bezKategorie, "{0} riadok bez kategórie dostane predvolenú", "{0} riadky bez kategórie dostanú predvolenú",
                             "{0} riadkov bez kategórie dostane predvolenú")
                         : "");
        TextVytvorit.Text = Preklad.Pocet(kusov, "Vytvoriť {0} položku", "Vytvoriť {0} položky", "Vytvoriť {0} položiek");
        Vytvorit.IsEnabled = kusov > 0;
    }

    private void Vytvorit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            VytvoritPolozky();
            DialogResult = true;
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Položky sa nepodarilo vytvoriť."), ex);
        }
    }

    /// <summary>Vytvorí položky (každý kus samostatne) a vráti ich id.</summary>
    public List<int> VytvoritPolozky()
    {
        Riadky.CommitEdit(DataGridEditingUnit.Row, true);
        var predvolena = PredvolenaKategoria.SelectedValue as int? ?? Kategorie.FirstOrDefault()?.Id ?? 0;
        if (predvolena == 0 && Polozky.Any(r => r.Pridat && r.KategoriaId == 0))
        {
            throw new EvitaException(Preklad.T("Vyberte kategóriu pre riadky, ktorých kategória sa nerozpoznala."));
        }

        var datum = DatumNakupu.SelectedDate;
        var miestnost = Miestnost.SelectedValue is int m && m > 0 ? m : (int?)null;
        var sluzba = new PolozkaService();
        Vytvorene.Clear();
        foreach (var r in Polozky.Where(r => r.Pridat && r.Nazov.Trim().Length > 0).ToList())
        {
            var vzor = new Polozka
            {
                Nazov = r.Nazov.Trim(),
                KategoriaId = r.KategoriaId > 0 ? r.KategoriaId : predvolena,
                MiestnostId = miestnost,
                Vyrobca = string.IsNullOrWhiteSpace(r.Vyrobca) ? null : r.Vyrobca.Trim(),
                Model = string.IsNullOrWhiteSpace(r.Model) ? null : r.Model.Trim(),
                Ean = string.IsNullOrWhiteSpace(r.Ean) ? null : r.Ean.Trim(),
                Cena = CenaSDph(r),
                DatumNakupu = datum,
                ZarukaDo = datum is { } d ? PolozkaEditViewModel.ZarukaPodlaNakupu(d) : null,
                DalsiaRevizia = datum is { } d2 ? PolozkaEditViewModel.ReviziaPodlaNakupu(d2) : null,
                Poznamka = string.IsNullOrWhiteSpace(Poznamka.Text) ? null : Poznamka.Text.Trim(),
                Stav = StavPolozky.VPrevadzke,
            };
            var idcka = sluzba.Pridaj(vzor, Math.Clamp(r.Pocet, 1, 500), Array.Empty<string>());
            Vytvorene.AddRange(idcka);
            if (vzor.Model is null && VyrobcaModelService.ZInternetuZapnute)
            {
                VyrobcaModelService.PridajCakajuce(idcka);
            }
        }

        return Vytvorene;
    }
}
