using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Evita.Pomocne;
using Evita.Services;
using Microsoft.Win32;

namespace Evita.Views;

/// <summary>Náhľad tlačovej zostavy: strany ako na papieri, tlač a uloženie do PDF.</summary>
public partial class NahladWindow : Window
{
    private const string KlucPriecinok = "Tlac.PriecinokPdf";

    private readonly Func<MigraDoc.DocumentObjectModel.Document> _tvorba;
    private readonly VolbyNahladu? _volby;
    private bool _naSirku;
    private List<string> _stlpce = new();
    private bool _nacitavam;
    private readonly string _nazovSuboru;
    private double _sirkaStrany;

    public sealed record StranaNahladu(ImageSource Obrazok, double Sirka, double Vyska);

    /// <summary>Stĺpec, ktorý sa dá vo filtri stĺpcov zapnúť alebo vypnúť.</summary>
    public sealed record StlpecNahladu(string Kluc, string Nazov, bool Predvoleny);

    /// <summary>
    /// Zostava, ktorej orientáciu a stĺpce si používateľ volí priamo v náhľade (tlač z Evidencie). Tvorba dostane orientáciu
    /// a zoznam vybraných stĺpcov; Zapamataj uloží voľbu na budúce.
    /// </summary>
    public sealed class VolbyNahladu
    {
        public required Func<bool, IReadOnlyList<string>, MigraDoc.DocumentObjectModel.Document> Tvorba { get; init; }
        public required IReadOnlyList<StlpecNahladu> Stlpce { get; init; }
        public required IReadOnlyList<string> Vybrane { get; init; }
        public bool NaSirku { get; init; }
        public Action<bool, IReadOnlyList<string>>? Zapamataj { get; init; }
    }

    /// <summary>Náhľad s voľbou orientácie (na výšku / na šírku) a filtrom stĺpcov.</summary>
    public NahladWindow(VolbyNahladu volby, string titulok, string nazovSuboru)
    {
        _volby = volby;
        _naSirku = volby.NaSirku;
        _stlpce = volby.Stlpce.Select(s => s.Kluc).Where(volby.Vybrane.Contains).ToList();
        _tvorba = () => volby.Tvorba(_naSirku, _stlpce);
        _nazovSuboru = nazovSuboru;
        InitializeComponent();
        Title = Preklad.F($"Náhľad – {titulok}");
        Volby.Visibility = Visibility.Visible;
        _nacitavam = true;
        (volby.NaSirku ? NaSirkuVolba : NaVysku).IsChecked = true;
        _nacitavam = false;
        NaplnStlpce(_stlpce);
        Zvacsenie.SelectedIndex = 6;
        NacitajStrany();
    }

    /// <summary>Aktuálna orientácia (platí pre náhľad, PDF aj tlač).</summary>
    public bool NaSirku => _naSirku;

    /// <summary>Stĺpce, ktoré sa práve tlačia.</summary>
    public IReadOnlyList<string> VybraneStlpce => _stlpce;

    /// <summary>Zmena orientácie – prekreslí náhľad (aj pre samotest).</summary>
    public void NastavOrientaciu(bool naSirku)
    {
        if (_volby is null || naSirku == _naSirku)
        {
            return;
        }

        _naSirku = naSirku;
        _nacitavam = true;
        (naSirku ? NaSirkuVolba : NaVysku).IsChecked = true;
        _nacitavam = false;
        PoZmene();
    }

    /// <summary>Zmena tlačených stĺpcov – prekreslí náhľad (aj pre samotest). Aspoň jeden stĺpec musí zostať.</summary>
    public void NastavStlpce(IEnumerable<string> kluce)
    {
        if (_volby is null)
        {
            return;
        }

        var nove = _volby.Stlpce.Select(s => s.Kluc).Where(kluce.Contains).ToList();
        if (nove.Count == 0 || nove.Count(k => k != StlpecIkony) == 0)
        {
            Dialogy.Upozornenie(Preklad.T("Vyberte aspoň jeden stĺpec."));
            NaplnStlpce(_stlpce);
            return;
        }

        if (nove.SequenceEqual(_stlpce))
        {
            return;
        }

        _stlpce = nove;
        NaplnStlpce(_stlpce);
        PoZmene();
    }

    /// <summary>Pseudo-stĺpec „ikona kategórie“ vo filtri stĺpcov.</summary>
    public const string StlpecIkony = "ikonaKategorie";

    private void PoZmene()
    {
        _volby?.Zapamataj?.Invoke(_naSirku, _stlpce);
        NacitajStrany();
        NastavMierku();
    }

    private void NaplnStlpce(IReadOnlyCollection<string> vybrane)
    {
        if (_volby is null)
        {
            return;
        }

        ZoznamStlpcov.Children.Clear();
        foreach (var s in _volby.Stlpce)
        {
            ZoznamStlpcov.Children.Add(new CheckBox { Content = s.Nazov, Tag = s.Kluc, IsChecked = vybrane.Contains(s.Kluc), Margin = new Thickness(0, 1, 0, 1), MinHeight = 0 });
        }
    }

    private IEnumerable<string> ZaskrtnuteStlpce() =>
        ZoznamStlpcov.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag);

    private void Orientacia_Checked(object sender, RoutedEventArgs e)
    {
        if (_nacitavam || _volby is null)
        {
            return;
        }

        var naSirku = NaSirkuVolba.IsChecked == true;
        if (naSirku != _naSirku)
        {
            _naSirku = naSirku;
            PoZmene();
        }
    }

    private void PouzitStlpce_Click(object sender, RoutedEventArgs e)
    {
        FilterStlpcov.IsChecked = false;
    }

    /// <summary>Po zatvorení ponuky (Použiť alebo klik mimo) sa zaškrtnuté stĺpce použijú.</summary>
    private void PonukaStlpcov_Closed(object? sender, EventArgs e) => NastavStlpce(ZaskrtnuteStlpce());

    private void VsetkyStlpce_Click(object sender, RoutedEventArgs e)
    {
        foreach (var c in ZoznamStlpcov.Children.OfType<CheckBox>())
        {
            c.IsChecked = true;
        }
    }

    private void PredvoleneStlpce_Click(object sender, RoutedEventArgs e)
    {
        var predvolene = _volby?.Stlpce.Where(s => s.Predvoleny).Select(s => s.Kluc).ToHashSet() ?? new HashSet<string>();
        foreach (var c in ZoznamStlpcov.Children.OfType<CheckBox>())
        {
            c.IsChecked = predvolene.Contains((string)c.Tag);
        }
    }

    public NahladWindow(Func<MigraDoc.DocumentObjectModel.Document> tvorba, bool naSirku, string titulok, string nazovSuboru)
    {
        _tvorba = tvorba;
        _naSirku = naSirku;
        _nazovSuboru = nazovSuboru;
        InitializeComponent();
        Title = Preklad.F($"Náhľad – {titulok}");
        Zvacsenie.SelectedIndex = 6;
        NacitajStrany();
    }

    public int PocetStran { get; private set; }

    /// <summary>Pre automatický test: cesta k PDF bez okna Uložiť ako.</summary>
    public string? CestaBezDialogu { get; set; }

    private void NacitajStrany()
    {
        var strany = new List<StranaNahladu>();
        Dialogy.Cakaj(() =>
        {
            var vykreslenie = NahladDokumentu.Priprav(_tvorba());
            PocetStran = NahladDokumentu.PocetStran(vykreslenie);
            for (var i = 1; i <= PocetStran; i++)
            {
                var kresba = NahladDokumentu.Strana(vykreslenie, i, _naSirku, out var velkost);
                var obrazok = new DrawingImage(kresba.Drawing);
                obrazok.Freeze();
                strany.Add(new StranaNahladu(obrazok, velkost.Width, velkost.Height));
            }
        });

        _sirkaStrany = strany.Count > 0 ? strany[0].Sirka : 800;
        Stranky.ItemsSource = strany;
        Info.Text = Preklad.Pocet(PocetStran, "{0} strana", "{0} strany", "{0} strán") + " · "
                    + (_naSirku ? Preklad.T("A4 na šírku") : Preklad.T("A4 na výšku"));
    }

    private void NastavMierku()
    {
        if (Zvacsenie.SelectedItem is not ComboBoxItem { Tag: string tag } || Mierka is null)
        {
            return;
        }

        var mierka = tag == "sirka"
            ? Math.Clamp((Posuv.ActualWidth - 80) / Math.Max(1, _sirkaStrany), 0.3, 3)
            : double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
        if (double.IsNaN(mierka) || mierka <= 0)
        {
            mierka = 1;
        }

        Mierka.ScaleX = mierka;
        Mierka.ScaleY = mierka;
    }

    private void Zvacsenie_SelectionChanged(object sender, SelectionChangedEventArgs e) => NastavMierku();

    private void Posuv_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Zvacsenie.SelectedItem is ComboBoxItem { Tag: "sirka" })
        {
            NastavMierku();
        }
    }

    private void Vytlacit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NahladDokumentu.Vytlac(_tvorba, _naSirku, Title);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Tlač sa nepodarila."), ex);
        }
    }

    /// <summary>Uložiť ako PDF: okno volieb PDF (šifrovanie, heslo, zákazy, strany na list, sivá …) s náhľadom.</summary>
    private void UlozitPdf_Click(object sender, RoutedEventArgs e) => UlozitPdfSVolbami(this, _tvorba, _naSirku, Title, _nazovSuboru);

    /// <summary>Otvorí okno volieb PDF pre zostavu a uloží ju. Vráti true, ak sa PDF uložilo.</summary>
    public static bool UlozitPdfSVolbami(Window? vlastnik, Func<MigraDoc.DocumentObjectModel.Document> tvorba, bool naSirku, string titulok, string nazovSuboru)
    {
        try
        {
            MigraDoc.Rendering.DocumentRenderer? vykreslenie = null;
            Dialogy.Cakaj(() => vykreslenie = NahladDokumentu.Priprav(tvorba()));
            var okno = new TlacWindow(vykreslenie, naSirku, titulok, RezimOknaTlace.Pdf, tvorba, nazovSuboru) { Owner = vlastnik ?? Dialogy.Vlastnik };
            return okno.ShowDialog() == true;
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
            return false;
        }
    }

    /// <summary>Uloží zostavu do PDF. Vráti cestu alebo null, ak používateľ zrušil.</summary>
    public string? UlozitPdf() => UlozPdf(_tvorba, _nazovSuboru, CestaBezDialogu, this);

    /// <summary>Uloží zostavu do PDF (s oknom Uložiť ako) a otvorí ho. Vráti cestu alebo null.</summary>
    public static string? UlozPdf(Func<MigraDoc.DocumentObjectModel.Document> tvorba, string nazovSuboru, string? cestaBezDialogu, Window? vlastnik = null,
        VolbyPdf? volbyPdf = null)
    {
        var cesta = cestaBezDialogu;
        if (cesta is null)
        {
            string? priecinok;
            using (var db = Data.Databaza.Otvor())
            {
                priecinok = Data.Databaza.Citaj(db, KlucPriecinok);
            }

            if (string.IsNullOrEmpty(priecinok) || !Directory.Exists(priecinok))
            {
                priecinok = Path.Combine(Path.GetDirectoryName(VzoryService.Priecinok) ?? "", "Zostavy");
                try
                {
                    Directory.CreateDirectory(priecinok);
                }
                catch (Exception)
                {
                    priecinok = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
            }

            var dialog = new SaveFileDialog
            {
                Title = Preklad.T("Uložiť zostavu ako PDF"),
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = nazovSuboru,
                InitialDirectory = priecinok,
                OverwritePrompt = true,
            };
            if ((vlastnik is null ? dialog.ShowDialog() : dialog.ShowDialog(vlastnik)) != true)
            {
                return null;
            }

            cesta = dialog.FileName;
        }

        try
        {
            Dialogy.Cakaj(() => PdfVystup.Uloz(tvorba(), cesta, volbyPdf));
            using (var db = Data.Databaza.Otvor())
            {
                Data.Databaza.Zapis(db, KlucPriecinok, Path.GetDirectoryName(cesta));
                db.SaveChanges();
            }

            Tlac.OtvorSubor(cesta);
            return cesta;
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
            return null;
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

    private void Zavriet_Click(object sender, RoutedEventArgs e) => Close();
}
