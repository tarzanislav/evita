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
    private readonly bool _naSirku;
    private readonly string _nazovSuboru;
    private double _sirkaStrany;

    public sealed record StranaNahladu(ImageSource Obrazok, double Sirka, double Vyska);

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

    private void UlozitPdf_Click(object sender, RoutedEventArgs e) => UlozitPdf();

    /// <summary>Uloží zostavu do PDF. Vráti cestu alebo null, ak používateľ zrušil.</summary>
    public string? UlozitPdf() => UlozPdf(_tvorba, _nazovSuboru, CestaBezDialogu, this);

    /// <summary>Uloží zostavu do PDF (s oknom Uložiť ako) a otvorí ho. Vráti cestu alebo null.</summary>
    public static string? UlozPdf(Func<MigraDoc.DocumentObjectModel.Document> tvorba, string nazovSuboru, string? cestaBezDialogu, Window? vlastnik = null)
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
            Dialogy.Cakaj(() => PdfZostava.UlozPdf(tvorba(), cesta));
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
