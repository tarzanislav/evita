using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;
using Microsoft.Win32;

namespace Evita.ViewModels;

public sealed record VolbaPeriody(PeriodaZaloh Perioda, string Nazov);

public sealed record ZaznamZalohyRiadok(DateTime Cas, string Typ, string Subor, string Velkost, string Vysledok, string? Cesta);

public partial class ServisViewModel : ObservableObject, IObnovitelny
{
    private readonly MainViewModel _hlavne;
    private readonly ServisService _servis = new();
    private readonly ZalohaService _zalohy = new();
    private readonly DiagnostikaService _diagnostika = new();
    private readonly AktualizaciaService _aktualizacie = new();
    private bool _nacitavam;
    private readonly StringBuilder _zaznamProtokolu = new();

    public ServisViewModel(MainViewModel hlavne)
    {
        _hlavne = hlavne;
    }

    public List<VolbaPeriody> Periody { get; } =
        Enum.GetValues<PeriodaZaloh>().Select(p => new VolbaPeriody(p, ZalohaService.NazovPeriody(p))).ToList();

    // prehľad
    [ObservableProperty] private string _cestaDatabazy = "";
    [ObservableProperty] private string _velkost = "";
    [ObservableProperty] private string _pocty = "";
    [ObservableProperty] private string _verziaSchemy = "";
    [ObservableProperty] private bool _ukazkoveData;

    // zálohy
    [ObservableProperty] private bool _zalohaZapnuta;
    [ObservableProperty] private PeriodaZaloh _perioda;
    [ObservableProperty] private string _priecinokZaloh = "";
    [ObservableProperty] private int _ponechat;
    [ObservableProperty] private bool _planovacWindows;
    [ObservableProperty] private string _casZalohy = "";
    [ObservableProperty] private bool _automatickaSHeslom;
    [ObservableProperty] private string _hesloAutomatickychStav = "";
    [ObservableProperty] private string _stavZaloh = "";
    [ObservableProperty] private string _planovacStav = "";
    [ObservableProperty] private List<ZaznamZalohyRiadok> _zaznamy = new();

    // kontrola a oprava
    [ObservableProperty] private string _protokol = "";

    // aktualizácie
    [ObservableProperty] private bool _kontrolovatAktualizacie;
    [ObservableProperty] private string _aktualizaciaStav = "";

    public string AktualnaVerziaText => $"EviTA {AktualizaciaService.AktualnaVerzia}";

    public string StrankaVydani => AktualizaciaService.StrankaVydani;

    partial void OnKontrolovatAktualizacieChanged(bool value)
    {
        if (!_nacitavam)
        {
            _aktualizacie.NastavKontroluPriSpusteni(value);
        }
    }

    [RelayCommand]
    private async Task SkontrolovatAktualizacie()
    {
        AktualizaciaStav = "Zisťujem, či je dostupná nová verzia…";
        try
        {
            var verzia = await _aktualizacie.SkontrolujAsync();
            if (verzia is null)
            {
                AktualizaciaStav = $"Máte najnovšiu verziu ({AktualizaciaService.AktualnaVerzia}). Skontrolované {Sk.DatumCas(DateTime.Now)}.";
            }
            else
            {
                AktualizaciaStav = $"Je dostupná nová verzia {verzia.Verzia}.";
                _hlavne.ZobrazitAktualizaciu(verzia);
            }
        }
        catch (EvitaException chyba)
        {
            AktualizaciaStav = chyba.Message;
        }
    }

    [RelayCommand]
    private void OtvoritStrankuVydani() =>
        Process.Start(new ProcessStartInfo(AktualizaciaService.StrankaVydani) { UseShellExecute = true });

    public void Obnov()
    {
        var info = _servis.Info();
        CestaDatabazy = info.Cesta;
        Velkost = info.VelkostBajtov < 1024 * 1024
            ? $"{info.VelkostBajtov / 1024.0:0.0} kB"
            : $"{info.VelkostBajtov / 1024.0 / 1024.0:0.0} MB";
        Pocty = $"{info.Polozky} položiek (z toho {info.Vyradene} vyradených), {info.Kategorie} kategórií, " +
                $"{info.Miestnosti} miestností, {info.Osoby} osôb, {info.ZaznamyHistorie} záznamov v histórii zmien";
        VerziaSchemy = info.VerziaSchemy.ToString();
        UkazkoveData = info.UkazkoveData;

        _nacitavam = true;
        KontrolovatAktualizacie = _aktualizacie.KontrolovatPriSpusteni();
        if (AktualizaciaStav.Length == 0 && _aktualizacie.PoslednaKontrola() is { } kontrola)
        {
            AktualizaciaStav = $"Posledná kontrola: {Sk.DatumCas(kontrola)}.";
        }

        _nacitavam = false;

        var nastavenia = _zalohy.NacitajNastavenia();
        ZalohaZapnuta = nastavenia.Zapnuta;
        Perioda = nastavenia.Perioda;
        PriecinokZaloh = nastavenia.Priecinok;
        Ponechat = nastavenia.Ponechat;
        PlanovacWindows = nastavenia.PlanovacWindows;
        CasZalohy = nastavenia.Cas;
        AutomatickaSHeslom = nastavenia.AutomatickaSHeslom;
        HesloAutomatickychStav = _zalohy.JeHesloAutomatickychNastavene() ? "Heslo je nastavené." : "Heslo nie je nastavené.";
        AktualizujStavZaloh(nastavenia);
    }

    private void AktualizujStavZaloh(NastaveniaZaloh nastavenia)
    {
        var posledna = _zalohy.PoslednaUspesna();
        var text = posledna is null ? "Zatiaľ nebola urobená žiadna záloha." : $"Posledná záloha: {Sk.DatumCas(posledna.Value)}.";
        if (nastavenia.Zapnuta)
        {
            text += posledna is null
                ? " Prvá pravidelná záloha sa urobí pri najbližšom spustení alebo zatvorení programu."
                : $" Ďalšia pravidelná záloha ({ZalohaService.NazovPeriody(nastavenia.Perioda)}): od {Sk.Datum(ZalohaService.DalsiTermin(posledna.Value, nastavenia.Perioda))}.";
        }
        else
        {
            text += " Pravidelná záloha je vypnutá.";
        }

        if (_zalohy.PoslednyZaznam() is { Uspech: false } chybna)
        {
            text += $"\nPosledný pokus o zálohu {Sk.DatumCas(chybna.Cas)} sa nepodaril: {chybna.Chyba}";
        }

        StavZaloh = text;
        PlanovacStav = nastavenia.PlanovacWindows
            ? (_zalohy.JePlanovacNastaveny()
                ? $"Úloha v Plánovači úloh Windows je nastavená ({ZalohaService.NazovPeriody(nastavenia.Perioda)} o {nastavenia.Cas})."
                : "Úloha v Plánovači úloh Windows sa nenašla – uložte nastavenia znova.")
            : "";

        Zaznamy = _zalohy.Zaznamy().Select(z => new ZaznamZalohyRiadok(
            z.Cas,
            ZalohaService.PopisTypu(z),
            z.Cesta is null ? "" : Path.GetFileName(z.Cesta),
            z.Velkost > 0 ? $"{z.Velkost / 1024.0:0} kB" : "",
            z.Uspech ? "v poriadku" : $"chyba: {z.Chyba}",
            z.Cesta)).ToList();
    }

    // ------------------------------------------------------------------ zálohy

    [RelayCommand]
    private void UlozitNastavenia()
    {
        var nastavenia = new NastaveniaZaloh(ZalohaZapnuta, Perioda, PriecinokZaloh, Ponechat, PlanovacWindows, CasZalohy, AutomatickaSHeslom);
        Dialogy.Cakaj(() => _zalohy.UlozNastavenia(nastavenia));
        Obnov();
        Dialogy.Info("Nastavenia záloh sú uložené.");
    }

    [RelayCommand]
    private void NastavitHesloAutomatickych()
    {
        var heslo = HesloWindow.Nove(
            "Týmto heslom sa budú šifrovať automatické zálohy (AES-256). Program si ho uloží chránené ochranou Windows " +
            "a pri zálohe ho použije sám. Pri obnove zo zálohy ho budete musieť zadať – dobre si ho uschovajte.");
        if (heslo is null)
        {
            return;
        }

        _zalohy.NastavHesloAutomatickych(heslo);
        HesloAutomatickychStav = "Heslo je nastavené.";
        AutomatickaSHeslom = true;
        Dialogy.Info("Heslo pre automatické zálohy je nastavené. Nezabudnite kliknúť na „Uložiť nastavenia“.");
    }

    [RelayCommand]
    private void VybratPriecinok()
    {
        var vyber = new OpenFolderDialog
        {
            Title = "Priečinok na zálohy",
            InitialDirectory = Directory.Exists(PriecinokZaloh) ? PriecinokZaloh : null,
        };
        if (vyber.ShowDialog() == true)
        {
            PriecinokZaloh = vyber.FolderName;
        }
    }

    [RelayCommand]
    private void ZalohovatTeraz() => Zalohuj(null);

    [RelayCommand]
    private void ZalohovatSHeslom()
    {
        var heslo = HesloWindow.Nove();
        if (heslo is not null)
        {
            Zalohuj(heslo);
        }
    }

    private void Zalohuj(string? heslo)
    {
        var cesta = "";
        Dialogy.Cakaj(() => cesta = _zalohy.Zalohuj(TypZalohy.Rucna, heslo));
        Obnov();
        Dialogy.Info($"Záloha je hotová:\n\n{cesta}" +
                     (heslo is null ? "" : "\n\nZáloha je zašifrovaná. Otvoriť ju dokáže EviTA alebo program 7-Zip, Prieskumník Windows nie."));
    }

    [RelayCommand]
    private void OtvoritPriecinokZaloh()
    {
        Directory.CreateDirectory(PriecinokZaloh);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{PriecinokZaloh}\"") { UseShellExecute = true });
    }

    // ------------------------------------------------------------------ obnova

    [RelayCommand]
    private void ObnovitZoZalohy()
    {
        var vyber = new OpenFileDialog
        {
            Title = "Vyberte zálohu EviTA",
            Filter = "Zálohy EviTA (*.zip)|*.zip",
            InitialDirectory = Directory.Exists(PriecinokZaloh) ? PriecinokZaloh : null,
        };
        if (vyber.ShowDialog() != true)
        {
            return;
        }

        string? heslo = null;
        InfoOZalohe? info = null;
        while (info is null)
        {
            try
            {
                info = _zalohy.PrecitajInfo(vyber.FileName, heslo);
            }
            catch (HesloPotrebneException)
            {
                heslo = HesloWindow.Zadaj();
                if (heslo is null)
                {
                    return;
                }
            }
            catch (EvitaException chyba) when (heslo is not null)
            {
                Dialogy.Upozornenie(chyba.Message);
                heslo = HesloWindow.Zadaj();
                if (heslo is null)
                {
                    return;
                }
            }
        }

        var otazka = new StringBuilder("Obnoviť databázu zo zálohy?\n\n");
        otazka.AppendLine($"Súbor: {Path.GetFileName(info.Subor)}");
        if (info.Vytvorene is { } vytvorene)
        {
            otazka.AppendLine($"Vytvorená: {Sk.DatumCas(vytvorene)}");
        }

        otazka.AppendLine($"Položiek: {info.Polozky}" + (info.UkazkoveData ? " (ukážkové dáta)" : ""));
        if (!string.IsNullOrEmpty(info.Pocitac))
        {
            otazka.AppendLine($"Počítač: {info.Pocitac}");
        }

        if (!string.IsNullOrEmpty(info.VerziaProgramu))
        {
            otazka.AppendLine($"Verzia programu: {info.VerziaProgramu}");
        }

        otazka.Append("\nSúčasné dáta sa nahradia dátami zo zálohy. Predtým sa automaticky zálohujú, takže krok sa dá vrátiť.");
        if (!Dialogy.Potvrd(otazka.ToString()))
        {
            return;
        }

        string? bezpecnostna = null;
        Dialogy.Cakaj(() => bezpecnostna = _zalohy.Obnov(vyber.FileName, heslo));
        _hlavne.DataZmenene();
        Obnov();
        Dialogy.Info("Databáza je obnovená zo zálohy." +
                     (bezpecnostna is null ? "" : $"\n\nPredchádzajúci stav je uložený tu:\n{bezpecnostna}"));
    }

    // ------------------------------------------------------------------ kontrola a oprava

    [RelayCommand]
    private void RychlaKontrola() => Spusti(_diagnostika.RychlaKontrola);

    [RelayCommand]
    private void UplnaKontrola() => Spusti(_diagnostika.UplnaKontrola);

    [RelayCommand]
    private void KontrolaVazieb() => Spusti(_diagnostika.KontrolaVazieb);

    [RelayCommand]
    private void KontrolaDat() => Spusti(_diagnostika.KontrolaDat);

    [RelayCommand]
    private void Optimalizovat() => Spusti(_diagnostika.Optimalizuj);

    [RelayCommand]
    private void ObnovitIndexy() => Spusti(_diagnostika.ObnovIndexy);

    [RelayCommand]
    private void Kompaktovat()
    {
        Spusti(_diagnostika.Kompaktuj);
        Obnov();
    }

    [RelayCommand]
    private void Zachranit()
    {
        if (!Dialogy.Potvrd(
                "Záchrana dát prenesie všetky čitateľné záznamy do novej databázy a tou nahradí súčasnú.\n\n" +
                "Používa sa len vtedy, keď kontrola integrity hlási poškodenie a nie je k dispozícii dobrá záloha. " +
                "Pôvodný súbor sa predtým uloží do priečinka záloh.\n\nPokračovať?"))
        {
            return;
        }

        Spusti(_diagnostika.Zachran);
        _hlavne.DataZmenene();
        Obnov();
    }

    [RelayCommand]
    private void UlozitProtokol()
    {
        if (_zaznamProtokolu.Length == 0)
        {
            Dialogy.Info("Protokol je zatiaľ prázdny. Najprv spustite niektorý nástroj.");
            return;
        }

        var ulozit = new SaveFileDialog
        {
            Title = "Uložiť protokol",
            Filter = "Textový súbor (*.txt)|*.txt",
            FileName = $"EviTA_protokol_{DateTime.Now:yyyy-MM-dd_HHmm}.txt",
        };
        if (ulozit.ShowDialog() == true)
        {
            File.WriteAllText(ulozit.FileName, _zaznamProtokolu.ToString(), Encoding.UTF8);
        }
    }

    [RelayCommand]
    private void VymazatProtokol()
    {
        _zaznamProtokolu.Clear();
        Protokol = "";
    }

    private void Spusti(Func<VysledokKontroly> nastroj)
    {
        VysledokKontroly? vysledok = null;
        Dialogy.Cakaj(() => vysledok = nastroj());
        if (vysledok is null)
        {
            return;
        }

        _zaznamProtokolu.AppendLine($"── {Sk.DatumCas(DateTime.Now)}  {vysledok.Nastroj}: {(vysledok.Ok ? "v poriadku" : "našli sa problémy")}");
        foreach (var riadok in vysledok.Riadky)
        {
            _zaznamProtokolu.AppendLine("   " + riadok);
        }

        _zaznamProtokolu.AppendLine();
        Protokol = _zaznamProtokolu.ToString();
    }

    // ------------------------------------------------------------------ ukážkové dáta

    [RelayCommand]
    private void NacitatUkazkove()
    {
        var info = _servis.Info();
        var otazka = info.Polozky > 0
            ? "Načítať ukážkové dáta? Súčasné položky, história, miestnosti a osoby sa najprv zálohujú a potom vymažú."
            : "Načítať ukážkové dáta (200 položiek, 30 osôb, 25 miestností)?";
        if (!Dialogy.Potvrd(otazka))
        {
            return;
        }

        string? zaloha = null;
        Dialogy.Cakaj(() => zaloha = _servis.NacitatUkazkoveData());
        _hlavne.DataZmenene();
        Obnov();
        Dialogy.Info(zaloha is null
            ? "Ukážkové dáta sú načítané."
            : $"Ukážkové dáta sú načítané. Pôvodné dáta sú zálohované tu:\n\n{zaloha}");
    }

    [RelayCommand]
    private void VymazatVsetko()
    {
        var okno = new VymazanieWindow { Owner = Dialogy.Vlastnik };
        if (okno.ShowDialog() != true)
        {
            return;
        }

        var zaloha = "";
        Dialogy.Cakaj(() => zaloha = _servis.VymazatData(okno.PonechatMiestnosti, okno.PonechatOsoby));
        _hlavne.DataZmenene();
        Obnov();
        Dialogy.Info($"Dáta sú vymazané a evidencia začína od prírastkového čísla 1.\n\nZáloha pred vymazaním:\n{zaloha}");
    }

    public string CestaKZalohamPredvolene => Databaza.PriecinokZaloh;
}
