using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Evita.Data;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;
using Evita.ViewModels;
using Evita.Views;

namespace Evita;

/// <summary>Samotest Etapy 8: kódy, štítky, inventúra, mobil, EAN a okná skenovania.</summary>
internal static partial class Samotest
{
    /// <summary>Prijímač kódov počas testu služieb (hlavné okno ešte nie je otvorené).</summary>
    private sealed class TestovaciPrijimac : IPrijimacSkenov
    {
        public List<string> Prijate { get; } = new();

        public VysledokSpracovania Prijmi(Sken sken)
        {
            Prijate.Add(sken.Text);
            var vysledok = new SkenovanieService().Najdi(sken);
            return new VysledokSpracovania(vysledok.Najdene, vysledok.Popis);
        }
    }

    private static void SkenovanieSluzby(string exportPriecinok)
    {
        // --------------------------------------------------------------- texty kódov a rozloženie klávesnice
        Over(KodyEvita.KodPolozky(123) == "EVITA-123" && KodyEvita.PrirastkoveZKodu("EVITA-123") == 123
             && KodyEvita.PrirastkoveZKodu("evita 0045") == 45 && KodyEvita.PrirastkoveZKodu("EVITA-") is null
             && KodyEvita.PrirastkoveZKodu("INV-123") is null, "kód štítku EVITA-číslo");
        Over(KodyEvita.Varianty("EVITA=+ľš").Contains("EVITA-123"), "čítačka v americkom režime na slovenskej klávesnici (EVITA=+ľš)");
        Over(KodyEvita.Varianty("+ľščťžýáíé").Contains("1234567890"), "číslice zo slovenskej klávesnice");
        Over(KodyEvita.Varianty("ABZ").Contains("ABY"), "výmena Y a Z (QWERTZ)");
        Over(KodyEvita.NormalizujEan("4006381333931") == "4006381333931" && KodyEvita.NormalizujEan("4006381333932") is null
             && KodyEvita.NormalizujEan("96385074") == "96385074" && KodyEvita.NormalizujEan("036000291452") == "036000291452"
             && KodyEvita.NormalizujEan("400-6381 333931") == "4006381333931" && KodyEvita.NormalizujEan("ABC") is null,
            "EAN-13, EAN-8, UPC-A a kontrolná číslica");

        // --------------------------------------------------------------- QR a čiarový kód tam a späť
        var nativna = Path.Combine(AppContext.BaseDirectory, "ZXing.dll");
        Over(File.Exists(nativna), "čítačka kódov zxing-cpp (ZXing.dll zostavená zo zdrojového kódu) je pri programe");
        Krok($"ZXing.dll: {new FileInfo(nativna).Length} B, SHA-256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(nativna)))}");
        var qr = CiarovyKod.Qr("EVITA-5");
        Over(qr.GetLength(0) == 21 && qr.GetLength(1) == 21, $"QR kód krátkeho textu má 21 × 21 modulov (má {qr.GetLength(0)})");
        var (qrData, qrSirka, qrVyska) = ObrazokQr("EVITA-5", 6);
        Over(CiarovyKod.Precitaj(qrData, qrSirka, qrVyska) == "EVITA-5", "QR kód sa dá prečítať späť");
        var (pruhyData, pruhySirka, pruhyVyska) = ObrazokCode128("EVITA-1234", 3, 90);
        Over(CiarovyKod.Precitaj(pruhyData, pruhySirka, pruhyVyska) == "EVITA-1234", "čiarový kód Code 128 sa dá prečítať späť");
        var png = Png(qrData, qrSirka, qrVyska);
        File.WriteAllBytes(Path.Combine(exportPriecinok, "sken_qr.png"), png);
        Over(CiarovyKod.PrecitajZObrazka(png) == "EVITA-5", "QR kód z obrázka PNG (fotka z mobilu)");
        Over(CiarovyKod.Precitaj(new byte[100 * 100 * 4], 100, 100) is null, "prázdny obrázok – žiadny kód");
        var qrSDiakritikou = ObrazokQr("INV-2026/0123 žltý", 5);
        Over(CiarovyKod.Precitaj(qrSDiakritikou.Data, qrSDiakritikou.Sirka, qrSDiakritikou.Vyska) == "INV-2026/0123 žltý", "QR kód s diakritikou");

        // kódy GS1 a viac kódov na jednej fotke (krabica: EAN, sériové číslo, QR s odkazom na web)
        Over(KodyEvita.GtinZGs1("(01)04006381333931(10)ABC") == "4006381333931" && KodyEvita.GtinZGs1("0104006381333931") == "4006381333931"
             && KodyEvita.GtinZGs1("https://id.gs1.org/01/04006381333931/21/123") == "4006381333931" && KodyEvita.GtinZGs1("http://eaton.eu/3SMini") is null,
            "GTIN z kódu GS1 a z odkazu GS1 Digital Link");
        Over(KodyEvita.Varianty("https://example.com/01/04006381333931").Contains("4006381333931"), "GTIN z odkazu je medzi podobami kódu");
        var (dveData, dveSirka, dveVyska) = DvaKody("http://eaton.eu/3SMini", "EVITA-5");
        var dva = CiarovyKod.PrecitajVsetky(dveData, dveSirka, dveVyska);
        Over(dva.Count == 2 && dva.Contains("http://eaton.eu/3SMini") && dva.Contains("EVITA-5"), $"dva kódy na jednom obrázku ({string.Join(", ", dva)})");
        var dvaPng = Png(dveData, dveSirka, dveVyska);
        File.WriteAllBytes(Path.Combine(exportPriecinok, "sken_dva_kody.png"), dvaPng);

        // --------------------------------------------------------------- vyhľadanie položky podľa kódu
        var sluzba = new PolozkaService();
        var riadky = sluzba.NacitajRiadky().Where(r => !r.Vyradena).OrderBy(r => r.PrirastkoveCislo).ToList();
        var skenovanie = new SkenovanieService();
        var prva = riadky[0];
        var vysledok = skenovanie.Najdi(Sken.Z(KodyEvita.KodPolozky(prva.PrirastkoveCislo), ZdrojSkenu.Citacka));
        Over(vysledok.Druh == DruhZhody.Stitok && vysledok.Polozky.Count == 1 && vysledok.Polozky[0].Id == prva.Id, "sken štítku EviTA");
        var sk = KodyEvita.KodPolozky(prva.PrirastkoveCislo).Replace('-', '=')
            .Replace('1', '+').Replace('2', 'ľ').Replace('3', 'š').Replace('4', 'č').Replace('5', 'ť')
            .Replace('6', 'ž').Replace('7', 'ý').Replace('8', 'á').Replace('9', 'í').Replace('0', 'é');
        Over(skenovanie.Najdi(Sken.Z(sk, ZdrojSkenu.Citacka)).Polozky.FirstOrDefault()?.Id == prva.Id, $"sken štítku zo slovenskej klávesnice ({sk})");
        var sInventarnym = riadky.First(r => !string.IsNullOrEmpty(r.InventarneCislo));
        var podlaInventarneho = skenovanie.Najdi(sInventarnym.InventarneCislo!.ToLowerInvariant());
        Over(podlaInventarneho.Druh == DruhZhody.Inventarne && podlaInventarneho.Polozky.Any(p => p.Id == sInventarnym.Id), "sken inventárneho čísla (aj malými písmenami)");
        var sSeriovym = riadky.First(r => !string.IsNullOrEmpty(r.SerioveCislo));
        var podlaSerioveho = skenovanie.Najdi(sSeriovym.SerioveCislo!);
        Over(podlaSerioveho.Druh == DruhZhody.Seriove && podlaSerioveho.Polozky.Any(p => p.Id == sSeriovym.Id), "sken sériového čísla");
        var podlaCisla = skenovanie.Najdi(riadky[3].PrirastkoveCislo.ToString());
        Over(podlaCisla.Polozky.Any(p => p.Id == riadky[3].Id), "prírastkové číslo napísané ručne");
        var neznamy = skenovanie.Najdi("NEZNAMY-KOD-XYZ");
        Over(!neznamy.Najdene && neznamy.Ean is null, "neznámy kód");
        Over(skenovanie.VyberKod(new[] { "http://eaton.eu/3SMini", KodyEvita.KodPolozky(prva.PrirastkoveCislo) }) == KodyEvita.KodPolozky(prva.PrirastkoveCislo)
             && skenovanie.VyberKod(new[] { "http://eaton.eu/3SMini", "C9507R", "6972470564259" }) == "6972470564259"
             && skenovanie.VyberKod(new[] { "http://eaton.eu/3SMini", "C9507R" }) == "C9507R",
            "výber kódu z fotky: položka v evidencii, potom EAN, webová adresa až nakoniec");
        var neexistujuci = skenovanie.Najdi("EVITA-999999");
        Over(!neexistujuci.Najdene && neexistujuci.JeStitokEviTA, "štítok EviTA neexistujúcej položky");

        // EAN pri dvoch položkách (rovnaký výrobok) a hľadanie aj podľa UPC
        foreach (var id in new[] { riadky[5].Id, riadky[6].Id })
        {
            var p = sluzba.Nacitaj(id)!;
            p.Ean = "0036000291452";
            sluzba.Uloz(p);
        }

        Over(sluzba.Historia(riadky[5].Id).Any(h => h.Pole == "EAN" && h.NovaHodnota == "0036000291452"), "zmena EAN v histórii položky");
        var podlaEan = skenovanie.Najdi("036000291452");
        Over(podlaEan.Druh == DruhZhody.Ean && podlaEan.Polozky.Count == 2, "sken EAN (UPC-A) nájde obe položky s EAN-13");
        Over(sluzba.NacitajRiadky().First(r => r.Id == riadky[5].Id).Hladanie.Contains("0036000291452"), "EAN v rýchlom vyhľadávaní evidencie");
        var zEvidencie = new EanService().ZEvidencie("0036000291452", riadky[5].Id);
        Over(zEvidencie is not null && zEvidencie.KategoriaId == riadky[6].KategoriaId, "údaje o výrobku podľa EAN z vlastnej evidencie");
        Krok("vyhľadanie podľa kódu v poriadku");

        // --------------------------------------------------------------- štítky
        var stitky = new StitkyService();
        var udaje = stitky.NacitajUdaje(riadky.Take(30).Select(r => r.Id));
        Over(udaje.Count == 30 && udaje[0].PrirastkoveCislo == prva.PrirastkoveCislo, "údaje pre štítky (zoradené podľa čísla)");
        var harok = StitkyService.Priprav(new MoznostiStitkov { Sablona = "a4-3x8", ZacatOd = 5 }, udaje, "Základná škola Samotest");
        Over(harok.PocetStitkov == 30 && harok.PocetStran == 2 && harok.Stitky[0].X == 70 && harok.Stitky[0].Strana == 0, "štítky na hárku A4 od 5. pozície");
        StitkyService.UlozPdf(harok, Path.Combine(exportPriecinok, "stitky_A4_qr.pdf"));
        var ciarove = StitkyService.Priprav(new MoznostiStitkov { Sablona = "a4-4x10", Kod = DruhKodu.Ciarovy, Kopie = 2, Seriove = true, Miestnost = true },
            udaje, "Základná škola Samotest");
        Over(ciarove.PocetStitkov == 60 && ciarove.PocetStran == 2, "čiarové kódy, 2 kópie, 40 na hárku");
        StitkyService.UlozPdf(ciarove, Path.Combine(exportPriecinok, "stitky_A4_ciarove.pdf"));
        var rola = StitkyService.Priprav(new MoznostiStitkov { Sablona = "rola-62x29", ObsahKodu = ObsahKodu.Inventarne }, udaje.Take(3).ToList(), null);
        Over(rola.PocetStran == 3 && rola.Sablona.SirkaStrany == 62, "tlačiareň štítkov – jeden štítok na stranu");
        StitkyService.UlozPdf(rola, Path.Combine(exportPriecinok, "stitky_rola_62x29.pdf"));
        var vlastne = StitkyService.Priprav(new MoznostiStitkov
        {
            Sablona = StitkyService.KlucVlastna, VlastnaSirka = 105, VlastnaVyska = 57, VlastneStlpce = 2, VlastneRiadky = 5,
            VlastnyOkrajHore = 5, VlastnaMedzeraZvisle = 2, Ramcek = true,
        }, udaje, null);
        Over(vlastne.Sablona.NaStranu == 10 && vlastne.PocetStran == 3 && Math.Abs(vlastne.Stitky[3].Y - (5 + 59)) < 0.001, "vlastný formát štítkov");
        StitkyService.UlozPdf(vlastne, Path.Combine(exportPriecinok, "stitky_vlastne.pdf"));
        Over(new[] { "stitky_A4_qr.pdf", "stitky_A4_ciarove.pdf", "stitky_rola_62x29.pdf", "stitky_vlastne.pdf" }
            .All(s => new FileInfo(Path.Combine(exportPriecinok, s)).Length > 3000), "PDF so štítkami");
        var bezInventarneho = udaje[0] with { Inventarne = null };
        Over(StitkyService.TextKodu(bezInventarneho, new MoznostiStitkov { ObsahKodu = ObsahKodu.Inventarne }) == KodyEvita.KodPolozky(bezInventarneho.PrirastkoveCislo)
             && StitkyService.TextKodu(udaje[0] with { Inventarne = "INV-1" }, new MoznostiStitkov { ObsahKodu = ObsahKodu.Inventarne }) == "INV-1",
            "kód štítku: inventárne číslo, bez neho štítok EviTA");
        stitky.UlozMoznosti(new MoznostiStitkov { Sablona = "a4-3x7", Kod = DruhKodu.Ciarovy, Kopie = 5 });
        var nacitane = stitky.NacitajMoznosti();
        Over(nacitane.Sablona == "a4-3x7" && nacitane.Kod == DruhKodu.Ciarovy && nacitane.Kopie == 1, "nastavenia štítkov sa zapamätajú (bez kópií)");
        stitky.UlozMoznosti(new MoznostiStitkov());
        Krok("štítky v poriadku");

        // --------------------------------------------------------------- inventúra
        var inventury = new InventuraService();
        var idInventury = inventury.Nova("Inventúra samotest");
        var vMiestnosti = riadky.Where(r => r.MiestnostId is not null).GroupBy(r => r.MiestnostId).OrderByDescending(g => g.Count()).First().ToList();
        var miestnost = vMiestnosti[0].MiestnostId;
        var inde = riadky.First(r => r.MiestnostId is not null && r.MiestnostId != miestnost);
        Sken S(string kod) => Sken.Z(kod, ZdrojSkenu.Citacka);

        var najdena = inventury.Zaznamenaj(idInventury, S(KodyEvita.KodPolozky(vMiestnosti[0].PrirastkoveCislo)), miestnost);
        Over(najdena.Druh == VysledokZaznamu.Najdena, $"inventúra: položka v správnej miestnosti ({najdena.Text})");
        Over(inventury.Zaznamenaj(idInventury, S(KodyEvita.KodPolozky(vMiestnosti[0].PrirastkoveCislo)), miestnost).Druh == VysledokZaznamu.UzNaskenovana,
            "inventúra: opakovaný sken");
        Over(inventury.Zaznamenaj(idInventury, S(KodyEvita.KodPolozky(inde.PrirastkoveCislo)), miestnost).Druh == VysledokZaznamu.NajdenaInde,
            "inventúra: položka z inej miestnosti");
        Over(inventury.Zaznamenaj(idInventury, S("NEZNAMY-123"), miestnost).Druh == VysledokZaznamu.Neznamy, "inventúra: neznámy kód");
        Over(inventury.Zaznamenaj(idInventury, S("0036000291452"), miestnost).Druh is VysledokZaznamu.Nejednoznacny or VysledokZaznamu.Najdena
             or VysledokZaznamu.NajdenaInde, "inventúra: kód s viacerými položkami");
        Over(inventury.OznacRucne(idInventury, vMiestnosti[1].Id, miestnost).Druh == VysledokZaznamu.Najdena, "inventúra: ručné označenie");
        var stav = inventury.Polozky(idInventury);
        Over(stav.Count(p => p.Najdena) >= 3 && stav.Single(p => p.Id == inde.Id).Inde, "inventúra: stav položiek");
        Over(inventury.Zoznam().Single(i => i.Id == idInventury).Najdene >= 3, "inventúra: počet nájdených v zozname");
        Over(inventury.PresunNajdene(stav.Where(p => p.Id == inde.Id)) == 1 && sluzba.Nacitaj(inde.Id)!.MiestnostId == miestnost,
            "inventúra: presun nájdenej položky do miestnosti (zapíše sa do evidencie)");
        inventury.ZrusPolozku(idInventury, vMiestnosti[1].Id);
        Over(!inventury.Polozky(idInventury).Single(p => p.Id == vMiestnosti[1].Id).Najdena, "inventúra: zrušenie označenia");
        Over(inventury.Nezname(idInventury).Count == 1, "inventúra: neznáme kódy");
        var protokol = Path.Combine(exportPriecinok, "inventura_protokol.pdf");
        PdfZostava.UlozPdf(inventury.Protokol(idInventury), protokol);
        Over(new FileInfo(protokol).Length > 3000, "inventúrny protokol (PDF)");
        inventury.Ukonci(idInventury);
        Over(Ocakavaj<EvitaException>(() => inventury.Zaznamenaj(idInventury, S("EVITA-1"), miestnost)), "ukončená inventúra neprijíma skeny");
        inventury.ZnovaOtvor(idInventury);
        Over(inventury.Nacitaj(idInventury)!.Stav == StavInventury.Prebieha, "inventúra otvorená znova");
        var druha = inventury.Nova("Na vymazanie");
        inventury.Vymaz(druha);
        Over(inventury.Nacitaj(druha) is null, "vymazanie inventúry");
        Krok("inventúra v poriadku");

        // --------------------------------------------------------------- mobil ako skener (miestna webová stránka)
        var prijimac = new TestovaciPrijimac();
        SkenerCentrum.Zaregistruj(prijimac);
        var server = new MobilnySkener(Dispatcher.CurrentDispatcher);
        try
        {
            server.Spusti(SkenovanieService.PortMobilu());
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var zaklad = $"http://127.0.0.1:{server.Port}";
            var zla = Cakaj(http.GetAsync($"{zaklad}/?k=zly"));
            Over((int)zla.StatusCode == 403, "mobil: zlý kľúč je odmietnutý");
            var stranka = Cakaj(http.GetAsync($"{zaklad}/?k={server.Kluc}"));
            Over(stranka.IsSuccessStatusCode && Cakaj(stranka.Content.ReadAsStringAsync()).Contains("capture=\"environment\"") && server.Pripojenia == 1,
                "mobil: stránka s fotoaparátom");
            var kod = Cakaj(http.PostAsync($"{zaklad}/kod?k={server.Kluc}", new StringContent(KodyEvita.KodPolozky(prva.PrirastkoveCislo), Encoding.UTF8)));
            var kodJson = Cakaj(kod.Content.ReadAsStringAsync());
            Over(kodJson.Contains("\"ok\":true") && prijimac.Prijate.LastOrDefault() == KodyEvita.KodPolozky(prva.PrirastkoveCislo), $"mobil: napísaný kód ({kodJson})");
            var foto = Cakaj(http.PostAsync($"{zaklad}/foto?k={server.Kluc}", new ByteArrayContent(png)));
            var fotoJson = Cakaj(foto.Content.ReadAsStringAsync());
            Over(fotoJson.Contains("\"ok\":true") && prijimac.Prijate.LastOrDefault() == "EVITA-5", $"mobil: kód z fotky ({fotoJson})");
            var dveFoto = Cakaj(http.PostAsync($"{zaklad}/foto?k={server.Kluc}", new ByteArrayContent(dvaPng)));
            var dveJson = Cakaj(dveFoto.Content.ReadAsStringAsync());
            Over(dveJson.Contains("\"ok\":true") && prijimac.Prijate.LastOrDefault() == "EVITA-5", $"mobil: z fotky s dvoma kódmi sa spracuje kód položky ({dveJson})");
            var dlhy = Cakaj(http.PostAsync($"{zaklad}/kod?k={server.Kluc}", new StringContent(new string('A', 5000), Encoding.UTF8)));
            Cakaj(dlhy.Content.ReadAsStringAsync());
            Over(prijimac.Prijate.LastOrDefault()?.Length <= KodyEvita.NajdlhsiKod, "mobil: príliš dlhý kód sa skráti");
            Krok($"mobil: adresy počítača {string.Join(", ", MobilnySkener.AdresyPocitaca())}, port {server.Port}");
        }
        finally
        {
            server.Dispose();
            SkenerCentrum.Odregistruj(prijimac);
        }

        Krok("mobil ako skener v poriadku");

        // --------------------------------------------------------------- EAN z internetu (len záznam – služby nemusia byť dostupné)
        foreach (var ean in new[] { "5099206027312", "0889842640816" })
        {
            try
            {
                var zInternetu = Cakaj(new EanService().HladajAsync(ean));
                Krok($"EAN {ean}: {string.Join(" | ", zInternetu.Najdene.Select(n => $"{n.Zdroj}: {n.Nazov} ({n.Prehlad})"))} {string.Join(" ", zInternetu.Spravy)}");
            }
            catch (Exception ex)
            {
                Krok($"EAN {ean}: {ex.GetBaseException().Message}");
            }
        }
    }

    private static void SkenovanieOkna(MainViewModel model, MainWindow okno)
    {
        var riadky = new PolozkaService().NacitajRiadky().Where(r => !r.Vyradena).OrderBy(r => r.PrirastkoveCislo).ToList();
        var kod = KodyEvita.KodPolozky(riadky[0].PrirastkoveCislo);

        // obrazovka Skenovanie: kód sa zobrazí v zozname posledných skenov a otvorí sa karta
        okno.OznacSekciu("skenovanie");
        Pumpuj();
        Over(SkenerCentrum.JePrijimac(model.Skenovanie), "obrazovka Skenovanie prijíma kódy");
        var hlasenia = Dialogy.TicheHlasenia.Count;
        var vysledok = SkenerCentrum.Odosli(Sken.Z(kod, ZdrojSkenu.Citacka));
        Pumpuj();
        Over(vysledok.Uspech && model.Skenovanie.Posledne[0].JednaPolozka && Dialogy.TicheHlasenia.Skip(hlasenia).Any(h => h.StartsWith("[Karta]")),
            "sken na obrazovke Skenovanie otvorí kartu položky");
        SkenerCentrum.Odosli(Sken.Z("NEZNAMY-XYZ-1", ZdrojSkenu.Klavesnica));
        SkenerCentrum.Odosli(Sken.Z("0036000291452", ZdrojSkenu.Mobil));
        Pumpuj();
        Snimka(okno, "okno_skenovanie");

        // mimo obrazovky Skenovanie: predvolené spracovanie v hlavnom okne
        okno.OznacSekciu("dashboard");
        Pumpuj();
        Over(!SkenerCentrum.JePrijimac(model.Skenovanie), "po odchode z obrazovky Skenovanie ju kódy neobchádzajú");
        hlasenia = Dialogy.TicheHlasenia.Count;
        SkenerCentrum.Odosli(Sken.Z(kod, ZdrojSkenu.Citacka));
        Pumpuj();
        Over(Dialogy.TicheHlasenia.Skip(hlasenia).Any(h => h.StartsWith("[Karta]")), "sken kdekoľvek v programe otvorí kartu položky");
        SkenerCentrum.Odosli(Sken.Z("0036000291452", ZdrojSkenu.Citacka));
        Pumpuj();
        Over(model.AktualnyObsah == model.Evidencia && model.Evidencia.Pohlad?.Count == 2, "kód viacerých položiek ich zobrazí v evidencii");
        Snimka(okno, "okno_evidencia_sken");
        SkenerCentrum.Odosli(Sken.Z("NEZNAMY-XYZ-2", ZdrojSkenu.Citacka));
        Pumpuj();
        Over(model.AktualnyObsah == model.Skenovanie && model.Skenovanie.Posledne[0].Nenajdene, "neznámy kód otvorí obrazovku Skenovanie");

        // vyhľadávanie v evidencii podľa štítku
        okno.OznacSekciu("evidencia");
        model.Evidencia.VycistitFiltreCommand.Execute(null);
        model.Evidencia.HladanyText = kod;
        var cakanie = System.Diagnostics.Stopwatch.StartNew();
        while (cakanie.ElapsedMilliseconds < 800)
        {
            Pumpuj();
            Thread.Sleep(20);
        }

        Over(model.Evidencia.Pohlad?.Count == 1, $"hľadanie štítku {kod} v evidencii nájde práve jednu položku");
        model.Evidencia.VycistitFiltreCommand.Execute(null);
        Pumpuj();

        // okno štítkov
        var stitkyModel = new StitkyViewModel(riadky.Take(30).Select(r => r.Id).ToList(), "samotest");
        var stitkyOkno = new StitkyWindow(stitkyModel);
        stitkyOkno.Show();
        Pumpuj();
        stitkyModel.Sablona = StitkyService.Sablony.First(s => s.Kluc == "a4-3x8");
        stitkyModel.ObnovNahlad();
        Pumpuj();
        Over(stitkyModel.Strany.Count == 2, $"okno štítkov: náhľad 2 strán ({stitkyModel.InfoText})");
        Snimka(stitkyOkno, "okno_stitky");
        stitkyModel.Sablona = StitkyService.Sablony.First(s => s.Kluc == "rola-62x29");
        stitkyModel.Kod = DruhKodu.Ciarovy;
        stitkyModel.ObnovNahlad();
        Pumpuj();
        Over(stitkyModel.Strany.Count == 12 && stitkyModel.MierkaNahladu > 1, "okno štítkov: tlačiareň štítkov, náhľad najviac 12 strán");
        Snimka(stitkyOkno, "okno_stitky_rola");
        stitkyModel.Sablona = StitkyService.Sablony.First(s => s.Kluc == StitkyService.KlucVlastna);
        stitkyModel.ObnovNahlad();
        Pumpuj();
        Snimka(stitkyOkno, "okno_stitky_vlastne");
        stitkyModel.CestaBezDialogu = Path.Combine(Databaza.Priecinok, "Export", "stitky_z_okna.pdf");
        Over(stitkyModel.UlozPdf() is { } pdf && File.Exists(pdf), "okno štítkov: uloženie PDF");
        stitkyModel.VytlacitCommand.Execute(null);
        stitkyOkno.Close();
        Pumpuj();

        // okno inventúry
        var idInventury = new InventuraService().Nova("Inventúra z okna");
        var oknoInventury = InventuraWindow.Otvor(idInventury);
        Pumpuj();
        var miestnost = riadky.First(r => r.MiestnostId is not null);
        oknoInventury.Model.MiestnostId = miestnost.MiestnostId!.Value;
        Pumpuj();
        Over(SkenerCentrum.JePrijimac(oknoInventury.Model), "okno inventúry prijíma kódy");
        var vInventure = SkenerCentrum.Odosli(Sken.Z(KodyEvita.KodPolozky(miestnost.PrirastkoveCislo), ZdrojSkenu.Kamera));
        SkenerCentrum.Odosli(Sken.Z(KodyEvita.KodPolozky(riadky.First(r => r.MiestnostId != miestnost.MiestnostId).PrirastkoveCislo), ZdrojSkenu.Citacka));
        Pumpuj();
        Over(vInventure.Uspech && oknoInventury.Model.Zaznamy.Count == 2 && oknoInventury.Model.NajdeneInde.Count == 1,
            "okno inventúry: sken nájdenej a cudzej položky");
        Snimka(oknoInventury, "okno_inventura");
        oknoInventury.Model.LenChybajuce = true;
        Pumpuj();
        Over(oknoInventury.Model.PolozkyMiestnosti.All(p => !p.Najdena || p.Inde), "okno inventúry: len chýbajúce");
        oknoInventury.Close();
        Pumpuj();
        Over(!SkenerCentrum.JePrijimac(oknoInventury.Model), "po zatvorení inventúry kódy idú znova do hlavného okna");

        // výber údajov podľa EAN, mobil a kamera
        var ean = new EanWindow(UkazkovyEan(), new CiselnikService().Volby(TypCiselnika.Kategorie, null, null));
        ean.Show();
        Pumpuj();
        Snimka(ean, "okno_ean");
        ean.Close();
        Pumpuj();
        var mobil = new MobilWindow();
        mobil.Show();
        Pumpuj();
        Snimka(mobil, "okno_mobil");
        mobil.Close();
        Pumpuj();
        var kamera = new KameraWindow();
        kamera.Show();
        cakanie.Restart();
        while (cakanie.ElapsedMilliseconds < 2500)
        {
            Pumpuj();
            Thread.Sleep(20);
        }

        Snimka(kamera, "okno_kamera");
        kamera.Close();
        Pumpuj();

        // karta položky s EAN
        var novaKarta = new PolozkaEditViewModel(null, null);
        OtvorAZatvor(new PolozkaEditWindow(novaKarta), "nová položka s poľom EAN");
        Krok("okná skenovania v poriadku");
    }

    /// <summary>Okná Etapy 8 pre kontrolu prekladov v každom jazyku.</summary>
    private static IEnumerable<(Func<Window> Okno, string Nazov)> OknaSkenovania(List<PolozkaRiadok> riadky)
    {
        var idInventury = new InventuraService().Nova("Jazyk");
        yield return (() => new StitkyWindow(new StitkyViewModel(riadky.Take(5).Select(r => r.Id).ToList(), "test")), "stitky");
        yield return (() => new InventuraWindow(new InventuraViewModel(idInventury)), "inventura");
        yield return (() => new EanWindow(UkazkovyEan(), new CiselnikService().Volby(TypCiselnika.Kategorie, null, null)), "ean");
        yield return (() => new MobilWindow(), "mobil");
        yield return (() => new KameraWindow(), "kamera");
    }

    private static VysledokEan UkazkovyEan() => new("4006381333931",
        new List<UdajeProduktu>
        {
            new("Open Icecat", "Notebook Dell Latitude 5540", "Dell", "Latitude 5540", "notebook", null, "15.6\" Full HD, Intel Core i5"),
            new("UPCitemdb", "Dell Latitude 5540 Laptop", "Dell", "5540", "Laptops", null, null),
        },
        new List<string> { "UPCitemdb: test" });

    private static T Cakaj<T>(Task<T> uloha)
    {
        var cakanie = System.Diagnostics.Stopwatch.StartNew();
        while (!uloha.IsCompleted && cakanie.Elapsed < TimeSpan.FromSeconds(30))
        {
            Pumpuj();
            Thread.Sleep(10);
        }

        return uloha.GetAwaiter().GetResult();
    }

    /// <summary>QR kód ako obrázok BGRA (modul = n bodov, tichá zóna 4 moduly).</summary>
    private static (byte[] Data, int Sirka, int Vyska) ObrazokQr(string text, int modul)
    {
        var moduly = CiarovyKod.Qr(text);
        var pocet = moduly.GetLength(0);
        var strana = (pocet + 8) * modul;
        var data = Biely(strana, strana);
        for (var y = 0; y < pocet; y++)
        {
            for (var x = 0; x < pocet; x++)
            {
                if (moduly[x, y])
                {
                    Vypln(data, strana, (x + 4) * modul, (y + 4) * modul, modul, modul);
                }
            }
        }

        return (data, strana, strana);
    }

    /// <summary>Dva QR kódy vedľa seba (ako na krabici výrobku).</summary>
    private static (byte[] Data, int Sirka, int Vyska) DvaKody(string prvy, string druhy)
    {
        var a = ObrazokQr(prvy, 5);
        var b = ObrazokQr(druhy, 5);
        var vyska = Math.Max(a.Vyska, b.Vyska);
        var sirka = a.Sirka + b.Sirka;
        var data = Biely(sirka, vyska);
        for (var y = 0; y < vyska; y++)
        {
            if (y < a.Vyska)
            {
                Array.Copy(a.Data, y * a.Sirka * 4, data, y * sirka * 4, a.Sirka * 4);
            }

            if (y < b.Vyska)
            {
                Array.Copy(b.Data, y * b.Sirka * 4, data, (y * sirka + a.Sirka) * 4, b.Sirka * 4);
            }
        }

        return (data, sirka, vyska);
    }

    /// <summary>Čiarový kód Code 128 ako obrázok BGRA.</summary>
    private static (byte[] Data, int Sirka, int Vyska) ObrazokCode128(string text, int modul, int vyska)
    {
        var pruhy = CiarovyKod.Code128(text);
        var sirka = (pruhy.Length + 20) * modul;
        var data = Biely(sirka, vyska);
        for (var i = 0; i < pruhy.Length; i++)
        {
            if (pruhy[i])
            {
                Vypln(data, sirka, (i + 10) * modul, 10, modul, vyska - 20);
            }
        }

        return (data, sirka, vyska);
    }

    private static byte[] Biely(int sirka, int vyska)
    {
        var data = new byte[sirka * vyska * 4];
        Array.Fill(data, (byte)255);
        return data;
    }

    private static void Vypln(byte[] data, int sirka, int x0, int y0, int w, int h)
    {
        for (var y = y0; y < y0 + h; y++)
        {
            for (var x = x0; x < x0 + w; x++)
            {
                var i = (y * sirka + x) * 4;
                data[i] = 0;
                data[i + 1] = 0;
                data[i + 2] = 0;
            }
        }
    }

    private static byte[] Png(byte[] bgra, int sirka, int vyska)
    {
        var obrazok = BitmapSource.Create(sirka, vyska, 96, 96, PixelFormats.Bgra32, null, bgra, sirka * 4);
        var kodovac = new PngBitmapEncoder();
        kodovac.Frames.Add(BitmapFrame.Create(obrazok));
        using var prud = new MemoryStream();
        kodovac.Save(prud);
        return prud.ToArray();
    }
}
