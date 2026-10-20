using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Evita.Data;
using Evita.Models;
using Evita.Services;
using Evita.ViewModels;
using Evita.Views;

namespace Evita;

/// <summary>
/// Automatická kontrola programu (spúšťa sa parametrom --samotest pri zostavení na GitHube).
/// Pracuje vo vlastnom dočasnom priečinku, skutočnej databázy sa nedotkne.
/// Vytvorí ukážkovú databázu, vyskúša hlavné operácie a otvorí všetky obrazovky a okná.
/// </summary>
internal static class Samotest
{
    public static List<string> Chyby { get; } = new();

    private static readonly StringBuilder Zaznam = new();

    public static int Spusti(App aplikacia)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EVITA_DATA_DIR")))
        {
            Environment.SetEnvironmentVariable("EVITA_DATA_DIR",
                Path.Combine(Path.GetTempPath(), "EviTA_samotest_" + Guid.NewGuid().ToString("N")));
        }

        Directory.CreateDirectory(Databaza.Priecinok);
        var subor = Path.Combine(Databaza.Priecinok, "samotest.log");
        var vysledok = 1;

        try
        {
            Krok($"priečinok: {Databaza.Priecinok}");
            if (File.Exists(Databaza.Cesta))
            {
                File.Delete(Databaza.Cesta);
            }

            Databaza.Inicializuj();
            Krok("databáza vytvorená");

            var servis = new ServisService();
            var sluzba = new PolozkaService();
            var ciselniky = new CiselnikService();

            servis.NacitatUkazkoveData();
            var riadky = sluzba.NacitajRiadky();
            Over(riadky.Count == 200, $"ukážkové dáta: 200 položiek (je {riadky.Count})");
            Over(riadky.Select(r => r.PrirastkoveCislo).Distinct().Count() == 200, "prírastkové čísla sú jedinečné");
            Over(riadky.Max(r => r.PrirastkoveCislo) == 200, "posledné prírastkové číslo je 200");
            Over(ciselniky.Nacitaj(TypCiselnika.Osoby).Count == 30, "30 osôb");
            Over(ciselniky.Nacitaj(TypCiselnika.Miestnosti).Count == 25, "25 miestností");
            Over(ciselniky.Nacitaj(TypCiselnika.Kategorie).Count == 15, "15 kategórií");
            Over(Databaza.SuUkazkoveData(), "príznak ukážkových dát");

            // pridanie, úprava, história
            var vzor = sluzba.Nacitaj(riadky.First(r => !r.Vyradena && r.MiestnostId is not null).Id)!;
            var nove = sluzba.Pridaj(vzor, 3, new[] { "TEST-1", "TEST-2" });
            Over(nove.Count == 3, "hromadné pridanie 3 ks");
            var nova = sluzba.Nacitaj(nove[0])!;
            Over(nova.PrirastkoveCislo == 201, $"nová položka má číslo 201 (má {nova.PrirastkoveCislo})");
            Over(nova.SerioveCislo == "TEST-1", "sériové číslo pri hromadnom pridaní");

            nova.Nazov += " (upravené)";
            nova.Cena = 123.45m;
            sluzba.Uloz(nova);
            var historia = sluzba.Historia(nove[0]);
            Over(historia.Count(h => h.Akcia == PolozkaService.AkciaZmena) == 2, $"zmena 2 polí v histórii (je {historia.Count})");

            var zastarana = sluzba.Nacitaj(nove[0])!;
            zastarana.Verzia = 1;
            var konflikt = false;
            try
            {
                sluzba.Uloz(zastarana);
            }
            catch (EvitaException)
            {
                konflikt = true;
            }

            Over(konflikt, "súčasná úprava z dvoch miest je zachytená");

            Over(sluzba.HromadnaZmena(nove, p => p.MiestnostId = null) >= 1, "hromadná zmena");
            Over(sluzba.Vyrad(nove.Take(2), DateTime.Today, "test") == 2, "vyradenie");
            Over(sluzba.Obnov(nove.Take(1)) == 1, "obnovenie");
            Over(sluzba.Vymaz(nove.Skip(2)) == 1, "vymazanie");
            var dalsia = sluzba.Pridaj(vzor, 1, Array.Empty<string>());
            Over(sluzba.Nacitaj(dalsia[0])!.PrirastkoveCislo == 204, "vymazané číslo sa znova nepoužije");

            // číselníky
            var novaKategoria = ciselniky.Uloz(TypCiselnika.Kategorie, 0, "testovacia kategória", null, null);
            ciselniky.PrepniAktivnu(TypCiselnika.Kategorie, novaKategoria);
            ciselniky.Vymaz(TypCiselnika.Kategorie, novaKategoria);
            var duplicita = false;
            try
            {
                ciselniky.Uloz(TypCiselnika.Kategorie, 0, "Notebook", null, null);
            }
            catch (EvitaException)
            {
                duplicita = true;
            }

            Over(duplicita, "duplicitný názov kategórie je zachytený");
            Krok("služby v poriadku");

            // obrazovky a okná
            var okno = new MainWindow();
            aplikacia.MainWindow = okno;
            okno.Show();
            Pumpuj();
            var model = (MainViewModel)okno.DataContext;
            foreach (var sekcia in new[] { "dashboard", "evidencia", "kategorie", "miestnosti", "osoby", "servis", "dashboard" })
            {
                okno.OznacSekciu(sekcia);
                Pumpuj();
                Krok($"obrazovka: {sekcia}");
            }

            model.Dashboard.OtvorEvidenciuCommand.Execute("zaruky");
            Pumpuj();
            Over(model.Evidencia.Pohlad is not null && model.Evidencia.Pohlad.Count > 0, "rýchly filter záruk nájde položky");
            model.Evidencia.VycistitFiltreCommand.Execute(null);
            model.Evidencia.HladanyText = "ucebna";
            model.Evidencia.FilterStav = 1;
            Pumpuj();

            OtvorAZatvor(new PolozkaEditWindow(new PolozkaEditViewModel(riadky[0].Id, null)), "karta položky");
            OtvorAZatvor(new PolozkaEditWindow(new PolozkaEditViewModel(null, riadky[0].Id)), "kópia položky");
            OtvorAZatvor(new PolozkaEditWindow(new PolozkaEditViewModel(null, null)), "nová položka");
            OtvorAZatvor(new HromadnaZmenaWindow(new HromadnaZmenaViewModel(new List<int> { riadky[0].Id })), "hromadná zmena");
            OtvorAZatvor(new VymazanieWindow(), "vymazanie dát");
            OtvorAZatvor(new PrveSpustenieWindow(), "prvé spustenie");
            OtvorAZatvor(new VstupWindow("Test", "Otázka", "text", true), "vstup");
            okno.Close();
            Pumpuj();

            // záloha a nový začiatok
            var zaloha = servis.Zalohuj();
            Over(File.Exists(zaloha) && new FileInfo(zaloha).Length > 0, "záloha ZIP");
            servis.VymazatData(ponechatMiestnosti: false, ponechatOsoby: false);
            Over(sluzba.NacitajRiadky().Count == 0, "vymazanie dát");
            Over(!Databaza.SuUkazkoveData(), "príznak ukážkových dát zrušený");
            vzor.MiestnostId = null;
            vzor.OsobaId = null;
            var prva = sluzba.Pridaj(vzor, 1, Array.Empty<string>());
            Over(sluzba.Nacitaj(prva[0])!.PrirastkoveCislo == 1, "po vymazaní začína číslovanie od 1");

            foreach (var hlasenie in Dialogy_Hlasenia())
            {
                Krok("hlásenie: " + hlasenie);
            }

            if (Chyby.Count > 0)
            {
                foreach (var chyba in Chyby)
                {
                    Krok("CHYBA: " + chyba);
                }
            }
            else
            {
                Krok("SAMOTEST OK");
                vysledok = 0;
            }
        }
        catch (Exception ex)
        {
            Krok("CHYBA: " + ex);
            foreach (var chyba in Chyby)
            {
                Krok("CHYBA: " + chyba);
            }
        }

        File.WriteAllText(subor, Zaznam.ToString(), Encoding.UTF8);
        return vysledok;
    }

    private static IEnumerable<string> Dialogy_Hlasenia() => Pomocne.Dialogy.TicheHlasenia;

    private static void OtvorAZatvor(Window okno, string nazov)
    {
        okno.Show();
        Pumpuj();
        okno.Close();
        Pumpuj();
        Krok($"okno: {nazov}");
    }

    private static void Over(bool podmienka, string popis)
    {
        if (!podmienka)
        {
            throw new InvalidOperationException("Neplatí: " + popis);
        }

        Krok("ok – " + popis);
    }

    private static void Krok(string text) => Zaznam.AppendLine($"{DateTime.Now:HH:mm:ss} {text}");

    /// <summary>Nechá WPF dokončiť rozloženie a vykreslenie (ako pri bežnom behu).</summary>
    private static void Pumpuj()
    {
        var ramec = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => ramec.Continue = false));
        Dispatcher.PushFrame(ramec);
    }
}
