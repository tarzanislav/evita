using System.Printing;
using System.Windows;
using System.Windows.Controls;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.Views;

/// <summary>
/// Sprievodca obojstrannou tlačou pre jednu tlačiareň: vytlačí skúšobné listy (číslo, šípka HORE, krúžky dierovača)
/// a podľa odpovedí zistí, či tlačiareň otáča list sama, či treba zadnú stranu otočiť o 180° a pri ručnom otáčaní
/// aj poradie listov. Výsledok sa uloží pre túto tlačiareň.
/// </summary>
public partial class ObojstrannaWindow : Window
{
    private enum Krok
    {
        Uvod,
        OtazkyAutomaticky,
        RucnePredne,
        RucneZadne,
        OtazkaRucne,
        Hotovo,
    }

    private readonly string _tlaciaren;
    private Krok _krok = Krok.Uvod;

    public ObojstrannaWindow(string tlaciaren)
    {
        _tlaciaren = tlaciaren;
        InitializeComponent();
        Tlaciaren.Text = Preklad.F($"Tlačiareň: {tlaciaren}");
        Ukaz(Krok.Uvod);
    }

    /// <summary>Samotest: zobrazí otázky po skúšobnej tlači (bez tlače).</summary>
    public void UkazOtazky(bool rucne) => Ukaz(rucne ? Krok.OtazkaRucne : Krok.OtazkyAutomaticky);

    /// <summary>Samotest: odpoveď na otázky (index možnosti) a uloženie – rovnako ako tlačidlo Uložiť.</summary>
    public void Odpovedz(int prva, int druha = 0)
    {
        if (Moznosti.Children.OfType<RadioButton>().ElementAtOrDefault(prva) is { } a)
        {
            a.IsChecked = true;
        }

        if (Moznosti2.Children.OfType<RadioButton>().ElementAtOrDefault(druha) is { } b)
        {
            b.IsChecked = true;
        }

        Hlavne_Click(this, new RoutedEventArgs());
    }

    /// <summary>Uložené nastavenie (po dokončení sprievodcu).</summary>
    public ProfilObojstrannej? Profil { get; private set; }

    private void Ukaz(Krok krok)
    {
        _krok = krok;
        Moznosti.Children.Clear();
        Moznosti2.Children.Clear();
        Moznosti2Nadpis.Visibility = Visibility.Collapsed;
        switch (krok)
        {
            case Krok.Uvod:
                Nadpis.Text = Preklad.T("Nastavenie obojstrannej tlače");
                Text.Text = Preklad.T("Každá tlačiareň otáča list pri obojstrannej tlači trochu inak – zadná strana môže vyjsť hore nohami alebo v inom poradí. Sprievodca vytlačí skúšobné listy s číslom, šípkou HORE a krúžkami ako po dierovači a opýta sa, ako vyšli. EviTA si to zapamätá a pri každej obojstrannej tlači (aj 2 – 8 strán na list a brožúra) zadnú stranu podľa toho upraví.");
                Moznost(Moznosti, Preklad.T("Tlačiareň tlačí obojstranne sama (automatický duplex)"), true);
                Moznost(Moznosti, Preklad.T("Tlačiareň obojstranne netlačí – listy budem otáčať ručne"), false);
                Hlavne.Content = Preklad.T("Vytlačiť skúšobné listy");
                break;

            case Krok.OtazkyAutomaticky:
                Nadpis.Text = Preklad.T("Ako vyšli skúšobné listy?");
                Text.Text = Preklad.T("Vytlačili sa 2 listy. Vezmite list s číslom 1 (na výšku) a otočte ho ako stránku v knihe – okolo ľavej hrany. Čo je na zadnej strane?");
                Moznost(Moznosti, Preklad.T("Číslo 2 stojí rovno – šípky na oboch stranách ukazujú hore a krúžky sa proti svetlu prekrývajú"), true);
                Moznost(Moznosti, Preklad.T("Číslo 2 je hore nohami"), false);
                Moznost(Moznosti, Preklad.T("Zadná strana je prázdna – číslo 2 je na samostatnom liste"), false);
                Moznosti2Nadpis.Text = Preklad.T("Teraz list s číslom 3 (na šírku) – otočte ho tiež okolo ľavej (kratšej) hrany. Čo je na zadnej strane?");
                Moznosti2Nadpis.Visibility = Visibility.Visible;
                Moznost(Moznosti2, Preklad.T("Číslo 4 stojí rovno"), true);
                Moznost(Moznosti2, Preklad.T("Číslo 4 je hore nohami"), false);
                Moznost(Moznosti2, Preklad.T("Zadná strana je prázdna – číslo 4 je na samostatnom liste"), false);
                Hlavne.Content = Preklad.T("Uložiť");
                break;

            case Krok.RucnePredne:
                Nadpis.Text = Preklad.T("Ručné otáčanie listov – 1. časť");
                Text.Text = Preklad.T("Vytlačia sa predné strany 2 skúšobných listov (čísla 1 a 3).");
                Hlavne.Content = Preklad.T("Vytlačiť predné strany");
                break;

            case Krok.RucneZadne:
                Nadpis.Text = Preklad.T("Ručné otáčanie listov – 2. časť");
                Text.Text = Preklad.T("Vezmite oba vytlačené listy z tlačiarne, nemeňte ich poradie a vložte ich do zásobníka tak, aby sa tlačilo na druhú, čistú stranu (tak, ako to budete robiť vždy). Potom vytlačte zadné strany.");
                Hlavne.Content = Preklad.T("Vytlačiť zadné strany");
                break;

            case Krok.OtazkaRucne:
                Nadpis.Text = Preklad.T("Ako vyšli skúšobné listy?");
                Text.Text = Preklad.T("Vezmite list s číslom 1 a otočte ho ako stránku v knihe – okolo ľavej hrany. Čo je na zadnej strane?");
                Moznost(Moznosti, Preklad.T("Číslo 2, stojí rovno"), true);
                Moznost(Moznosti, Preklad.T("Číslo 2, ale hore nohami"), false);
                Moznost(Moznosti, Preklad.T("Číslo 4, stojí rovno (poradie listov je obrátené)"), false);
                Moznost(Moznosti, Preklad.T("Číslo 4, hore nohami"), false);
                Moznost(Moznosti, Preklad.T("Nič – zadné strany sa vytlačili na tú istú stranu ako predné (listy treba vložiť obrátene)"), false);
                Hlavne.Content = Preklad.T("Uložiť");
                break;

            case Krok.Hotovo:
                Nadpis.Text = Preklad.T("Hotovo");
                Text.Text = Preklad.F($"Nastavenie obojstrannej tlače pre túto tlačiareň je uložené ({Profil?.Popis}). Môžete ho kedykoľvek zopakovať tlačidlom „Nastaviť…“ v okne tlače.");
                Hlavne.Content = Preklad.T("Zavrieť");
                break;
        }
    }

    private static void Moznost(Panel panel, string text, bool vybrana) =>
        panel.Children.Add(new RadioButton
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            IsChecked = vybrana,
            GroupName = panel.Name,
            Margin = new Thickness(0, 3, 0, 3),
        });

    private static int Vybrana(Panel panel) =>
        panel.Children.OfType<RadioButton>().ToList().FindIndex(r => r.IsChecked == true);

    private void Hlavne_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            switch (_krok)
            {
                case Krok.Uvod when Vybrana(Moznosti) == 1:
                    Ukaz(Krok.RucnePredne);
                    break;

                case Krok.Uvod:
                    VytlacSkusobne(automaticky: true);
                    Ukaz(Krok.OtazkyAutomaticky);
                    break;

                case Krok.OtazkyAutomaticky:
                {
                    var naVysku = Vybrana(Moznosti);
                    var naSirku = Vybrana(Moznosti2);
                    if (naVysku == 2 || naSirku == 2)
                    {
                        Dialogy.Info(Preklad.T("Tlačiareň obojstranne netlačí (alebo je duplex vypnutý v jej nastavení). Nastavíme ručné otáčanie listov."));
                        Ukaz(Krok.RucnePredne);
                        break;
                    }

                    Uloz(new ProfilObojstrannej { Rezim = RezimObojstrannej.Automaticky, OtocitNaVysku = naVysku == 1, OtocitNaSirku = naSirku == 1 });
                    break;
                }

                case Krok.RucnePredne:
                    VytlacRucne(predne: true);
                    Ukaz(Krok.RucneZadne);
                    break;

                case Krok.RucneZadne:
                    VytlacRucne(predne: false);
                    Ukaz(Krok.OtazkaRucne);
                    break;

                case Krok.OtazkaRucne:
                {
                    var odpoved = Vybrana(Moznosti);
                    if (odpoved == 4)
                    {
                        Dialogy.Info(Preklad.T("Vložte nabudúce listy do zásobníka opačnou stranou. Skúsime to znova."));
                        Ukaz(Krok.RucnePredne);
                        break;
                    }

                    var hore = odpoved is 1 or 3;
                    Uloz(new ProfilObojstrannej
                    {
                        Rezim = RezimObojstrannej.Rucne, OtocitNaVysku = hore, OtocitNaSirku = hore, ObratitPoradie = odpoved is 2 or 3,
                    });
                    break;
                }

                case Krok.Hotovo:
                    DialogResult = true;
                    break;
            }
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Skúšobné listy sa nepodarilo vytlačiť."), ex);
        }
    }

    private void Uloz(ProfilObojstrannej profil)
    {
        ProfilObojstrannej.Uloz(_tlaciaren, profil);
        Profil = profil;
        Ukaz(Krok.Hotovo);
    }

    /// <summary>Automatický duplex: list 1/2 na výšku a list 3/4 na šírku, každý ako obojstranná úloha.</summary>
    private void VytlacSkusobne(bool automaticky)
    {
        using var server = new LocalPrintServer();
        using var fronta = ObojstrannaKresba.NajdiTlaciaren(server, _tlaciaren);
        foreach (var (prva, naSirku) in new[] { (1, false), (3, true) })
        {
            var velkost = RozvrhTlace.VelkostListu(naSirku);
            var tiket = NahladDokumentu.Tiket(fronta, new VolbyTlace(), naSirku, obojstranne: automaticky);
            Dialogy.Cakaj(() => ObojstrannaKresba.Posli(fronta, tiket,
                new ZoznamStran(2, velkost, i => ObojstrannaKresba.SkusobnaStrana(prva + i, velkost, i == 1)),
                Preklad.T("EviTA – skúšobný list obojstrannej tlače")));
        }
    }

    /// <summary>Ručné otáčanie: najprv predné strany 1 a 3, potom zadné 2 a 4 (jednostranne, na výšku).</summary>
    private void VytlacRucne(bool predne)
    {
        using var server = new LocalPrintServer();
        using var fronta = ObojstrannaKresba.NajdiTlaciaren(server, _tlaciaren);
        var velkost = RozvrhTlace.VelkostListu(false);
        var tiket = NahladDokumentu.Tiket(fronta, new VolbyTlace(), false, obojstranne: false);
        var cisla = predne ? new[] { 1, 3 } : new[] { 2, 4 };
        Dialogy.Cakaj(() => ObojstrannaKresba.Posli(fronta, tiket,
            new ZoznamStran(2, velkost, i => ObojstrannaKresba.SkusobnaStrana(cisla[i], velkost, !predne)),
            Preklad.T("EviTA – skúšobný list obojstrannej tlače")));
    }
}
