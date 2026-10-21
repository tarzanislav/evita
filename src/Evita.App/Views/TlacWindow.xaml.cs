using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Evita.Pomocne;
using Evita.Services;
using MigraDoc.Rendering;

namespace Evita.Views;

/// <summary>
/// Okno tlače EviTA: tlačiareň, kópie, obojstranne, 1/2/4/8 strán na list, opakovanie strán, brožúra, zväčšenie,
/// orámovanie, odtiene sivej a dierovač – s náhľadom listu. Voľby sa zapamätajú.
/// </summary>
public partial class TlacWindow : Window
{
    private readonly DocumentRenderer _vykreslenie;
    private readonly bool _naSirku;
    private readonly string _nazovUlohy;
    private readonly DispatcherTimer _oneskorenie = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private NahladDokumentu.StrankovacRozvrhu? _nahlad;
    private int _list;
    private bool _nacitavam = true;

    public TlacWindow(DocumentRenderer vykreslenie, bool naSirku, string nazovUlohy)
    {
        _vykreslenie = vykreslenie;
        _naSirku = naSirku;
        _nazovUlohy = nazovUlohy;
        InitializeComponent();
        Title = Preklad.F($"Vytlačiť – {nazovUlohy}");
        _oneskorenie.Tick += (_, _) =>
        {
            _oneskorenie.Stop();
            Prekresli();
        };

        var volby = VolbyTlace.Nacitaj();
        NaplnTlaciarne(volby.Tlaciaren);
        Kopie.Text = volby.Kopie.ToString(CultureInfo.InvariantCulture);
        Obojstranne.IsChecked = volby.Obojstranne;
        (volby.Rozmiestnenie switch
        {
            Rozmiestnenie.Dve => Dve,
            Rozmiestnenie.Styri => Styri,
            Rozmiestnenie.Osem => Osem,
            Rozmiestnenie.Brozura => Brozura,
            _ => Jedna,
        }).IsChecked = true;
        Opakovat.IsChecked = volby.OpakovatStranky;
        Zvacsenie.Value = Math.Round(volby.Zvacsenie * 100);
        Ramcek.IsChecked = volby.Ramcek;
        Siva.IsChecked = volby.OdtieneSivej;
        Dierovac.IsChecked = volby.Dierovac;
        DierovacMm.Text = volby.DierovacMm.ToString("0.#", CultureInfo.CurrentCulture);
        _nacitavam = false;
        Prekresli();
        AktualizujProfil();
    }

    /// <summary>Aktuálne voľby z okna.</summary>
    public VolbyTlace Volby => new VolbyTlace
    {
        Tlaciaren = (Tlaciarne.SelectedItem as ComboBoxItem)?.Tag as string,
        Kopie = int.TryParse(Kopie.Text.Trim(), out var kopie) ? kopie : 1,
        Obojstranne = Obojstranne.IsChecked == true,
        Rozmiestnenie = Dve.IsChecked == true ? Rozmiestnenie.Dve
            : Styri.IsChecked == true ? Rozmiestnenie.Styri
            : Osem.IsChecked == true ? Rozmiestnenie.Osem
            : Brozura.IsChecked == true ? Rozmiestnenie.Brozura
            : Rozmiestnenie.Jedna,
        OpakovatStranky = Opakovat.IsChecked == true,
        Zvacsenie = Zvacsenie.Value / 100.0,
        Ramcek = Ramcek.IsChecked == true,
        OdtieneSivej = Siva.IsChecked == true,
        Dierovac = Dierovac.IsChecked == true,
        DierovacMm = double.TryParse(DierovacMm.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) ? mm : 10,
    }.Opravene();

    /// <summary>Počet strán listov, ktoré sa vytlačia (pre samotest a súhrn).</summary>
    public int PocetListov => _nahlad?.PocetListov ?? 0;

    /// <summary>Nastavenie volieb z kódu (samotest).</summary>
    public void NastavVolby(VolbyTlace volby)
    {
        _nacitavam = true;
        Obojstranne.IsChecked = volby.Obojstranne;
        (volby.Rozmiestnenie switch
        {
            Rozmiestnenie.Dve => Dve,
            Rozmiestnenie.Styri => Styri,
            Rozmiestnenie.Osem => Osem,
            Rozmiestnenie.Brozura => Brozura,
            _ => Jedna,
        }).IsChecked = true;
        Opakovat.IsChecked = volby.OpakovatStranky;
        Zvacsenie.Value = Math.Round(volby.Zvacsenie * 100);
        Ramcek.IsChecked = volby.Ramcek;
        Siva.IsChecked = volby.OdtieneSivej;
        Dierovac.IsChecked = volby.Dierovac;
        DierovacMm.Text = volby.DierovacMm.ToString("0.#", CultureInfo.CurrentCulture);
        _nacitavam = false;
        Prekresli();
    }

    private void NaplnTlaciarne(string? ulozena)
    {
        try
        {
            using var server = new LocalPrintServer();
            var predvolena = LocalPrintServer.GetDefaultPrintQueue()?.FullName;
            var fronty = server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
                .Select(f => f.FullName).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var nazov in fronty)
            {
                Tlaciarne.Items.Add(new ComboBoxItem { Content = nazov == predvolena ? Preklad.F($"{nazov} (predvolená)") : nazov, Tag = nazov });
            }

            Tlaciarne.SelectedItem = Tlaciarne.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == ulozena)
                                     ?? Tlaciarne.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == predvolena)
                                     ?? Tlaciarne.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        catch (Exception)
        {
            // zoznam tlačiarní sa nedá zistiť (napr. vypnutá služba zaraďovača tlače)
        }
    }

    private void Volba_Changed(object sender, RoutedEventArgs e)
    {
        if (_nacitavam)
        {
            return;
        }

        _oneskorenie.Stop();
        _oneskorenie.Start();
    }

    private void Zvacsenie_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZvacsenieText is not null)
        {
            ZvacsenieText.Text = $"{Zvacsenie.Value:0} %";
        }

        Volba_Changed(sender, e);
    }

    private void Prekresli()
    {
        var volby = Volby;
        Opakovat.IsEnabled = volby.Rozmiestnenie is not (Rozmiestnenie.Jedna or Rozmiestnenie.Brozura);
        Dierovac.IsEnabled = volby.Rozmiestnenie != Rozmiestnenie.Brozura;
        DierovacMm.IsEnabled = Dierovac.IsEnabled && Dierovac.IsChecked == true;
        Obojstranne.IsEnabled = volby.Rozmiestnenie != Rozmiestnenie.Brozura;
        _nahlad = new NahladDokumentu.StrankovacRozvrhu(_vykreslenie, _naSirku, volby) { NahladDpi = 110 };
        if (ProfilText is not null)
        {
            AktualizujProfil();
        }
        _list = Math.Clamp(_list, 0, Math.Max(0, _nahlad.PocetListov - 1));
        UkazList();

        var stran = NahladDokumentu.PocetStran(_vykreslenie);
        var listov = volby.TlacitObojstranne ? (_nahlad.PocetListov + 1) / 2 : _nahlad.PocetListov;
        Suhrn.Text = Preklad.Pocet(stran, "{0} strana zostavy", "{0} strany zostavy", "{0} strán zostavy") + " → "
                     + Preklad.Pocet(listov, "{0} list papiera", "{0} listy papiera", "{0} listov papiera")
                     + (volby.Kopie > 1 ? $" × {volby.Kopie}" : "")
                     + (volby.TlacitObojstranne ? " · " + Preklad.T("obojstranne") : "")
                     + " · A4 " + (_nahlad.ListNaSirku ? Preklad.T("na šírku") : Preklad.T("na výšku"));
    }

    private void UkazList()
    {
        if (_nahlad is null || _nahlad.PocetListov == 0)
        {
            Nahlad.Source = null;
            CisloListu.Text = "";
            return;
        }

        var vizual = _nahlad.List(_list);
        var obrazok = new DrawingImage(vizual.Drawing);
        obrazok.Freeze();
        Nahlad.Source = obrazok;
        Nahlad.Width = _nahlad.PageSize.Width;
        Nahlad.Height = _nahlad.PageSize.Height;
        var volby = Volby;
        CisloListu.Text = volby.TlacitObojstranne
            ? Preklad.F($"list {_list / 2 + 1} z {(_nahlad.PocetListov + 1) / 2}, {(_list % 2 == 0 ? Preklad.T("predná strana") : Preklad.T("zadná strana"))}")
            : Preklad.F($"list {_list + 1} z {_nahlad.PocetListov}");
        Predosly.IsEnabled = _list > 0;
        Dalsi.IsEnabled = _list < _nahlad.PocetListov - 1;
    }

    private void Predosly_Click(object sender, RoutedEventArgs e)
    {
        _list = Math.Max(0, _list - 1);
        UkazList();
    }

    private void Dalsi_Click(object sender, RoutedEventArgs e)
    {
        _list = Math.Min((_nahlad?.PocetListov ?? 1) - 1, _list + 1);
        UkazList();
    }

    private void NastavitObojstranne_Click(object sender, RoutedEventArgs e)
    {
        if (Volby.Tlaciaren is not { } tlaciaren)
        {
            Dialogy.Upozornenie(Preklad.T("Nie je nainštalovaná žiadna tlačiareň."));
            return;
        }

        new ObojstrannaWindow(tlaciaren) { Owner = this }.ShowDialog();
        AktualizujProfil();
    }

    private void AktualizujProfil()
    {
        var profil = ProfilObojstrannej.Pre(Volby.Tlaciaren);
        ProfilText.Text = profil is null
            ? Preklad.T("Obojstranná tlač na tejto tlačiarni ešte nie je nastavená.")
            : Preklad.F($"Obojstranne: {profil.Popis}");
    }

    private void Vytlacit_Click(object sender, RoutedEventArgs e)
    {
        var volby = Volby;
        if (volby.Tlaciaren is null)
        {
            Dialogy.Upozornenie(Preklad.T("Nie je nainštalovaná žiadna tlačiareň."));
            return;
        }

        try
        {
            // obojstranná tlač: pri prvom použití tlačiarne sprievodca so skúšobnými listami
            var profil = volby.TlacitObojstranne ? ProfilObojstrannej.Pre(volby.Tlaciaren) : null;
            if (volby.TlacitObojstranne && profil is null)
            {
                if (Dialogy.Potvrd(Preklad.F($"Obojstranná tlač na tlačiarni „{volby.Tlaciaren}“ ešte nie je nastavená. Každá tlačiareň otáča list inak a zadná strana by mohla vyjsť hore nohami.\n\nSpustiť sprievodcu? Vytlačí 2 skúšobné listy a opýta sa, ako vyšli.\n\n(Nie = tlačiť s predvoleným nastavením.)")))
                {
                    var sprievodca = new ObojstrannaWindow(volby.Tlaciaren) { Owner = this };
                    sprievodca.ShowDialog();
                    AktualizujProfil();
                    if (sprievodca.Profil is null)
                    {
                        return;
                    }
                }

                profil = ProfilObojstrannej.Pre(volby.Tlaciaren) ?? new ProfilObojstrannej();
            }

            if (!Vytlac(volby, profil, potvrdenie: Dialogy.Potvrd))
            {
                return;
            }

            volby.Uloz();
            DialogResult = true;
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.F($"Tlač na „{volby.Tlaciaren}“ sa nepodarila."), ex);
        }
    }

    /// <summary>
    /// Strany na tlač podľa volieb a profilu obojstrannej tlače: pri obojstrannej tlači sa doplní prázdna strana na párny
    /// počet (ďalšia kópia nezačne na zadnej strane), zadné strany sa podľa profilu otočia o 180°. Pri ručnom otáčaní sú to
    /// dve úlohy – predné strany a po vložení listov zadné strany (podľa profilu v obrátenom poradí).
    /// </summary>
    public static List<(List<DrawingVisual> Strany, bool Duplex, int Kopie)> Ulohy(NahladDokumentu.StrankovacRozvrhu rozvrh, VolbyTlace volby, ProfilObojstrannej? profil)
    {
        var velkost = rozvrh.PageSize;
        var pocet = rozvrh.PocetListov;
        if (!volby.TlacitObojstranne)
        {
            return new() { (Enumerable.Range(0, pocet).Select(rozvrh.List).ToList(), false, volby.Kopie) };
        }

        profil ??= new ProfilObojstrannej();
        var parny = (pocet + 1) / 2 * 2;
        DrawingVisual Strana(int i) => i < pocet ? rozvrh.List(i) : ObojstrannaKresba.Prazdna(velkost);
        DrawingVisual Zadna(int i) => profil.Otocit(rozvrh.ListNaSirku) ? ObojstrannaKresba.Otocena(Strana(i), velkost) : Strana(i);

        if (profil.Rezim == RezimObojstrannej.Automaticky)
        {
            return new() { (Enumerable.Range(0, parny).Select(i => i % 2 == 0 ? Strana(i) : Zadna(i)).ToList(), true, volby.Kopie) };
        }

        var predne = new List<DrawingVisual>();
        var zadne = new List<DrawingVisual>();
        for (var kopia = 0; kopia < volby.Kopie; kopia++)
        {
            for (var i = 0; i < parny; i += 2)
            {
                predne.Add(Strana(i));
                zadne.Add(Zadna(i + 1));
            }
        }

        if (profil.ObratitPoradie)
        {
            zadne.Reverse();
        }

        return new() { (predne, false, 1), (zadne, false, 1) };
    }

    private bool Vytlac(VolbyTlace volby, ProfilObojstrannej? profil, Func<string, bool> potvrdenie)
    {
        using var server = new LocalPrintServer();
        using var fronta = ObojstrannaKresba.NajdiTlaciaren(server, volby.Tlaciaren!);
        var rozvrh = new NahladDokumentu.StrankovacRozvrhu(_vykreslenie, _naSirku, volby);
        var ulohy = Ulohy(rozvrh, volby, profil);
        for (var u = 0; u < ulohy.Count; u++)
        {
            var (strany, duplex, kopie) = ulohy[u];
            if (u == 1 && !potvrdenie(Preklad.F($"Predné strany ({strany.Count} listov) sa tlačia.\n\nKeď sú vytlačené, vezmite listy z tlačiarne, nemeňte ich poradie a vložte ich do zásobníka tak, ako pri skúšobných listoch (čistou stranou na tlač). Potom pokračujte – vytlačia sa zadné strany.")))
            {
                return false;
            }

            var tiket = NahladDokumentu.Tiket(fronta, volby with { Kopie = kopie }, rozvrh.ListNaSirku, duplex);
            Dialogy.Cakaj(() => ObojstrannaKresba.Posli(fronta, tiket, new ZoznamStran(strany.Count, rozvrh.PageSize, i => strany[i]),
                u == 0 ? _nazovUlohy : _nazovUlohy + " – " + Preklad.T("zadné strany")));
        }

        return true;
    }

    /// <summary>Strana listu ako obrázok (samotest – snímka náhľadu tlače).</summary>
    public DrawingImage? ObrazokListu(int index)
    {
        if (_nahlad is null || index >= _nahlad.PocetListov)
        {
            return null;
        }

        var obrazok = new DrawingImage(_nahlad.List(index).Drawing);
        obrazok.Freeze();
        return obrazok;
    }

    /// <summary>Uloženie volieb (aj bez tlače – samotest).</summary>
    public void UlozVolby() => Volby.Uloz();
}
