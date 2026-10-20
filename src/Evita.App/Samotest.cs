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

        // Kalendárium tiež v dočasnom priečinku – skutočného sa test nedotkne
        Environment.SetEnvironmentVariable("KALENDARIUM_DIR",
            Path.Combine(Environment.GetEnvironmentVariable("EVITA_DATA_DIR")!, "Kalendarium"));

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

            // Kalendárium: meniny a sviatky
            var kalendar = Kalendarium.KalendariumDb.Spolocna;
            if (File.Exists(kalendar.Cesta))
            {
                File.Delete(kalendar.Cesta);
            }

            kalendar.Inicializuj();
            kalendar.Inicializuj(); // druhé spustenie nesmie nič zdvojiť
            Over(kalendar.PocetMien() == 467, $"kalendár mien má 467 mien (má {kalendar.PocetMien()})");
            Over(string.Join(",", kalendar.Meniny(new DateOnly(2026, 10, 20))) == "Vendelín", "meniny 20. 10.: Vendelín");
            Over(string.Join(",", kalendar.Meniny(new DateOnly(2026, 12, 24))) == "Adam,Eva", "meniny 24. 12.: Adam, Eva");
            Over(kalendar.Meniny(new DateOnly(2026, 1, 1)).Count == 0, "1. 1. nemá meniny nikto");
            Over(string.Join(",", kalendar.Meniny(new DateOnly(2028, 2, 29))) == "Radomír", "meniny 29. 2.: Radomír");
            Over(Kalendarium.KalendariumDb.VelkonocnaNedela(2025) == new DateOnly(2025, 4, 20), "Veľká noc 2025");
            Over(Kalendarium.KalendariumDb.VelkonocnaNedela(2026) == new DateOnly(2026, 4, 5), "Veľká noc 2026");
            Over(Kalendarium.KalendariumDb.VelkonocnaNedela(2027) == new DateOnly(2027, 3, 28), "Veľká noc 2027");
            int DniPokoja(int rok) => kalendar.Sviatky(rok).Where(x => x.DenPracovnehoPokoja).Select(x => x.Datum).Distinct().Count();
            Over(DniPokoja(2025) == 14, $"2025: 14 dní pracovného pokoja (je {DniPokoja(2025)})");
            Over(DniPokoja(2026) == 11, $"2026: 11 dní pracovného pokoja (je {DniPokoja(2026)})");
            Over(DniPokoja(2027) == 13, $"2027: 13 dní pracovného pokoja (je {DniPokoja(2027)})");
            Over(kalendar.JeSviatokVolno(new DateOnly(2025, 11, 17)) && !kalendar.JeSviatokVolno(new DateOnly(2026, 11, 17)), "17. november od 2026 nie je voľno");
            Over(!kalendar.JeSviatokVolno(new DateOnly(2026, 5, 8)) && kalendar.JeSviatokVolno(new DateOnly(2027, 5, 8)), "8. máj: v 2026 výnimka, v 2027 voľno");
            Over(!kalendar.JeSviatokVolno(new DateOnly(2026, 9, 1)) && !kalendar.JeSviatokVolno(new DateOnly(2026, 10, 28)), "1. 9. a 28. 10. nie sú voľno");
            Over(kalendar.JeSviatokVolno(new DateOnly(2026, 4, 3)) && kalendar.JeSviatokVolno(new DateOnly(2026, 4, 6)), "Veľký piatok a Veľkonočný pondelok 2026");
            Over(kalendar.NajblizsiDenPracovnehoPokoja(new DateOnly(2026, 10, 20)) is { Nazov: "Sviatok Všetkých svätých" } najblizsi
                 && najblizsi.Datum == new DateOnly(2026, 11, 1), "najbližší deň pracovného pokoja po 20. 10. 2026");
            var november = kalendar.SviatkyDna(new DateOnly(2026, 11, 17)).Single(x => x.Druh == Kalendarium.DruhSviatku.StatnySviatok);
            kalendar.NastavDenPracovnehoPokoja(november.Id, 2026, true);
            Over(kalendar.JeSviatokVolno(new DateOnly(2026, 11, 17)), "ručná zmena dňa pracovného pokoja");
            kalendar.ZrusZmenu(november.Id, 2026);
            Over(!kalendar.JeSviatokVolno(new DateOnly(2026, 11, 17)), "zrušenie ručnej zmeny");
            Krok("Kalendárium v poriadku");

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
            vzor.SerioveCislo = null;
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

            // aktualizácia štruktúry zo staršej verzie (simulácia databázy z verzie 0.1)
            Vykonaj("DROP TABLE \"ZaznamyZaloh\"; UPDATE \"Nastavenia\" SET \"Hodnota\" = '1' WHERE \"Kluc\" = 'Schema.Verzia';");
            Databaza.Inicializuj();
            Over(Databaza.VerziaVDatabaze() == Databaza.VerziaSchemy, "aktualizácia štruktúry z verzie 1");
            Over(Directory.GetFiles(Databaza.PriecinokZaloh, "*_EviTA_pred_upravou_struktury_*.db").Length == 1, "záloha pred aktualizáciou štruktúry");

            // zálohy
            var zalohy = new ZalohaService();
            var nastavenia = zalohy.NacitajNastavenia();
            Over(nastavenia.Zapnuta && nastavenia.Perioda == PeriodaZaloh.Tyzdenne && nastavenia.Ponechat == 10, "predvolené nastavenia záloh");
            Over(zalohy.JeCasNaZalohu(nastavenia), "bez zálohy je čas na zálohu");
            var pocet = sluzba.NacitajRiadky().Count;
            var obycajna = zalohy.Zalohuj(TypZalohy.Rucna);
            using (var zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(obycajna))
            {
                Over(zip.GetEntry("kalendarium.db") is not null, "záloha obsahuje aj Kalendárium");
            }

            Over(File.Exists(obycajna) && !zalohy.JeZasifrovana(obycajna), "ručná záloha ZIP");
            Over(!zalohy.JeCasNaZalohu(nastavenia), "po zálohe ešte nie je čas na ďalšiu");
            Over(zalohy.PrecitajInfo(obycajna, null).Polozky == pocet, "popis zálohy sedí");

            const string heslo = "tajne-heslo-123";
            var sHeslom = zalohy.Zalohuj(TypZalohy.Rucna, heslo);
            Over(zalohy.JeZasifrovana(sHeslom), "záloha s heslom je zašifrovaná");
            Over(Ocakavaj<HesloPotrebneException>(() => zalohy.PrecitajInfo(sHeslom, null)), "bez hesla sa záloha neotvorí");
            Over(Ocakavaj<EvitaException>(() => zalohy.PrecitajInfo(sHeslom, "zle-heslo")), "nesprávne heslo je odmietnuté");
            Over(zalohy.PrecitajInfo(sHeslom, heslo) is { Zasifrovana: true } infoHeslo && infoHeslo.Polozky == pocet, "správne heslo otvorí zálohu");

            // obnova zo zálohy s heslom
            sluzba.Pridaj(vzor, 2, Array.Empty<string>());
            Over(sluzba.NacitajRiadky().Count == pocet + 2, "pridané položky po zálohe");
            var bezpecnostna = zalohy.Obnov(sHeslom, heslo);
            Over(sluzba.NacitajRiadky().Count == pocet, "obnova vrátila stav zo zálohy");
            Over(bezpecnostna is not null && File.Exists(bezpecnostna), "bezpečnostná záloha pred obnovou");
            Over(zalohy.Zaznamy().Any(z => z.Typ == "obnova" && z.Uspech), "obnova je v zázname záloh");
            Over(zalohy.Zaznamy().Any(z => z.Typ == "pred obnovou" && z.Uspech), "záloha pred obnovou má vlastný typ");

            // typ zálohy pred aktualizáciou programu
            var predAktualizaciou = zalohy.Zalohuj(TypZalohy.PredAktualizaciou, poznamka: "test");
            Over(Path.GetFileName(predAktualizaciou).Contains("_EviTA_pred_aktualizaciou_"), "názov zálohy pred aktualizáciou");
            Over(zalohy.Zaznamy().First().Typ == "pred aktualizáciou", "typ zálohy pred aktualizáciou");

            // oprava typu pri zázname zo staršej verzie
            var staryTyp = zalohy.Zalohuj(TypZalohy.PredOperaciou, poznamka: "Automatická záloha pred aktualizáciou na verziu 0.2.8");
            Over(zalohy.Zaznamy().First().Typ == "pred operáciou", "záznam so starým typom");
            Over(zalohy.OpravTypyStarychZaznamov() == 1 && zalohy.Zaznamy().First(z => z.Cesta == staryTyp).Typ == "pred aktualizáciou",
                "starý záznam dostal typ pred aktualizáciou");
            Over(zalohy.OpravTypyStarychZaznamov() == 0, "oprava typov sa robí len raz");

            // rotácia automatických záloh
            zalohy.UlozNastavenia(nastavenia with { Ponechat = 2 });
            for (var i = 0; i < 3; i++)
            {
                zalohy.Zalohuj(TypZalohy.Automaticka);
            }

            Over(Directory.GetFiles(nastavenia.Priecinok, "*_EviTA_auto_*.zip").Length == 2, "rotácia ponechá 2 automatické zálohy");
            Over(Directory.GetFiles(nastavenia.Priecinok, "*_EviTA_zaloha_*.zip").Length == 2, "ručné zálohy rotácia nemaže");

            // číslovanie záloh
            var nazvy = Directory.GetFiles(nastavenia.Priecinok, "*_EviTA_*").Select(f => Path.GetFileName(f)).ToList();
            Over(nazvy.Count > 0 && nazvy.All(n => n.Length > 6 && n[..5].All(char.IsDigit) && n[5] == '_'), "zálohy majú poradové číslo na začiatku");
            Over(nazvy.Select(n => int.Parse(n[..5])).Distinct().Count() == nazvy.Count, "poradové čísla záloh sú jedinečné");
            var staraZaloha = Path.Combine(nastavenia.Priecinok, "EviTA_zaloha_2019-01-01_000000.zip");
            File.WriteAllText(staraZaloha, "test");
            File.SetLastWriteTimeUtc(staraZaloha, new DateTime(2019, 1, 1));
            var ocakavaneCislo = ZalohaService.DalsieCislo(nastavenia.Priecinok);
            Over(zalohy.PrecislujStareZalohy() == 1, "prečíslovanie zálohy zo staršej verzie");
            Over(File.Exists(Path.Combine(nastavenia.Priecinok, $"{ocakavaneCislo:D5}_EviTA_zaloha_2019-01-01_000000.zip")),
                "stará záloha dostala poradové číslo");

            // aktualizácie programu (bez prístupu na internet – len spracovanie údajov)
            const string vydanieJson = """
                {"tag_name":"v9.8.7","name":"EviTA 9.8.7","body":"## Novinky\n- **oprava**","html_url":"https://github.com/x",
                 "published_at":"2026-10-20T10:00:00Z","assets":[
                   {"name":"notes.md","browser_download_url":"https://x/notes.md","size":1},
                   {"name":"EviTA_Setup_9.8.7.exe","browser_download_url":"https://x/EviTA_Setup_9.8.7.exe","size":123,"digest":"sha256:ABCDEF"}]}
                """;
            var vydanie = AktualizaciaService.PrecitajVydanie(vydanieJson);
            Over(vydanie is { SuborNazov: "EviTA_Setup_9.8.7.exe", Sha256: "ABCDEF", SuborVelkost: 123 } && vydanie.Verzia == new Version(9, 8, 7),
                "spracovanie údajov o novej verzii");
            Over(vydanie!.Verzia > AktualizaciaService.AktualnaVerzia, "nová verzia je novšia ako nainštalovaná");
            Over(AktualizaciaService.PrecitajVydanie("""{"tag_name":"v1.0.0","assets":[]}""") is null, "vydanie bez inštalátora sa ignoruje");
            Over(AktualizaciaWindow.UpravNovinky(vydanie.Novinky) == $"Novinky{Environment.NewLine}- oprava", "úprava textu noviniek");

            // automatická záloha s heslom a popisy typov
            zalohy.NastavHesloAutomatickych("auto-heslo-456");
            Over(zalohy.JeHesloAutomatickychNastavene(), "heslo automatických záloh je uložené");
            zalohy.UlozNastavenia(nastavenia with { Ponechat = 2, AutomatickaSHeslom = true });
            var autoSHeslom = zalohy.Zalohuj(TypZalohy.Automaticka);
            Over(zalohy.JeZasifrovana(autoSHeslom) && zalohy.PrecitajInfo(autoSHeslom, "auto-heslo-456").Polozky > 0,
                "automatická záloha je zašifrovaná uloženým heslom");
            Over(ZalohaService.PopisTypu(zalohy.Zaznamy().First()) == "automatická záloha s heslom", "typ: automatická záloha s heslom");
            Over(ZalohaService.PopisTypu(new ZaznamZalohy { Typ = "ručná" }) == "ručná záloha", "typ: ručná záloha");
            Over(ZalohaService.PopisTypu(new ZaznamZalohy { Typ = "ručná", Zasifrovana = true }) == "ručná záloha s heslom", "typ: ručná záloha s heslom");
            Over(ZalohaService.PopisTypu(new ZaznamZalohy { Typ = "pred aktualizáciou" }) == "záloha pred aktualizáciou", "typ: záloha pred aktualizáciou");
            zalohy.NastavHesloAutomatickych(null);
            Over(!zalohy.NacitajNastavenia().AutomatickaSHeslom, "zmazaním hesla sa šifrovanie automatických záloh vypne");

            // Plánovač úloh Windows
            zalohy.UlozNastavenia(nastavenia with { Ponechat = 2, PlanovacWindows = true, Cas = "21:30" });
            Over(zalohy.JePlanovacNastaveny(), "úloha v Plánovači úloh Windows je vytvorená");
            zalohy.UlozNastavenia(nastavenia with { Ponechat = 2, PlanovacWindows = false });
            Over(!zalohy.JePlanovacNastaveny(), "úloha v Plánovači úloh Windows je zrušená");
            Krok("zálohy a obnova v poriadku");

            // kontrola a oprava
            var diagnostika = new DiagnostikaService();
            foreach (var nastroj in new Func<VysledokKontroly>[]
                     {
                         diagnostika.RychlaKontrola, diagnostika.UplnaKontrola, diagnostika.KontrolaVazieb,
                         diagnostika.KontrolaDat, diagnostika.Optimalizuj, diagnostika.ObnovIndexy, diagnostika.Kompaktuj,
                     })
            {
                var v = nastroj();
                Over(v.Ok, $"{v.Nastroj}: {string.Join(" | ", v.Riadky)}");
            }

            var pocetPredZachranou = sluzba.NacitajRiadky().Count;
            var zachrana = diagnostika.Zachran();
            Over(zachrana.Ok, $"záchrana dát: {string.Join(" | ", zachrana.Riadky)}");
            Over(sluzba.NacitajRiadky().Count == pocetPredZachranou, "záchrana dát zachovala všetky položky");
            Over(diagnostika.UplnaKontrola().Ok, "databáza po záchrane je v poriadku");
            Krok("kontrola a oprava v poriadku");

            // ------------------------------------------------------------ export
            var exportPriecinok = Path.Combine(Databaza.Priecinok, "Export");
            Directory.CreateDirectory(exportPriecinok);
            var export = new ExportService();
            var import = new ImportService();
            var vsetkyStlpce = StlpcePrenosu.Vsetky.Select(s => s.Kluc).ToList();
            var pocetPoloziek = sluzba.NacitajRiadky().Count;
            foreach (var format in Enum.GetValues<FormatExportu>())
            {
                var cesta = Path.Combine(exportPriecinok, "evita" + ExportService.Pripona(format));
                var vyexportovane = export.Exportuj(new MoznostiExportu(format, cesta, null, vsetkyStlpce,
                    SHistoriou: true, HarokPreKategoriu: format == FormatExportu.Excel, Popis: "test"));
                Over(vyexportovane == pocetPoloziek && File.Exists(cesta) && new FileInfo(cesta).Length > 500, $"export {format}");
            }

            Over(File.Exists(Path.Combine(exportPriecinok, XsdEviTA.NazovSuboru)), "pri exporte XML sa uloží evita.xsd");
            var predvoleneStlpce = StlpcePrenosu.Vsetky.Where(s => s.PredvolenyExport).Select(s => s.Kluc).ToList();
            foreach (var format in new[] { FormatExportu.Pdf, FormatExportu.Html })
            {
                var cesta = Path.Combine(exportPriecinok, "predvolene" + ExportService.Pripona(format));
                Over(export.Exportuj(new MoznostiExportu(format, cesta, null, predvoleneStlpce, Popis: "celá evidencia")) == pocetPoloziek,
                    $"export {format} s predvolenými stĺpcami");
            }

            var tri = sluzba.NacitajRiadky().Take(3).Select(r => r.Id).ToList();
            Over(export.Exportuj(new MoznostiExportu(FormatExportu.Csv, Path.Combine(exportPriecinok, "oznacene.csv"), tri, vsetkyStlpce)) == 3,
                "export označených položiek");
            Over(File.ReadAllText(Path.Combine(exportPriecinok, "evita.csv")).StartsWith("Prírastkové číslo;Názov;Kategória", StringComparison.Ordinal),
                "CSV má slovenské názvy stĺpcov a bodkočiarku");

            // čo sa vyexportuje, dá sa naimportovať: stĺpce sa rozpoznajú a položky sa spoznajú ako existujúce
            foreach (var pripona in new[] { ".xlsx", ".csv", ".json", ".xml" })
            {
                var zdroj = import.Nacitaj(Path.Combine(exportPriecinok, "evita" + pripona));
                var priradenie = ImportService.AutomatickePriradenie(zdroj.Hlavicky);
                Over(zdroj.Riadky.Count == pocetPoloziek, $"načítanie {pripona}: {zdroj.Riadky.Count} riadkov");
                Over(priradenie.Contains(StlpcePrenosu.Nazov) && priradenie.Contains(StlpcePrenosu.Identifikator) && priradenie.Contains(StlpcePrenosu.Seriove)
                     && priradenie.Contains(StlpcePrenosu.DatumNakupu) && priradenie.Contains(StlpcePrenosu.Cena),
                    $"stĺpce {pripona} sa rozpoznajú automaticky");
                var riadkyZdroja = ImportService.Priprav(zdroj, priradenie);
                new KontrolaImportu(DruhImportu.Zlucenie, zdroj).SkontrolujVsetky(riadkyZdroja);
                Over(riadkyZdroja.All(r => r.Vysledok == StavRiadku.Duplicita),
                    $"{pripona}: všetky položky sa spoznajú ako existujúce ({string.Join(" | ", riadkyZdroja.Where(r => r.Vysledok != StavRiadku.Duplicita).Take(3).Select(r => r.Problem))})");
            }

            Krok("export v poriadku");

            // ------------------------------------------------------------ vzory a import
            Over(Prevod.SkusDatum("20.10.2026", out var d1) && Prevod.SkusDatum("20. 10. 2026", out var d2) && Prevod.SkusDatum("2026-10-20", out var d3)
                 && d1 == new DateTime(2026, 10, 20) && d2 == d1 && d3 == d1, "dátumy 20.10.2026, 20. 10. 2026 a 2026-10-20");
            Over(!Prevod.SkusDatum("31.2.2026", out _) && !Prevod.SkusDatum("zajtra", out _), "nezmyselný dátum sa nerozpozná");
            Over(Prevod.SkusStav("V prevadzke", out var st1) && st1 == StavPolozky.VPrevadzke && Prevod.SkusStav("vyradené", out var st2) && st2 == StavPolozky.Vyradene
                 && !Prevod.SkusStav("stratené", out _), "rozpoznanie stavu");
            Over(StlpcePrenosu.Rozpoznaj("S/N") == StlpcePrenosu.Seriove && StlpcePrenosu.Rozpoznaj("Umiestnenie") == StlpcePrenosu.Miestnost
                 && StlpcePrenosu.Rozpoznaj("Dátum nákupu") == StlpcePrenosu.DatumNakupu, "rozpoznanie názvov stĺpcov");
            var csvTest = CitacSuborov.RozdelCsv("a;\"b;c\";\"d\"\"e\"\r\n1;\"x\ny\";3", ';');
            Over(csvTest.Count == 2 && csvTest[0][1] == "b;c" && csvTest[0][2] == "d\"e" && csvTest[1][1] == "x\ny", "CSV s úvodzovkami a zalomením riadku");

            Over(Directory.Exists(VzoryService.Vytvor()) && VzoryService.Existuju, "vzorové súbory");
            foreach (var vzorovy in new[] { VzoryService.Excel, VzoryService.Csv, VzoryService.Json, VzoryService.Xml })
            {
                var zdroj = import.Nacitaj(Path.Combine(VzoryService.Priecinok, vzorovy));
                var riadkyVz = ImportService.Priprav(zdroj, ImportService.AutomatickePriradenie(zdroj.Hlavicky));
                new KontrolaImportu(DruhImportu.Zlucenie, zdroj).SkontrolujVsetky(riadkyVz);
                Over(riadkyVz.Count == 5 && riadkyVz.All(r => r.Vysledok is not (StavRiadku.Chyba or StavRiadku.Duplicita)),
                    $"vzor {vzorovy}: 5 riadkov bez chyby ({string.Join(" | ", riadkyVz.Where(r => !r.MozeImportovat).Take(3).Select(r => r.Problem))})");
                Over(!zdroj.Upozornenia.Any(u => u.Contains(XsdEviTA.NazovSuboru)), $"vzor {vzorovy} zodpovedá popisu formátu ({string.Join(" | ", zdroj.Upozornenia)})");
            }

            var zdrojVzoru = import.Nacitaj(Path.Combine(VzoryService.Priecinok, VzoryService.Csv));
            var riadkyVzoru = ImportService.Priprav(zdrojVzoru, ImportService.AutomatickePriradenie(zdrojVzoru.Hlavicky));
            var kontrolaVzoru = new KontrolaImportu(DruhImportu.Zlucenie, zdrojVzoru);
            kontrolaVzoru.SkontrolujVsetky(riadkyVzoru);
            riadkyVzoru[0].Nazov = "";
            kontrolaVzoru.SkontrolujVsetky(riadkyVzoru);
            Over(riadkyVzoru[0].Vysledok == StavRiadku.Chyba && !riadkyVzoru[0].Importovat, "riadok bez názvu je chybný");
            riadkyVzoru[0].Nazov = "Notebook (opravený pri importe)";
            riadkyVzoru[1].DatumNakupu = "31.2.2026";
            kontrolaVzoru.SkontrolujVsetky(riadkyVzoru);
            Over(riadkyVzoru[0].MozeImportovat && riadkyVzoru[1].Vysledok == StavRiadku.Chyba, "opravený riadok je v poriadku, nezmyselný dátum je chyba");
            riadkyVzoru[1].DatumNakupu = "4. 11. 2025";
            kontrolaVzoru.SkontrolujVsetky(riadkyVzoru);
            foreach (var r in riadkyVzoru.Where(r => r.MozeImportovat))
            {
                r.Importovat = true;
            }

            var predImportom = sluzba.NacitajRiadky().Count;
            var nahladVzoru = kontrolaVzoru.Nahlad(riadkyVzoru);
            Over(nahladVzoru.Pridat == 5 && nahladVzoru.CisloOd is not null, $"náhľad importu: pribudne 5 (je {nahladVzoru.Pridat})");
            var vysledokVzoru = import.Importuj(zdrojVzoru, riadkyVzoru, DruhImportu.Zlucenie);
            Over(vysledokVzoru.Pridane == 5 && sluzba.NacitajRiadky().Count == predImportom + 5, "import vzoru CSV pridal 5 položiek");
            Over(vysledokVzoru.CisloOd == nahladVzoru.CisloOd, "nové prírastkové čísla od posledného + 1");
            var importovana = sluzba.NacitajRiadky().Single(r => r.PrirastkoveCislo == vysledokVzoru.CisloOd);
            Over(importovana.Nazov == "Notebook (opravený pri importe)" && importovana.Osoba == "Ján Novák" && importovana.Miestnost == "Kancelária 101",
                "importovaná položka má opravený názov, osobu a miestnosť");
            Over(sluzba.Historia(importovana.Id).Any(h => h.Akcia == PolozkaService.AkciaImport && (h.NovaHodnota ?? "").Contains("pôvodné prírastkové číslo 1")),
                "pôvodné prírastkové číslo je v histórii");
            Over(sluzba.NacitajRiadky().Any(r => r.Nazov.StartsWith("PC Lenovo") && r.Vyradena), "vyradená položka sa naimportovala ako vyradená");
            Over(new ZalohaService().Zaznamy().Any(z => z.Typ == "pred importom" && z.Uspech), "záloha pred importom");
            Over(import.PoslednyImport() is not null, "posledný import sa dá vrátiť");

            var opakovane = ImportService.Priprav(zdrojVzoru, ImportService.AutomatickePriradenie(zdrojVzoru.Hlavicky));
            new KontrolaImportu(DruhImportu.Zlucenie, zdrojVzoru).SkontrolujVsetky(opakovane);
            Over(opakovane.All(r => r.Vysledok == StavRiadku.Duplicita), "opakovaný import toho istého súboru nič nezdvojí");

            import.VratPoslednyImport();
            Over(sluzba.NacitajRiadky().Count == predImportom, "vrátenie importu");
            Over(import.PoslednyImport() is null, "po vrátení už nie je čo vracať");

            // prenos celej databázy do prázdnej (JSON aj XML, s históriou)
            foreach (var format in new[] { FormatExportu.Json, FormatExportu.Xml })
            {
                var celySubor = Path.Combine(exportPriecinok, "cela" + ExportService.Pripona(format));
                export.Exportuj(new MoznostiExportu(format, celySubor, null, vsetkyStlpce, SHistoriou: true));
                var povodne = sluzba.NacitajRiadky();
                int historiaPred, dalsieCisloPred, osobyPred;
                using (var db = Databaza.Otvor())
                {
                    historiaPred = db.Historia.Count();
                    dalsieCisloPred = Databaza.DalsiePrirastkoveCislo(db);
                    osobyPred = db.Osoby.Count();
                }

                var skusobny = import.Nacitaj(celySubor);
                var skusobne = ImportService.Priprav(skusobny, ImportService.AutomatickePriradenie(skusobny.Hlavicky));
                Over(Ocakavaj<EvitaException>(() => import.Importuj(skusobny, skusobne, DruhImportu.CelaDatabaza)),
                    $"{format}: prenos celej databázy do neprázdnej evidencie je odmietnutý");
                servis.VymazatData(ponechatMiestnosti: false, ponechatOsoby: false);
                var zdrojCely = import.Nacitaj(celySubor);
                Over(zdrojCely.Balik is { Osoby.Count: > 0, Kategorie.Count: > 0 } && zdrojCely.Balik.Historia is { Count: > 0 }, $"{format}: export obsahuje číselníky a históriu");
                var riadkyCele = ImportService.Priprav(zdrojCely, ImportService.AutomatickePriradenie(zdrojCely.Hlavicky));
                var kontrolaCela = new KontrolaImportu(DruhImportu.CelaDatabaza, zdrojCely);
                kontrolaCela.SkontrolujVsetky(riadkyCele);
                Over(riadkyCele.All(r => r.Importovat), $"{format}: všetky riadky v poriadku ({string.Join(" | ", riadkyCele.Where(r => !r.Importovat).Take(3).Select(r => r.Problem))})");
                import.Importuj(zdrojCely, riadkyCele, DruhImportu.CelaDatabaza);
                var prenesene = sluzba.NacitajRiadky();
                static string Odtlacok(PolozkaRiadok r) =>
                    $"{r.PrirastkoveCislo}|{r.Nazov}|{r.Kategoria}|{r.Miestnost}|{r.Osoba}|{r.SerioveCislo}|{r.Stav}|{r.ZarukaDo:yyyyMMdd}|{r.Poznamka}";
                Over(prenesene.Count == povodne.Count
                     && prenesene.Select(Odtlacok).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(povodne.Select(Odtlacok).OrderBy(x => x, StringComparer.Ordinal)),
                    $"{format}: prenos celej databázy zachoval čísla a údaje");
                using (var db = Databaza.Otvor())
                {
                    Over(db.Historia.Count() >= historiaPred, $"{format}: prenesená história zmien ({db.Historia.Count()} z {historiaPred})");
                    Over(Databaza.DalsiePrirastkoveCislo(db) == dalsieCisloPred, $"{format}: ďalšie prírastkové číslo zostalo {dalsieCisloPred}");
                    Over(db.Osoby.Count() == osobyPred, $"{format}: prenesené osoby ({db.Osoby.Count()} z {osobyPred})");
                }
            }

            Krok("import v poriadku");

            // po prenose celej databázy majú položky nové interné Id
            riadky = sluzba.NacitajRiadky();

            // obrazovky a okná
            var okno = new MainWindow();
            aplikacia.MainWindow = okno;
            okno.Show();
            Pumpuj();
            var model = (MainViewModel)okno.DataContext;
            foreach (var sekcia in new[] { "dashboard", "evidencia", "import", "kategorie", "miestnosti", "osoby", "sviatky", "servis", "dashboard" })
            {
                okno.OznacSekciu(sekcia);
                Pumpuj();
                PrejdiZalozky(okno);
                Krok($"obrazovka: {sekcia}");
            }

            Over(model.Dashboard.MeninyDnes.Length > 0 && model.Dashboard.MeninyDnes != "Kalendárium nie je dostupné", "dashboard: meniny");
            Over(model.Dashboard.NajblizsiVolnyDen.Length > 0, "dashboard: najbližší deň pracovného pokoja");
            Over(model.Dashboard.Graf.Count > 0 && model.Dashboard.Graf.Sum(g => g.Pocet) == sluzba.NacitajRiadky().Count(r => !r.Vyradena),
                "dashboard: graf kategórií sedí s počtom položiek");
            model.Dashboard.UkazGraf = false;
            Over(!new DashboardViewModel(model).UkazGraf, "dashboard: skrytý panel sa zapamätá");
            model.Dashboard.UkazGraf = true;
            model.Dashboard.OtvorKategoriuCommand.Execute(model.Dashboard.Graf[0]);
            Pumpuj();
            Over(model.Evidencia.FilterKategoria == model.Dashboard.Graf[0].KategoriaId && model.Evidencia.Pohlad!.Count == model.Dashboard.Graf[0].Pocet,
                "klik na graf zobrazí položky kategórie");

            // sviatky: prepnutie dňa pracovného pokoja v tabuľke
            model.Sviatky.Rok = 2026;
            model.Sviatky.Obnov();
            Over(!model.Sviatky.RezimZmien, "sviatky sú pri otvorení zamknuté");
            var riadokNovembra = model.Sviatky.Riadky.Single(r => r.Sviatok.Datum == new DateOnly(2026, 11, 17));
            riadokNovembra.DenPracovnehoPokoja = true;
            Over(!kalendar.JeSviatokVolno(new DateOnly(2026, 11, 17)), "bez „Zmeniť“ sa zmena neuloží");
            model.Sviatky.RezimZmien = true;
            riadokNovembra = model.Sviatky.Riadky.Single(r => r.Sviatok.Datum == new DateOnly(2026, 11, 17));
            riadokNovembra.DenPracovnehoPokoja = false;
            riadokNovembra.DenPracovnehoPokoja = true;
            Over(kalendar.JeSviatokVolno(new DateOnly(2026, 11, 17)), "zmena v tabuľke sviatkov sa uloží");
            model.Sviatky.VratitPodlaZakonaCommand.Execute(null);
            Over(!kalendar.JeSviatokVolno(new DateOnly(2026, 11, 17)), "vrátenie pravidiel zo zákona");

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
            OtvorAZatvor(new HesloWindow(true, null), "nové heslo");
            OtvorAZatvor(new HesloWindow(false, null), "zadanie hesla");
            OtvorAZatvor(new AktualizaciaWindow(vydanie), "ponuka aktualizácie");
            Over(model.Evidencia.PopisFiltra().Contains("ucebna"), "popis filtra pre export");

            // okno exportu
            var exportModel = new ExportViewModel(riadky.Take(10).Select(r => r.Id).ToList(), riadky.Take(2).Select(r => r.Id).ToList(), "test")
            {
                CestaBezDialogu = Path.Combine(exportPriecinok, "z_okna.pdf"),
                OtvoritPoExporte = false,
            };
            var oknoExportu = new ExportWindow(exportModel);
            oknoExportu.Show();
            Pumpuj();
            exportModel.Formaty.First(f => f.Popis.Format == FormatExportu.Pdf).Vybrany = true;
            Pumpuj();
            Over(exportModel.Format == FormatExportu.Pdf && exportModel.UkazStlpce && exportModel.RozsahOznacene, "okno exportu: formát a rozsah");
            exportModel.ExportovatCommand.Execute(null);
            Pumpuj();
            Over(File.Exists(exportModel.CestaBezDialogu), "export z okna");
            if (oknoExportu.IsVisible)
            {
                oknoExportu.Close();
            }

            Krok("okno: export");

            // sprievodca importom krok za krokom
            var predSprievodcom = sluzba.NacitajRiadky().Count;
            var sprievodca = new ImportViewModel { Cesta = Path.Combine(VzoryService.Priecinok, VzoryService.Csv) };
            var oknoImportu = new ImportWindow(sprievodca);
            oknoImportu.Show();
            Pumpuj();
            sprievodca.DalejCommand.Execute(null);
            Pumpuj();
            Over(sprievodca.Krok == 2 && sprievodca.Priradenia.Count >= 15, $"sprievodca: priradenie stĺpcov (krok {sprievodca.Krok})");
            sprievodca.DalejCommand.Execute(null);
            Pumpuj();
            Over(sprievodca.Krok == 3 && sprievodca.Riadky.Count == 5, $"sprievodca: kontrola (krok {sprievodca.Krok})");
            sprievodca.Riadky[0].Nazov = "";
            Pumpuj();
            Over(sprievodca.Riadky[0].Vysledok == StavRiadku.Chyba, "sprievodca: zmena v tabuľke sa hneď skontroluje");
            sprievodca.Riadky[0].Nazov = "Notebook zo sprievodcu";
            Pumpuj();
            Over(sprievodca.Riadky[0].MozeImportovat, "sprievodca: opravený riadok");
            sprievodca.VybratVsetkyCommand.Execute(null);
            sprievodca.LenProblemove = true;
            Pumpuj();
            sprievodca.LenProblemove = false;
            sprievodca.DalejCommand.Execute(null);
            Pumpuj();
            Over(sprievodca.Krok == 4 && sprievodca.NahladPridat.Contains("5 položiek"), $"sprievodca: náhľad ({sprievodca.NahladPridat})");
            sprievodca.ImportovatCommand.Execute(null);
            Pumpuj();
            Over(sprievodca.Krok == 5 && sprievodca.Vysledok?.Pridane == 5 && sluzba.NacitajRiadky().Count == predSprievodcom + 5, "sprievodca: import");
            Over(sprievodca.Protokol.Contains("Notebook zo sprievodcu"), "sprievodca: protokol");
            sprievodca.VytlacitProtokolCommand.Execute(null);
            sprievodca.VratitImportCommand.Execute(null);
            Pumpuj();
            Over(sprievodca.ImportVrateny && sluzba.NacitajRiadky().Count == predSprievodcom, "sprievodca: vrátenie importu");
            oknoImportu.Close();
            Pumpuj();
            Krok("okno: sprievodca importom");
            model.NovaVerzia = vydanie;
            Pumpuj();
            Over(model.JeNovaVerzia, "pruh s novou verziou");
            okno.Close();
            Pumpuj();

            // nový začiatok
            servis.VymazatData(ponechatMiestnosti: false, ponechatOsoby: false);
            Over(sluzba.NacitajRiadky().Count == 0, "vymazanie dát");
            Over(new ZalohaService().Zaznamy().First().Typ == "pred vymazaním dát", "záloha pred vymazaním má vlastný typ");
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

    private static bool Ocakavaj<T>(Action akcia) where T : Exception
    {
        try
        {
            akcia();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }

    private static void Vykonaj(string sql)
    {
        using var spojenie = Databaza.OtvorSpojenie();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        prikaz.ExecuteNonQuery();
    }

    /// <summary>Postupne zobrazí všetky záložky na obrazovke, aby sa vytvoril ich obsah.</summary>
    private static void PrejdiZalozky(DependencyObject koren)
    {
        foreach (var zalozky in Potomkovia<System.Windows.Controls.TabControl>(koren).ToList())
        {
            for (var i = 0; i < zalozky.Items.Count; i++)
            {
                zalozky.SelectedIndex = i;
                Pumpuj();
            }

            zalozky.SelectedIndex = 0;
            Pumpuj();
        }
    }

    private static IEnumerable<T> Potomkovia<T>(DependencyObject koren) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(koren); i++)
        {
            var dieta = System.Windows.Media.VisualTreeHelper.GetChild(koren, i);
            if (dieta is T hladany)
            {
                yield return hladany;
            }

            foreach (var vnoreny in Potomkovia<T>(dieta))
            {
                yield return vnoreny;
            }
        }
    }

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
