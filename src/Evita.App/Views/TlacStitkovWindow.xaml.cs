using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.Views;

/// <summary>Tlač štítkov s náhľadom každej strany: tlačiareň (pamätá sa zvlášť pre štítky), rozsah strán.</summary>
public partial class TlacStitkovWindow : Window
{
    public const string KlucTlaciaren = "Stitky.Tlaciaren";

    private readonly RozlozenieStitkov _rozlozenie;
    private int _strana;
    private bool _nacitavam = true;

    public TlacStitkovWindow(RozlozenieStitkov rozlozenie)
    {
        _rozlozenie = rozlozenie;
        InitializeComponent();
        Do.Text = rozlozenie.PocetStran.ToString(CultureInfo.InvariantCulture);
        FormatText.Text = rozlozenie.Sablona.Nazov + " · " + rozlozenie.Sablona.Rozmer;
        string? ulozena;
        using (var db = Databaza.Otvor())
        {
            ulozena = Databaza.Citaj(db, KlucTlaciaren);
        }

        NaplnTlaciarne(ulozena);
        _nacitavam = false;
        Ukaz();
    }

    public int PocetStran => _rozlozenie.PocetStran;

    /// <summary>Strany na tlač (od – do, číslované od 1).</summary>
    public (int Od, int Do) RozsahStran
    {
        get
        {
            if (Vsetky.IsChecked == true)
            {
                return (1, PocetStran);
            }

            var od = int.TryParse(Od.Text.Trim(), out var o) ? o : 1;
            var po = int.TryParse(Do.Text.Trim(), out var d) ? d : PocetStran;
            od = Math.Clamp(od, 1, PocetStran);
            return (od, Math.Clamp(po, od, PocetStran));
        }
    }

    /// <summary>Samotest: rozsah strán.</summary>
    public void NastavRozsah(int od, int po)
    {
        Rozsah.IsChecked = true;
        Od.Text = od.ToString(CultureInfo.InvariantCulture);
        Do.Text = po.ToString(CultureInfo.InvariantCulture);
        Ukaz();
    }

    private void NaplnTlaciarne(string? ulozena)
    {
        try
        {
            using var server = new LocalPrintServer();
            var predvolena = LocalPrintServer.GetDefaultPrintQueue()?.FullName;
            foreach (var nazov in server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                         .Select(f => f.FullName).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
            {
                Tlaciarne.Items.Add(new ComboBoxItem { Content = nazov == predvolena ? Preklad.F($"{nazov} (predvolená)") : nazov, Tag = nazov });
            }

            Tlaciarne.SelectedItem = Tlaciarne.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == ulozena)
                                     ?? Tlaciarne.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == predvolena)
                                     ?? Tlaciarne.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        catch (Exception)
        {
            // zaraďovač tlače nebeží
        }
    }

    private void Ukaz()
    {
        if (_nacitavam)
        {
            return;
        }

        var (od, po) = RozsahStran;
        _strana = Math.Clamp(_strana, 0, PocetStran - 1);
        var kresba = StitkyService.Vizual(_rozlozenie, _strana, out var velkost);
        var obrazok = new DrawingImage(kresba.Drawing);
        obrazok.Freeze();
        Nahlad.Source = obrazok;
        Nahlad.Width = velkost.Width;
        Nahlad.Height = velkost.Height;
        var tlaci = _strana + 1 >= od && _strana + 1 <= po;
        Nahlad.Opacity = tlaci ? 1 : 0.35;
        CisloStrany.Text = Preklad.F($"strana {_strana + 1} z {PocetStran}") + (tlaci ? "" : " · " + Preklad.T("netlačí sa"));
        var stitkov = _rozlozenie.Stitky.Count(s => s.Strana + 1 >= od && s.Strana + 1 <= po);
        Suhrn.Text = Preklad.Pocet(stitkov, "{0} štítok", "{0} štítky", "{0} štítkov") + " · "
                     + Preklad.Pocet(po - od + 1, "{0} strana", "{0} strany", "{0} strán")
                     + (po - od + 1 < PocetStran ? " " + Preklad.F($"(z {PocetStran})") : "");
    }

    /// <summary>Obrázok aktuálnej strany náhľadu (samotest).</summary>
    public ImageSource? ObrazokStrany => Nahlad.Source;

    private void Rozsah_Changed(object sender, RoutedEventArgs e)
    {
        if (!_nacitavam && sender is TextBox)
        {
            Rozsah.IsChecked = true;
        }

        Ukaz();
    }

    private void Predosla_Click(object sender, RoutedEventArgs e)
    {
        _strana = Math.Max(0, _strana - 1);
        Ukaz();
    }

    private void Dalsia_Click(object sender, RoutedEventArgs e)
    {
        _strana = Math.Min(PocetStran - 1, _strana + 1);
        Ukaz();
    }

    private void Vytlacit_Click(object sender, RoutedEventArgs e)
    {
        if (Tlaciarne.SelectedItem is not ComboBoxItem { Tag: string tlaciaren })
        {
            Dialogy.Upozornenie(Preklad.T("Nie je nainštalovaná žiadna tlačiareň."));
            return;
        }

        try
        {
            using var server = new LocalPrintServer();
            using var fronta = ObojstrannaKresba.NajdiTlaciaren(server, tlaciaren);
            var tiket = (fronta.UserPrintTicket ?? fronta.DefaultPrintTicket).Clone();
            var s = _rozlozenie.Sablona;
            if (s.Harok)
            {
                tiket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
                tiket.PageOrientation = PageOrientation.Portrait;
            }
            else
            {
                var sirka = s.Sirka / 25.4 * 96;
                var vyska = s.Vyska / 25.4 * 96;
                tiket.PageMediaSize = new PageMediaSize(Math.Min(sirka, vyska), Math.Max(sirka, vyska));
                tiket.PageOrientation = sirka > vyska ? PageOrientation.Landscape : PageOrientation.Portrait;
            }

            tiket.CopyCount = 1;
            var (od, po) = RozsahStran;
            Dialogy.Cakaj(() => ObojstrannaKresba.Posli(fronta, tiket,
                new RozsahStranStitkov(new StitkyService.Strankovac(_rozlozenie), od - 1, po - od + 1), Preklad.T("Štítky EviTA")));
            using (var db = Databaza.Otvor())
            {
                Databaza.Zapis(db, KlucTlaciaren, tlaciaren);
                db.SaveChanges();
            }

            DialogResult = true;
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Tlač sa nepodarila."), ex);
        }
    }

    /// <summary>Len vybrané strany štítkov.</summary>
    public sealed class RozsahStranStitkov(DocumentPaginator zdroj, int prva, int pocet) : DocumentPaginator
    {
        public override bool IsPageCountValid => true;

        public override int PageCount => pocet;

        public override Size PageSize
        {
            get => zdroj.PageSize;
            set => zdroj.PageSize = value;
        }

        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber) => zdroj.GetPage(prva + pageNumber);
    }
}
