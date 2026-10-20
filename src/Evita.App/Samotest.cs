using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Evita.Data;
using Evita.Models;
using Evita.Pomocne;
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
            Preklad.UlozJazyk("sk");
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
            using (var db = Databaza.Otvor())
            {
                Over(db.TypyUdalosti.Count() == Databaza.PociatocneTypyUdalosti.Length, "počiatočné typy udalostí");
                Over(db.Udalosti.Count() == 20, $"ukážkové udalosti kalendára: 20 (je {db.Udalosti.Count()})");
                Over(db.Ulohy.Count() == 7 && db.Ulohy.Count(u => u.Hotovo) == 1, $"ukážkové úlohy: 7 (je {db.Ulohy.Count()})");
                Over(db.Poznamky.Count() == 5 && db.Poznamky.Count(p => p.Pripnuta) == 2 && db.Poznamky.Any(p => p.UdalostId != null && p.Datum != null)
                     && db.Poznamky.Any(p => p.PolozkaId != null), "ukážkové poznámky s prepojeniami");
            }

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

            // ------------------------------------------------------------ pracovný kalendár
            var kalendarSluzba = new KalendarService();
            var dnesK = DateTime.Today;
            var typyK = kalendarSluzba.Typy();
            var iny = typyK.First(t => t.Nazov == "iné");
            var porada = kalendarSluzba.Vyskyty(dnesK.AddDays(-14), dnesK.AddDays(13), automaticke: false).Count(v => v.Nazov == "Porada IT");
            Over(porada == 4, $"týždenná porada sa za 4 týždne zobrazí 4-krát (je {porada})");
            var mesacna = kalendarSluzba.Uloz(new Udalost
            {
                Nazov = "Test mesačne 31.", TypUdalostiId = iny.Id, Zaciatok = new DateTime(2027, 1, 31, 9, 0, 0),
                Koniec = new DateTime(2027, 1, 31, 10, 0, 0), Opakovanie = OpakovanieUdalosti.Mesacne, OpakovanieDo = new DateTime(2027, 6, 30),
            });
            var vyskytyMesacne = kalendarSluzba.Vyskyty(new DateTime(2027, 1, 1), new DateTime(2027, 12, 31), automaticke: false)
                .Where(v => v.UdalostId == mesacna).ToList();
            Over(vyskytyMesacne.Count == 6 && vyskytyMesacne[1].Zaciatok == new DateTime(2027, 2, 28, 9, 0, 0),
                $"mesačné opakovanie od 31. 1.: 6 výskytov, vo februári 28. ({vyskytyMesacne.Count})");
            var viacDni = kalendarSluzba.Uloz(new Udalost
            {
                Nazov = "Test obdobie", TypUdalostiId = iny.Id, Zaciatok = new DateTime(2027, 3, 29), Koniec = new DateTime(2027, 4, 2), CelyDen = true,
            });
            Over(kalendarSluzba.Vyskyty(new DateTime(2027, 4, 1), new DateTime(2027, 4, 1), automaticke: false).Any(v => v.UdalostId == viacDni && v.ViacDni),
                "udalosť cez viac dní je vidieť aj v jej strede");
            Over(Ocakavaj<EvitaException>(() => kalendarSluzba.Uloz(new Udalost
            {
                Nazov = "zlá", TypUdalostiId = iny.Id, Zaciatok = new DateTime(2027, 3, 2, 10, 0, 0), Koniec = new DateTime(2027, 3, 2, 9, 0, 0),
            })), "koniec pred začiatkom je odmietnutý");
            kalendarSluzba.Vymaz(mesacna);
            kalendarSluzba.Vymaz(viacDni);
            Over(kalendarSluzba.Vyskyty(new DateTime(2027, 1, 1), new DateTime(2027, 12, 31), automaticke: false).All(v => v.UdalostId != mesacna),
                "vymazanie udalosti");

            var automaticke = kalendarSluzba.Vyskyty(dnesK, dnesK.AddDays(365));
            Over(automaticke.Any(v => v.Zdroj == "záruka") && automaticke.Any(v => v.Zdroj == "revízia"), "automatické termíny záruk a revízií");
            Over(automaticke.Any(v => v.Zdroj == "inventúra") && automaticke.Any(v => v.Zdroj == "záloha"), "automatické termíny inventúry a záloh");
            var nastaveniaK = kalendarSluzba.NacitajNastavenia();
            kalendarSluzba.UlozNastavenia(nastaveniaK with { Zaruky = false });
            Over(kalendarSluzba.Vyskyty(dnesK, dnesK.AddDays(365)).All(v => v.Zdroj != "záruka"), "automatický termín sa dá vypnúť");
            kalendarSluzba.UlozNastavenia(nastaveniaK);

            var prehladK = kalendarSluzba.Prehlad(dnesK);
            Over(prehladK.PoTermine.Any(v => v.Nazov.StartsWith("Nákup tonerov")), "úloha po termíne v prehľade dňa");
            Over(prehladK.Dnes.Any(v => v.Nazov == "Porada vedenia"), "dnešná udalosť v prehľade dňa");
            Over(prehladK.Pripomienky.Any(v => v.Nazov.StartsWith("Školenie")), "pripomienka týždeň vopred v prehľade dňa");
            var tonery = prehladK.PoTermine.First(v => v.Nazov.StartsWith("Nákup tonerov"));
            kalendarSluzba.NastavStav(tonery.UdalostId!.Value, StavUdalosti.Hotovo);
            Over(!kalendarSluzba.Prehlad(dnesK).PoTermine.Any(v => v.Nazov.StartsWith("Nákup tonerov")), "hotová úloha už nie je po termíne");
            kalendarSluzba.NastavStav(tonery.UdalostId!.Value, StavUdalosti.Naplanovane);

            var ics = kalendarSluzba.Ics(dnesK.AddDays(-30), dnesK.AddDays(365), true);
            Over(ics.StartsWith("BEGIN:VCALENDAR\r\n", StringComparison.Ordinal) && ics.Contains("RRULE:FREQ=WEEKLY") && ics.Contains("DTSTART;VALUE=DATE:")
                 && ics.TrimEnd().EndsWith("END:VCALENDAR", StringComparison.Ordinal), "export .ics");
            Over(ics.Split("\r\n").All(r => System.Text.Encoding.UTF8.GetByteCount(r) <= 75), "riadky .ics majú najviac 75 bajtov");

            var novyTyp = kalendarSluzba.UlozTyp(0, "testovací typ", "#123456");
            Over(Ocakavaj<EvitaException>(() => kalendarSluzba.UlozTyp(0, "Servis", "#000000")), "duplicitný typ udalosti je odmietnutý");
            Over(kalendarSluzba.VymazTyp(novyTyp), "nepoužitý typ sa vymaže");
            Over(!kalendarSluzba.VymazTyp(typyK.First(t => t.Nazov == "porada").Id) && kalendarSluzba.Typy(true).All(t => t.Nazov != "porada"),
                "používaný typ sa len skryje");
            kalendarSluzba.UlozTyp(typyK.First(t => t.Nazov == "porada").Id, "porada", typyK.First(t => t.Nazov == "porada").Farba);
            Krok("pracovný kalendár v poriadku");

            // ------------------------------------------------------------ úlohy, poznámky, kurzy, počasie, konfigurácia
            var ulohySluzba = new UlohyService();
            var otvoreneUlohy = ulohySluzba.Nacitaj(ajHotove: false);
            Over(otvoreneUlohy.Count == 6 && otvoreneUlohy[0].Vysoka && otvoreneUlohy.Any(u => u.PoTermine), "úlohy: zoradenie (vysoká priorita hore) a po termíne");
            var novaUlohaId = ulohySluzba.Pridaj("Test úlohy zo samotestu", DateTime.Today.AddDays(1), PrioritaUlohy.Nizka, riadky[0].Id);
            Over(ulohySluzba.Nacitaj(ajHotove: false, polozkaId: riadky[0].Id).Any(u => u.Id == novaUlohaId && u.PolozkaCislo == riadky[0].PrirastkoveCislo),
                "úloha naviazaná na položku");
            ulohySluzba.NastavHotovo(novaUlohaId, true);
            Over(ulohySluzba.Nacitaj(novaUlohaId) is { Hotovo: true, DatumHotovo: not null }, "úloha označená ako hotová");
            Over(Ocakavaj<EvitaException>(() => ulohySluzba.Pridaj("   ")), "prázdna úloha je odmietnutá");
            Over(ulohySluzba.VymazHotove() == 2 && ulohySluzba.Pocty().Hotove == 0, "vymazanie hotových úloh");
            ulohySluzba.Pridaj("Vyčistiť filtre projektorov");
            ulohySluzba.NastavHotovo(ulohySluzba.Nacitaj(ajHotove: false).First(u => u.Text == "Vyčistiť filtre projektorov").Id, true);

            var poznamkySluzba = new PoznamkyService();
            var novaPoznamka = poznamkySluzba.Uloz(new Poznamka { Text = "Prvý riadok je názov\nďalší text", Datum = DateTime.Today.AddDays(2), PolozkaId = riadky[1].Id });
            var nacitanaPoznamka = poznamkySluzba.Nacitaj(novaPoznamka)!;
            Over(nacitanaPoznamka.Nazov == "Prvý riadok je názov" && nacitanaPoznamka.Farba == PoznamkyService.Farby[0], "poznámka bez názvu dostane názov z prvého riadku");
            Over(poznamkySluzba.Zoznam("ďalší TEXT").Any(p => p.Id == novaPoznamka) && poznamkySluzba.Zoznam("dalsi text").Any(p => p.Id == novaPoznamka),
                "hľadanie v poznámkach bez ohľadu na diakritiku");
            Over(poznamkySluzba.PreDni(DateTime.Today, DateTime.Today.AddDays(3)).Any(p => p.Id == novaPoznamka), "poznámka naviazaná na deň v kalendári");
            Over(poznamkySluzba.PrePolozku(riadky[1].Id).Any(p => p.Id == novaPoznamka), "poznámka naviazaná na položku");
            poznamkySluzba.NastavPripnutie(novaPoznamka, true);
            Over(poznamkySluzba.Zoznam()[0].Pripnuta && poznamkySluzba.Pripnute(10).Any(p => p.Id == novaPoznamka), "pripnutá poznámka je hore");
            Over(Ocakavaj<EvitaException>(() => poznamkySluzba.Uloz(new Poznamka { Nazov = " ", Text = "\n" })), "prázdna poznámka je odmietnutá");
            var skolenieUdalost = kalendarSluzba.Vyskyty(dnesK, dnesK.AddDays(30), automaticke: false).First(v => v.Nazov.StartsWith("Školenie"));
            Over(poznamkySluzba.PreUdalost(skolenieUdalost.UdalostId!.Value).Count == 1, "poznámka naviazaná na udalosť");
            poznamkySluzba.Vymaz(novaPoznamka);
            Over(poznamkySluzba.Nacitaj(novaPoznamka) is null, "vymazanie poznámky");

            // kurzy ECB a počasie Open-Meteo – čítanie odpovedí (bez internetu)
            const string ukazkaEcb = """
                <?xml version="1.0" encoding="UTF-8"?>
                <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
                  <gesmes:subject>Reference rates</gesmes:subject>
                  <Cube>
                    <Cube time="2026-10-16"><Cube currency="USD" rate="1.0800"/><Cube currency="CZK" rate="24.600"/></Cube>
                    <Cube time="2026-10-19"><Cube currency="USD" rate="1.0850"/><Cube currency="CZK" rate="24.512"/><Cube currency="JPY" rate="162.31"/></Cube>
                  </Cube>
                </gesmes:Envelope>
                """;
            var dniEcb = KurzyService.Precitaj(ukazkaEcb);
            Over(dniEcb.Count == 2 && dniEcb[0].Datum == new DateTime(2026, 10, 19) && dniEcb[0].Kurzy["USD"] == 1.0850m && dniEcb[0].Kurzy.Count == 3,
                "kurzy ECB: čítanie XML (najnovší deň prvý)");
            Over(KurzyService.Prepocitaj(100m, "EUR", "USD", dniEcb[0].Kurzy) == 108.5m
                 && KurzyService.Prepocitaj(24.512m, "CZK", "EUR", dniEcb[0].Kurzy) == 1m
                 && KurzyService.Prepocitaj(10m, "USD", "CZK", dniEcb[0].Kurzy) is > 225m and < 226m, "kurzy: prepočet cez euro");
            Over(new KurzMeny("USD", 1.085m, 1.08m, new[] { 1.08m, 1.085m }).ZmenaPercent == 0.46m, "kurzy: zmena oproti predošlému dňu");
            const string ukazkaMiesta = """{"results":[{"id":3058000,"name":"Pezinok","latitude":48.28986,"longitude":17.26664,"country_code":"SK","admin1":"Bratislavský kraj","admin2":"Okres Pezinok","country":"Slovensko"},{"id":1,"name":"bez súradníc"}],"generationtime_ms":1.2}""";
            var miestaUkazka = PocasieService.PrecitajMiesta(ukazkaMiesta);
            Over(miestaUkazka.Count == 1 && miestaUkazka[0].Popis == "Pezinok, Okres Pezinok, Bratislavský kraj, Slovensko", "počasie: čítanie vyhľadaných miest");
            Over(PocasieService.PrecitajMiesta("""{"generationtime_ms":0.5}""").Count == 0, "počasie: nič sa nenašlo");
            const string ukazkaPredpovede = """{"latitude":48.3,"longitude":17.26,"current":{"time":"2026-10-20T14:00","interval":900,"temperature_2m":12.4,"apparent_temperature":10.1,"relative_humidity_2m":71,"weather_code":3,"wind_speed_10m":11.2,"precipitation":0.0,"is_day":1},"daily":{"time":["2026-10-20","2026-10-21"],"weather_code":[3,61],"temperature_2m_max":[14.2,12.0],"temperature_2m_min":[6.1,null],"precipitation_probability_max":[10,80]}}""";
            var predpoved = PocasieService.PrecitajPredpoved(ukazkaPredpovede, miestaUkazka[0], DateTime.Now);
            Over(predpoved.Teplota == 12.4 && predpoved.Vlhkost == 71 && predpoved.Kod == 3 && predpoved.Den && predpoved.Dni.Count == 2
                 && predpoved.Dni[1].Kod == 61 && predpoved.Dni[1].Min is null && predpoved.Dni[1].Zrazky == 80, "počasie: čítanie predpovede");
            Over(predpoved.Popis == "zamračené" && predpoved.Dni[1].Popis == "slabý dážď" && predpoved.TeplotaText == "12 °C", "počasie: popis podľa kódu WMO");

            // skutočné stiahnutie (na zostavovacom serveri je internet; bez neho sa len zapíše)
            try
            {
                var miesta = new PocasieService().HladajAsync("Pezinok").GetAwaiter().GetResult();
                Krok($"počasie: vyhľadanie Pezinok – {miesta.Count} miest, prvé {miesta.FirstOrDefault()?.Popis}");
                if (miesta.Count > 0)
                {
                    new PocasieService().UlozMiesto(miesta[0]);
                    var naozaj = new PocasieService().StiahniAsync(miesta[0]).GetAwaiter().GetResult();
                    Krok($"počasie: {naozaj.TeplotaText}, {naozaj.Popis}, {naozaj.Dni.Count} dní predpovede");
                    Over(new PocasieService().ZVyrovnavacejPamate() is { } ulozene && ulozene.Teplota == naozaj.Teplota, "počasie: vyrovnávacia pamäť");
                }

                var listok = new KurzyService().StiahniAsync().GetAwaiter().GetResult();
                Krok($"kurzy: {Sk.Datum(listok.Datum)} – {string.Join(", ", listok.Meny.Select(m => $"{m.Kod} {m.KurzText} {m.ZmenaText}"))}");
                Over(listok.Meny.Count == 8 && new KurzyService().ZVyrovnavacejPamate()?.Meny.Count == 8, "kurzy: stiahnutý kurzový lístok predvolených mien");
            }
            catch (EvitaException ex)
            {
                Krok("bez internetu: " + ex.Message + " – " + ex.InnerException?.Message);
            }

            // konfigurácia programu: export, zmena, import
            var konfiguracia = Path.Combine(Databaza.Priecinok, "konfiguracia.json");
            new KurzyService().UlozMeny(new[] { "GBP", "CHF" });
            using (var db = Databaza.Otvor())
            {
                Databaza.Zapis(db, "Dashboard.Skryte", "graf");
                db.SaveChanges();
            }

            var exportovane = KonfiguraciaService.Exportuj(konfiguracia);
            var obsahKonfiguracie = File.ReadAllText(konfiguracia);
            Over(exportovane > 5 && obsahKonfiguracie.Contains("\"Kurzy.Meny\": \"GBP,CHF\"") && obsahKonfiguracie.Contains("\"Program.Jazyk\"")
                 && !obsahKonfiguracie.Contains("Schema.Verzia") && !obsahKonfiguracie.Contains("PrirastkoveCislo"), "konfigurácia: export len nastavení");
            new KurzyService().UlozMeny(new[] { "USD" });
            var typServis = typyK.First(t => t.Nazov == "servis");
            kalendarSluzba.UlozTyp(typServis.Id, "servis", "#000000");
            using (var db = Databaza.Otvor())
            {
                Databaza.Zapis(db, "Dashboard.Skryte", "");
                db.SaveChanges();
            }

            var nacitanaKonfiguracia = KonfiguraciaService.Precitaj(konfiguracia);
            Over(KonfiguraciaService.Obsah(nacitanaKonfiguracia).Count >= 4, "konfigurácia: popis obsahu");
            var vysledokKonfiguracie = KonfiguraciaService.Importuj(nacitanaKonfiguracia);
            Over(new KurzyService().ZvoleneMeny().SequenceEqual(new[] { "GBP", "CHF" }) && kalendarSluzba.Typy().First(t => t.Id == typServis.Id).Farba == typServis.Farba
                 && !vysledokKonfiguracie.ZmenaJazyka, "konfigurácia: import obnovil nastavenia a farby typov");
            using (var db = Databaza.Otvor())
            {
                Over(Databaza.Citaj(db, "Dashboard.Skryte") == "graf", "konfigurácia: panely dashboardu");
                Databaza.Zapis(db, "Dashboard.Skryte", "");
                db.SaveChanges();
            }

            new KurzyService().UlozMeny(KurzyService.PredvoleneMeny.Split(','));
            File.WriteAllText(Path.Combine(Databaza.Priecinok, "zla_konfiguracia.json"), "{\"Format\":\"niečo iné\"}");
            Over(Ocakavaj<EvitaException>(() => KonfiguraciaService.Precitaj(Path.Combine(Databaza.Priecinok, "zla_konfiguracia.json"))),
                "konfigurácia: cudzí súbor je odmietnutý");

            // preklad: tvary slov podľa počtu a jazyka
            Over(Preklad.Pocet(1, "{0} položka", "{0} položky", "{0} položiek") == "1 položka"
                 && Preklad.Pocet(3, "{0} položka", "{0} položky", "{0} položiek") == "3 položky"
                 && Preklad.Pocet(12, "{0} položka", "{0} položky", "{0} položiek") == "12 položiek", "preklad: slovenské tvary podľa počtu");
            Over(Sk.SkusPrecitatSumu("1,234.50", out var sumaEn) && sumaEn == 1234.5m && Sk.SkusPrecitatSumu("1 234,5 €", out var sumaSk) && sumaSk == 1234.5m
                 && Sk.SkusPrecitatSumu("1.234,50", out var sumaDe) && sumaDe == 1234.5m && Sk.SkusPrecitatSumu("12.5", out var sumaBodka) && sumaBodka == 12.5m,
                "suma s oddeľovačmi tisícov v rôznych jazykoch");
            Krok("úlohy, poznámky, kurzy, počasie a konfigurácia v poriadku");

            // aktualizácia štruktúry zo staršej verzie (simulácia databázy z verzie 0.1)
            const string stlpceUloh = "\"Id\", \"Guid\", \"Text\", \"Popis\", \"Termin\", \"Priorita\", \"Hotovo\", \"DatumHotovo\", \"Vytvorene\", \"Poradie\", \"PolozkaId\"";
            const string stlpcePoznamok = "\"Id\", \"Guid\", \"Nazov\", \"Text\", \"Farba\", \"Pripnuta\", \"Vytvorene\", \"Zmenene\", \"Datum\", \"PolozkaId\", \"UdalostId\"";
            Vykonaj($"CREATE TABLE \"ZalohaUloh\" AS SELECT {stlpceUloh} FROM \"Ulohy\"; CREATE TABLE \"ZalohaPoznamok\" AS SELECT {stlpcePoznamok} FROM \"Poznamky\"; DROP TABLE \"Poznamky\"; DROP TABLE \"Ulohy\";");
            Vykonaj("CREATE TABLE \"ZalohaUdalosti\" AS SELECT * FROM \"Udalosti\"; DROP TABLE \"Udalosti\"; DROP TABLE \"TypyUdalosti\"; DROP TABLE \"ZaznamyZaloh\"; UPDATE \"Nastavenia\" SET \"Hodnota\" = '1' WHERE \"Kluc\" = 'Schema.Verzia';");
            Databaza.Inicializuj();
            Vykonaj("INSERT INTO \"Udalosti\" (\"Id\", \"Guid\", \"Nazov\", \"Popis\", \"TypUdalostiId\", \"Druh\", \"Zaciatok\", \"Koniec\", \"CelyDen\", \"Opakovanie\", \"OpakovanieDo\", \"Pripomienka\", \"PolozkaId\", \"MiestnostId\", \"OsobaId\", \"Stav\", \"Vytvorene\", \"Zmenene\") SELECT \"Id\", \"Guid\", \"Nazov\", \"Popis\", \"TypUdalostiId\", \"Druh\", \"Zaciatok\", \"Koniec\", \"CelyDen\", \"Opakovanie\", \"OpakovanieDo\", \"Pripomienka\", \"PolozkaId\", \"MiestnostId\", \"OsobaId\", \"Stav\", \"Vytvorene\", \"Zmenene\" FROM \"ZalohaUdalosti\"; DROP TABLE \"ZalohaUdalosti\";");
            Vykonaj($"INSERT INTO \"Ulohy\" ({stlpceUloh}) SELECT {stlpceUloh} FROM \"ZalohaUloh\"; DROP TABLE \"ZalohaUloh\"; INSERT INTO \"Poznamky\" ({stlpcePoznamok}) SELECT {stlpcePoznamok} FROM \"ZalohaPoznamok\"; DROP TABLE \"ZalohaPoznamok\";");
            using (var db = Databaza.Otvor())
            {
                Over(db.TypyUdalosti.Count() == Databaza.PociatocneTypyUdalosti.Length && db.Udalosti.Count() == 20, "aktualizácia štruktúry vytvorí tabuľky kalendára");
                Over(db.Ulohy.Count() == 7 && db.Poznamky.Count() == 5, "aktualizácia štruktúry vytvorí tabuľky úloh a poznámok");
            }

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
            int udalostiPredZachranou, ulohyPredZachranou, poznamkyPredZachranou;
            using (var db = Databaza.Otvor())
            {
                udalostiPredZachranou = db.Udalosti.Count();
                ulohyPredZachranou = db.Ulohy.Count();
                poznamkyPredZachranou = db.Poznamky.Count();
            }

            var zachrana = diagnostika.Zachran();
            Over(zachrana.Ok, $"záchrana dát: {string.Join(" | ", zachrana.Riadky)}");
            Over(sluzba.NacitajRiadky().Count == pocetPredZachranou, "záchrana dát zachovala všetky položky");
            using (var db = Databaza.Otvor())
            {
                Over(db.Udalosti.Count() == udalostiPredZachranou, "záchrana dát zachovala udalosti kalendára");
                Over(db.Ulohy.Count() == ulohyPredZachranou && db.Poznamky.Count() == poznamkyPredZachranou && poznamkyPredZachranou > 0,
                    "záchrana dát zachovala úlohy a poznámky");
            }
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
            foreach (var vzorNaStiahnutie in VzoryService.Zoznam)
            {
                var stiahnuty = VzoryService.Stiahni(vzorNaStiahnutie);
                Over(File.Exists(stiahnuty) && Path.GetFileName(stiahnuty) == vzorNaStiahnutie.Subor
                     && Path.GetDirectoryName(stiahnuty) == VzoryService.PriecinokStiahnutych, $"stiahnutie vzoru {vzorNaStiahnutie.Nazov}");
            }

            Over(File.Exists(Path.Combine(VzoryService.PriecinokStiahnutych, XsdEviTA.NazovSuboru)), "pri vzore XML sa stiahne aj evita.xsd");
            Over(Path.GetFileName(VzoryService.Stiahni(VzoryService.Zoznam[0])) == "EviTA_vzor_Excel (2).xlsx", "opakované stiahnutie neprepíše súbor");
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
                int historiaPred, dalsieCisloPred, osobyPred, udalostiPred, ulohyPred, poznamkyPred;
                using (var db = Databaza.Otvor())
                {
                    udalostiPred = db.Udalosti.Count();
                    ulohyPred = db.Ulohy.Count();
                    poznamkyPred = db.Poznamky.Count();
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
                    Over(db.Udalosti.Count() == udalostiPred && udalostiPred > 0, $"{format}: prenesené udalosti kalendára ({db.Udalosti.Count()} z {udalostiPred})");
                    Over(db.Udalosti.Any(u => u.PolozkaId != null), $"{format}: udalosti zostali naviazané na položky");
                    Over(db.Ulohy.Count() == ulohyPred && ulohyPred > 0 && db.Ulohy.Any(u => u.PolozkaId != null) && db.Ulohy.Any(u => u.Hotovo),
                        $"{format}: prenesené úlohy ({db.Ulohy.Count()} z {ulohyPred})");
                    Over(db.Poznamky.Count() == poznamkyPred && db.Poznamky.Any(p => p.UdalostId != null) && db.Poznamky.Any(p => p.PolozkaId != null)
                         && db.Poznamky.Count(p => p.Pripnuta) == 2, $"{format}: prenesené poznámky s prepojeniami ({db.Poznamky.Count()} z {poznamkyPred})");
                }
            }

            Krok("import v poriadku");

            // po prenose celej databázy majú položky nové interné Id
            riadky = sluzba.NacitajRiadky();

            // ------------------------------------------------------------ tlačové zostavy
            var logo = Path.Combine(exportPriecinok, "logo.png");
            File.WriteAllBytes(logo, UkazkoveLogo());
            NastaveniaTlace.UlozNazov("Základná škola Ukážková, Hlavná 1, Bratislava");
            NastaveniaTlace.UlozLogo(File.ReadAllBytes(logo));
            Over(NastaveniaTlace.Nacitaj() is { Nazov: not null, Logo.Length: > 0 }, "názov organizácie a logo pre hlavičku");
            var zostavy = new ZostavyService();
            var dnesTest = DateTime.Today;
            foreach (var popisZostavy in ZostavyService.Zoznam.Where(z => z.Dostupna))
            {
                var moznostiZostavy = new MoznostiZostavy
                {
                    Druh = popisZostavy.Druh,
                    Stlpce = popisZostavy.PredvoleneStlpce,
                    NaSirku = ZostavyService.OdporucNaSirku(popisZostavy.Druh, popisZostavy.PredvoleneStlpce.Length),
                    VrataneVyradenych = popisZostavy.PredvoleneVyradene,
                    Od = popisZostavy.Druh == DruhZostavy.Kalendar ? new DateTime(dnesTest.Year, dnesTest.Month, 1)
                        : popisZostavy.MaObdobie ? new DateTime(2019, 1, 1) : null,
                    Do = popisZostavy.Druh == DruhZostavy.Kalendar ? new DateTime(dnesTest.Year, dnesTest.Month, 1).AddMonths(2).AddDays(-1)
                        : popisZostavy.MaObdobie ? dnesTest : null,
                    Zoskupenie = Zoskupenie.Miestnost,
                    Podpis = popisZostavy.MaPodpis,
                    KazdaNaNovejStrane = popisZostavy.MaNovuStranu,
                    IdPoloziek = popisZostavy.Druh == DruhZostavy.Vyfiltrovane ? riadky.Where(r => r.Kategoria == "notebook").Select(r => r.Id).ToList() : null,
                    PopisFiltra = popisZostavy.Druh == DruhZostavy.Vyfiltrovane ? "kategória notebook" : null,
                };
                var cestaZostavy = Path.Combine(exportPriecinok, $"zostava_{popisZostavy.Druh}.pdf");
                zostavy.UlozPdf(moznostiZostavy, cestaZostavy);
                var stranyZostavy = NahladDokumentu.PocetStran(NahladDokumentu.Priprav(zostavy.Vytvor(moznostiZostavy)));
                Over(File.Exists(cestaZostavy) && new FileInfo(cestaZostavy).Length > 2000 && stranyZostavy > 0,
                    $"zostava {popisZostavy.Nazov}: {stranyZostavy} strán");
            }

            zostavy.UlozPdf(new MoznostiZostavy
            {
                Druh = DruhZostavy.Kalendar, KalendarZoznam = true, NaSirku = true, Od = dnesTest.AddDays(-7), Do = dnesTest.AddDays(60),
            }, Path.Combine(exportPriecinok, "zostava_Kalendar_zoznam.pdf"));
            Over(Ocakavaj<EvitaException>(() => zostavy.Vytvor(new MoznostiZostavy { Druh = DruhZostavy.Kalendar, Od = dnesTest, Do = dnesTest.AddDays(-1) })),
                "kalendár s obráteným obdobím je odmietnutý");
            var miestnostSPolozkami = riadky.First(r => r.MiestnostId is not null).MiestnostId;
            zostavy.UlozPdf(new MoznostiZostavy { Druh = DruhZostavy.Historia, NaSirku = true, MiestnostId = miestnostSPolozkami },
                Path.Combine(exportPriecinok, "zostava_Historia_miestnost.pdf"));
            zostavy.UlozPdf(new MoznostiZostavy { Druh = DruhZostavy.Historia, NaSirku = true, PrirastkoveCislo = riadky[0].PrirastkoveCislo },
                Path.Combine(exportPriecinok, "zostava_Historia_polozka.pdf"));
            zostavy.UlozPdf(new MoznostiZostavy
            {
                Druh = DruhZostavy.SuhrnnyZoznam,
                Stlpce = new[] { StlpcePrenosu.Cislo, StlpcePrenosu.Nazov, StlpcePrenosu.Cena },
                Zoskupenie = Zoskupenie.Kategoria,
            }, Path.Combine(exportPriecinok, "zostava_Suhrn_kategorie.pdf"));
            Over(Ocakavaj<EvitaException>(() => zostavy.Vytvor(new MoznostiZostavy { Druh = DruhZostavy.SuhrnnyZoznam })), "zostava bez stĺpcov je odmietnutá");
            Krok("tlačové zostavy v poriadku");

            // obrazovky a okná
            var okno = new MainWindow();
            aplikacia.MainWindow = okno;
            okno.Show();
            Pumpuj();
            var model = (MainViewModel)okno.DataContext;
            foreach (var sekcia in new[] { "dashboard", "kalendar", "ulohy", "poznamky", "evidencia", "tlac", "import", "kategorie", "miestnosti", "osoby", "sviatky", "servis", "nastavenia", "dashboard" })
            {
                okno.OznacSekciu(sekcia);
                Pumpuj();
                if (sekcia is "import")
                {
                    model.ImportExport.StiahnutVzorCommand.Execute(VzoryService.Zoznam[0]);
                    Pumpuj();
                    Over(model.ImportExport.StiahnutyVzor.EndsWith(".xlsx", StringComparison.Ordinal), "obrazovka Import a export: stiahnutie vzoru");
                }

                if (sekcia is "import" or "evidencia" or "tlac" or "kalendar" or "dashboard" or "ulohy" or "poznamky" or "nastavenia")
                {
                    Snimka(okno, "okno_" + sekcia);
                }

                PrejdiZalozky(okno);
                Krok($"obrazovka: {sekcia}");
            }

            Over(model.Dashboard.MeninyDnes.Length > 0 && model.Dashboard.MeninyDnes != "Kalendárium nie je dostupné", "dashboard: meniny");
            Over(model.Dashboard.Ulohy.Count == 6 && model.Dashboard.PripnutePoznamky.Count == 2, "dashboard: úlohy a pripnuté poznámky");
            model.Dashboard.NovaUloha = "Rýchla úloha z dashboardu";
            model.Dashboard.PridatUlohuCommand.Execute(null);
            Over(model.Dashboard.Ulohy.Any(u => u.Text == "Rýchla úloha z dashboardu") && model.Dashboard.NovaUloha.Length == 0, "dashboard: rýchle pridanie úlohy");
            model.Dashboard.PrepnutHotovoCommand.Execute(model.Dashboard.Ulohy.First(u => u.Text == "Rýchla úloha z dashboardu"));
            Over(model.Dashboard.Ulohy.All(u => u.Text != "Rýchla úloha z dashboardu"), "dashboard: hotová úloha zmizne zo zoznamu");
            model.Dashboard.SumaPrepoctu = "100";
            var cakanie = System.Diagnostics.Stopwatch.StartNew();
            while (!model.Dashboard.PosledneNacitanieZoSiete.IsCompleted && cakanie.Elapsed < TimeSpan.FromSeconds(40))
            {
                Pumpuj();
                Thread.Sleep(50);
            }

            Pumpuj();
            Krok($"dashboard: počasie {(model.Dashboard.JePocasie ? model.Dashboard.Pocasie!.TeplotaText : model.Dashboard.PocasieStav)}, kurzy {model.Dashboard.Kurzy.Count} ({model.Dashboard.KurzyStav}), prepočet {model.Dashboard.VysledokPrepoctu}");
            Snimka(okno, "okno_dashboard_pocasie_kurzy");

            // úlohy a poznámky na obrazovkách
            okno.OznacSekciu("ulohy");
            Pumpuj();
            model.Ulohy.NovyText = "Úloha z obrazovky Úlohy";
            model.Ulohy.NovaPriorita = PrioritaUlohy.Vysoka;
            model.Ulohy.PridatCommand.Execute(null);
            Pumpuj();
            Over(model.Ulohy.Vybrana is { Text: "Úloha z obrazovky Úlohy", Vysoka: true }, "obrazovka Úlohy: pridanie a výber");
            model.Ulohy.Popis = "popis zo samotestu";
            model.Ulohy.PolozkaCislo = riadky[0].PrirastkoveCislo.ToString();
            model.Ulohy.UlozitCommand.Execute(null);
            Pumpuj();
            Over(model.Ulohy.Vybrana is { MaPopis: true } vybrataUloha && vybrataUloha.PolozkaId == riadky[0].Id, "obrazovka Úlohy: úprava s položkou");
            Snimka(okno, "okno_ulohy");
            model.Ulohy.JeVsetky = true;
            Pumpuj();
            Over(model.Ulohy.Zoznam.Any(u => u.Hotovo), "obrazovka Úlohy: všetky aj s hotovými");
            model.Ulohy.JeOtvorene = true;

            okno.OznacSekciu("poznamky");
            Pumpuj();
            Over(model.Poznamky.Editor is not null && model.Poznamky.Zoznam.Count == 5 && model.Poznamky.Zoznam[0].Pripnuta, "obrazovka Poznámky: zoznam a otvorená poznámka");
            model.Poznamky.NovaPoznamkaCommand.Execute(null);
            Pumpuj();
            model.Poznamky.Editor!.Nazov = "Poznámka zo samotestu";
            model.Poznamky.Editor.Text = "Text poznámky\ndruhý riadok";
            model.Poznamky.Editor.PolozkaCislo = riadky[2].PrirastkoveCislo.ToString();
            model.Poznamky.Editor.Datum = DateTime.Today;
            model.Poznamky.Editor.Farba = PoznamkyService.Farby[4];
            Pumpuj();
            Over(model.Poznamky.Editor.Zmenena && model.Poznamky.Editor.PolozkaText == riadky[2].Nazov, "obrazovka Poznámky: úprava novej poznámky");
            model.Poznamky.Vybrana = model.Poznamky.Zoznam[1];
            Pumpuj();
            Over(model.Poznamky.Zoznam.Any(p => p.Nazov == "Poznámka zo samotestu" && p.PolozkaId == riadky[2].Id && p.Datum == DateTime.Today),
                "obrazovka Poznámky: rozpísaná poznámka sa pri prechode uloží");
            model.Poznamky.Hladat = "zo samotestu";
            Pumpuj();
            Over(model.Poznamky.Zoznam.Count == 1, "obrazovka Poznámky: hľadanie");
            model.Poznamky.Vybrana = model.Poznamky.Zoznam[0];
            Pumpuj();
            Snimka(okno, "okno_poznamky");
            model.Poznamky.Hladat = "";
            Pumpuj();

            okno.OznacSekciu("nastavenia");
            Pumpuj();
            Over(model.Nastavenia.Meny.Count == KurzyService.VsetkyMeny.Length && model.Nastavenia.Meny.Count(m => m.Zvolena) == 8, "obrazovka Nastavenia: meny");
            model.Nastavenia.Meny.First(m => m.Kod == "SEK").Zvolena = true;
            Over(new KurzyService().ZvoleneMeny().Contains("SEK"), "obrazovka Nastavenia: zmena mien sa uloží");
            model.Nastavenia.PredvoleneMenyCommand.Execute(null);
            model.Nastavenia.CestaExportuBezDialogu = Path.Combine(exportPriecinok, "EviTA_konfiguracia.json");
            model.Nastavenia.ExportovatKonfiguraciuCommand.Execute(null);
            Over(File.Exists(model.Nastavenia.CestaExportuBezDialogu), "obrazovka Nastavenia: export konfigurácie");
            model.Nastavenia.CestaImportuBezDialogu = model.Nastavenia.CestaExportuBezDialogu;
            model.Nastavenia.BezRestartu = true;
            model.Nastavenia.ImportovatKonfiguraciuCommand.Execute(null);
            Over(model.Nastavenia.KonfiguraciaText.Length > 0, "obrazovka Nastavenia: import konfigurácie");
            model.Nastavenia.VybranyJazyk = Preklad.Najdi("cs");
            model.Nastavenia.PouzitJazykCommand.Execute(null);
            Over(Preklad.JazykZDatabazy() == "cs", "obrazovka Nastavenia: uloženie jazyka");
            model.Nastavenia.ResetJazykaCommand.Execute(null);
            Over(Preklad.JazykZDatabazy() == "en" && model.Nastavenia.VybranyJazyk.Kod == "en", "Language reset nastaví angličtinu");
            Preklad.UlozJazyk("sk");
            Pumpuj();
            Snimka(okno, "okno_nastavenia");
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

            // tlač: obrazovka, náhľad a PDF
            model.Tlac.Obnov();
            model.Tlac.NastavLogoZoSuboru(Path.Combine(exportPriecinok, "logo.png"));
            Over(model.Tlac.MaLogo && model.Tlac.NazovOrganizacie.Length > 0, "obrazovka tlače: logo a názov organizácie");
            model.Tlac.Vybrana = ZostavyService.Popis(DruhZostavy.PoMiestnostiach);
            Over(model.Tlac.UkazPodpis && model.Tlac.UkazMiestnost && !model.Tlac.UkazObdobie && model.Tlac.Stlpce.Count == StlpcePrenosu.Vsetky.Count,
                "obrazovka tlače: voľby zostavy po miestnostiach");
            model.Tlac.VybranyStlpec = model.Tlac.Stlpce[1];
            model.Tlac.HoreCommand.Execute(null);
            Over(model.Tlac.Stlpce[0].Stlpec.Kluc == StlpcePrenosu.Nazov, "obrazovka tlače: zmena poradia stĺpcov");

            // uložené zostavy: voľby, poradie stĺpcov a obdobie počítané podľa dňa tlače
            var miestnostZostavy = model.Tlac.Miestnosti.First(v => v.Id != 0);
            model.Tlac.MiestnostId = miestnostZostavy.Id;
            model.Tlac.NaSirku = true;
            model.Tlac.Podpis = false;
            model.Tlac.NazovBezDialogu = "Inventúra – " + miestnostZostavy.Nazov;
            model.Tlac.UlozitZostavuCommand.Execute(null);
            model.Tlac.Vybrana = ZostavyService.Popis(DruhZostavy.Historia);
            model.Tlac.ObdobieUlozenia = ObdobieZostavy.MinulyMesiac;
            var minulyMesiac = UlozeneZostavy.Rozsah(ObdobieZostavy.MinulyMesiac, DateTime.Today)!.Value;
            Over(model.Tlac.Od == minulyMesiac.Od && model.Tlac.Do == minulyMesiac.Do, "obdobie zostavy: minulý mesiac");
            model.Tlac.NazovBezDialogu = "Zmeny za minulý mesiac";
            model.Tlac.UlozitZostavuCommand.Execute(null);
            Over(model.Tlac.UlozeneZostavy.Count == 2 && UlozeneZostavy.Nacitaj().Count == 2, "uloženie zostáv");
            var tlacZnova = new TlacViewModel(model);
            tlacZnova.Obnov();
            tlacZnova.VybranaUlozena = tlacZnova.UlozeneZostavy.First(z => z.Nazov.StartsWith("Inventúra"));
            Over(tlacZnova.Vybrana.Druh == DruhZostavy.PoMiestnostiach && tlacZnova.MiestnostId == miestnostZostavy.Id && tlacZnova.NaSirku
                 && !tlacZnova.Podpis && tlacZnova.Stlpce[0].Stlpec.Kluc == StlpcePrenosu.Nazov, "použitie uloženej zostavy");
            tlacZnova.VybranaUlozena = tlacZnova.UlozeneZostavy.First(z => z.Nazov.StartsWith("Zmeny"));
            Over(tlacZnova.Vybrana.Druh == DruhZostavy.Historia && tlacZnova.Od == minulyMesiac.Od && tlacZnova.ObdobieUlozenia == ObdobieZostavy.MinulyMesiac,
                "uložená zostava s obdobím „minulý mesiac“");
            Over(tlacZnova.UlozeneZostavy.First(z => z.Nazov.StartsWith("Zmeny")).Popis.Contains("minulý mesiac"), "popis uloženej zostavy");
            tlacZnova.VymazatUlozenuCommand.Execute(tlacZnova.UlozeneZostavy.First(z => z.Nazov.StartsWith("Zmeny")));
            Over(UlozeneZostavy.Nacitaj().Count == 1 && tlacZnova.UlozeneZostavy.Count == 1, "vymazanie uloženej zostavy");
            model.Tlac.NazovBezDialogu = null;
            model.Tlac.Obnov();
            model.Tlac.MiestnostId = 0;
            model.Tlac.Podpis = true;
            model.Tlac.Vybrana = ZostavyService.Popis(DruhZostavy.PoMiestnostiach);
            model.Tlac.VybranyStlpec = model.Tlac.Stlpce[1];
            model.Tlac.HoreCommand.Execute(null);
            var nahlad = model.Tlac.VytvorNahlad();
            Over(nahlad is { PocetStran: > 1 }, $"náhľad zostavy po miestnostiach ({nahlad?.PocetStran} strán)");
            nahlad!.Show();
            Pumpuj();
            Snimka(nahlad, "nahlad_po_miestnostiach");
            nahlad.CestaBezDialogu = Path.Combine(exportPriecinok, "z_nahladu.pdf");
            Over(nahlad.UlozitPdf() is { } zNahladu && File.Exists(zNahladu), "uloženie PDF z náhľadu");
            nahlad.Close();
            Pumpuj();
            model.Tlac.Vybrana = ZostavyService.Popis(DruhZostavy.Vyfiltrovane);
            Over(model.Tlac.PopisVyfiltrovanych.Contains("ucebna"), "obrazovka tlače: vyfiltrované položky podľa Evidencie");
            model.Tlac.CestaBezDialogu = Path.Combine(exportPriecinok, "z_obrazovky.pdf");
            model.Tlac.UlozitPdfCommand.Execute(null);
            Over(File.Exists(model.Tlac.CestaBezDialogu), "uloženie PDF z obrazovky tlače");
            model.Tlac.VytlacitCommand.Execute(null);
            var nahladEvidencie = model.Evidencia.VytvorNahladTlace();
            nahladEvidencie.Show();
            Pumpuj();
            Over(nahladEvidencie.PocetStran >= 1, "tlač z Evidencie");
            Snimka(nahladEvidencie, "nahlad_evidencie");
            nahladEvidencie.Close();
            Pumpuj();
            Krok("okno: náhľad tlače");

            // kalendár: týždeň, zoznam, udalosť, typy, nastavenia, prehľad dňa, upozornenie
            okno.OznacSekciu("kalendar");
            Pumpuj();
            model.Kalendar.Rezim = RezimKalendara.Tyzden;
            Pumpuj();
            Over(model.Kalendar.DniTyzdna.Count == 7 && model.Kalendar.DniTyzdna.Any(d => d.Dnes), "kalendár: týždeň");
            Snimka(okno, "okno_kalendar_tyzden");
            model.Kalendar.Rezim = RezimKalendara.Zoznam;
            Pumpuj();
            Over(model.Kalendar.DniZoznamu.Count > 0, "kalendár: zoznam");
            Snimka(okno, "okno_kalendar_zoznam");
            model.Kalendar.Rezim = RezimKalendara.Mesiac;
            model.Kalendar.NasledujuceCommand.Execute(null);
            model.Kalendar.PredchadzajuceCommand.Execute(null);
            model.Kalendar.DnesCommand.Execute(null);
            Pumpuj();
            Over(model.Kalendar.Tyzdne.Count == 6 && model.Kalendar.Tyzdne.SelectMany(t => t.Dni).Any(d => d.Dnes && d.Udalosti.Count > 0),
                "kalendár: mesiac s dnešnými udalosťami");
            model.Kalendar.CestaIcsBezDialogu = Path.Combine(exportPriecinok, "kalendar.ics");
            model.Kalendar.ExportIcsCommand.Execute(null);
            Over(File.Exists(model.Kalendar.CestaIcsBezDialogu), "kalendár: export .ics z obrazovky");

            var novaUdalost = new UdalostEditViewModel(null, DateTime.Today.AddDays(3), riadky[0].Id)
            {
                Nazov = "Test zo samotestu",
                CasOd = "10.30",
                CasDo = "11:15",
                Pripomienka = PripomienkaUdalosti.DenVopred,
            };
            var oknoUdalosti = new UdalostWindow(novaUdalost);
            oknoUdalosti.Show();
            Pumpuj();
            Snimka(oknoUdalosti, "okno_udalost");
            Over(novaUdalost.PolozkaText == riadky[0].Nazov, "udalosť: naviazanie na položku podľa čísla");
            novaUdalost.UlozitCommand.Execute(null);
            Pumpuj();
            var ulozena = kalendarSluzba.Nacitaj(novaUdalost.UlozeneId);
            Over(ulozena is { PolozkaId: not null } && ulozena.Zaciatok == DateTime.Today.AddDays(3).AddHours(10.5) && ulozena.Koniec!.Value.Minute == 15,
                "udalosť: uloženie s časom a položkou");
            var polozkaSUdalostou = new PolozkaEditViewModel(riadky[0].Id, null);
            Over(polozkaSUdalostou.Udalosti.Any(v => v.UdalostId == novaUdalost.UlozeneId), "karta položky: záložka Udalosti");
            kalendarSluzba.Vymaz(novaUdalost.UlozeneId);

            OtvorAZatvor(new TypyUdalostiWindow(), "typy udalostí");
            OtvorAZatvor(new NastaveniaKalendaraWindow(), "nastavenia kalendára");
            var prehladOkno = new PrehladDnaWindow();
            prehladOkno.Show();
            Pumpuj();
            Over(!prehladOkno.Prehlad.JePrazdny, "prehľad dňa pri spustení");
            Snimka(prehladOkno, "okno_prehlad_dna");
            prehladOkno.Close();
            Pumpuj();
            OtvorAZatvor(new UpozornenieWindow(prehladK.Dnes[0]), "upozornenie na udalosť");
            var poznamkaKDnu = new PoznamkaEditViewModel(null, DateTime.Today.AddDays(1)) { Nazov = "Poznámka k zajtrajšku", Text = "z okna" };
            var oknoPoznamky = new PoznamkaWindow(poznamkaKDnu);
            oknoPoznamky.Show();
            Pumpuj();
            Snimka(oknoPoznamky, "okno_poznamka");
            poznamkaKDnu.UlozitCommand.Execute(null);
            Pumpuj();
            model.Kalendar.Rezim = RezimKalendara.Tyzden;
            model.Kalendar.AktualnyDatum = DateTime.Today.AddDays(1);
            Pumpuj();
            Over(model.Kalendar.DniTyzdna.Any(d => d.Poznamky.Any(p => p.Nazov == "Poznámka k zajtrajšku")), "kalendár: poznámka k dňu v týždni");
            Snimka(okno, "okno_kalendar_tyzden_poznamka");
            model.Kalendar.Rezim = RezimKalendara.Mesiac;
            model.Kalendar.AktualnyDatum = DateTime.Today;
            Pumpuj();
            var skolenieTeraz = kalendarSluzba.Vyskyty(DateTime.Today, DateTime.Today.AddDays(30), automaticke: false).First(v => v.Nazov.StartsWith("Školenie"));
            var udalostSPoznamkou = new UdalostEditViewModel(skolenieTeraz.UdalostId, null, null);
            Over(udalostSPoznamkou.Poznamky.Count == 1, "okno udalosti: prepojené poznámky");
            OtvorAZatvor(new UdalostWindow(udalostSPoznamkou), "udalosť s poznámkou");
            var polozkaSPoznamkou = new PolozkaEditViewModel(riadky[2].Id, null);
            Over(polozkaSPoznamkou.Poznamky.Any(p => p.Nazov == "Poznámka zo samotestu"), "karta položky: záložka Poznámky a úlohy");
            OtvorAZatvor(new PolozkaEditWindow(polozkaSPoznamkou), "karta položky s poznámkami");
            Krok("okná kalendára v poriadku");

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
            Snimka(oknoExportu, "okno_export");
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
            sprievodca.StiahnutVzorCommand.Execute(VzoryService.Zoznam[1]);
            Pumpuj();
            Over(File.Exists(sprievodca.StiahnutyVzor), "sprievodca: stiahnutie vzoru");
            sprievodca.Cesta = Path.Combine(VzoryService.Priecinok, VzoryService.Csv);
            Snimka(oknoImportu, "import_1");
            sprievodca.DalejCommand.Execute(null);
            Pumpuj();
            Snimka(oknoImportu, "import_2");
            Over(sprievodca.Krok == 2 && sprievodca.Priradenia.Count >= 15, $"sprievodca: priradenie stĺpcov (krok {sprievodca.Krok})");
            sprievodca.DalejCommand.Execute(null);
            Pumpuj();
            Over(sprievodca.Krok == 3 && sprievodca.Riadky.Count == 5, $"sprievodca: kontrola (krok {sprievodca.Krok})");
            sprievodca.Riadky[0].Nazov = "";
            sprievodca.Riadky[2].Cena = "lacné";
            Pumpuj();
            Snimka(oknoImportu, "import_3");
            Over(sprievodca.Riadky[0].Vysledok == StavRiadku.Chyba, "sprievodca: zmena v tabuľke sa hneď skontroluje");
            sprievodca.Riadky[2].Cena = "459,00";
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
            Snimka(oknoImportu, "import_4");
            sprievodca.ImportovatCommand.Execute(null);
            Pumpuj();
            Over(sprievodca.Krok == 5 && sprievodca.Vysledok?.Pridane == 5 && sluzba.NacitajRiadky().Count == predSprievodcom + 5, "sprievodca: import");
            Over(sprievodca.Protokol.Contains("Notebook zo sprievodcu"), "sprievodca: protokol");
            Snimka(oknoImportu, "import_5");
            sprievodca.VytlacitProtokolCommand.Execute(null);
            sprievodca.VratitImportCommand.Execute(null);
            Pumpuj();
            Over(sprievodca.ImportVrateny && sluzba.NacitajRiadky().Count == predSprievodcom, "sprievodca: vrátenie importu");
            oknoImportu.Close();
            Pumpuj();
            Krok("okno: sprievodca importom");

            // ------------------------------------------------------------ sprievodca: import číselníkov
            int PocetMiestnosti()
            {
                using var dbc = Databaza.Otvor();
                return dbc.Miestnosti.Count();
            }

            string existujucaMiestnost;
            using (var dbc = Databaza.Otvor())
            {
                existujucaMiestnost = dbc.Miestnosti.Select(m => m.Nazov).First();
            }

            var vzorCiselnikov = Path.Combine(VzoryService.Priecinok, VzoryService.Ciselniky);
            var harokOsob = CitacSuborov.Nacitaj(vzorCiselnikov, TypCiselnika.Osoby);
            var harokKategorii = CitacSuborov.Nacitaj(vzorCiselnikov, TypCiselnika.Kategorie);
            Over(harokOsob.Riadky.Count == 2 && PolaCiselnika.AutomatickePriradenie(TypCiselnika.Osoby, harokOsob.Hlavicky).All(k => k is not null)
                 && harokKategorii.Riadky.Count == 3, "vzor číselníkov: výber hárku podľa číselníka");

            var csvMiestnosti = Path.Combine(Databaza.Priecinok, "miestnosti_import.csv");
            File.WriteAllText(csvMiestnosti,
                "Názov;Budova;Poznámka;Aktívna\r\nTestovacia učebňa A;C;prízemie;áno\r\nTestovacia učebňa A;C;;áno\r\n;C;bez názvu;áno\r\n" +
                "Testovací kabinet B;D;;nie\r\n" + existujucaMiestnost + ";;;\r\n", new UTF8Encoding(true));
            var predMiestnostami = PocetMiestnosti();
            var sprievodcaC = new ImportViewModel { Cesta = csvMiestnosti, DruhCiselnik = true, TypCiselnika = TypCiselnika.Miestnosti };
            var oknoC = new ImportWindow(sprievodcaC);
            oknoC.Show();
            Pumpuj();
            sprievodcaC.DalejCommand.Execute(null);
            Pumpuj();
            Over(sprievodcaC.Krok == 2 && sprievodcaC.Priradenia.Count == 4 && sprievodcaC.Priradenia.All(pr => pr.Kluc.Length > 0),
                $"číselník: rozpoznanie stĺpcov miestností (krok {sprievodcaC.Krok})");
            Snimka(oknoC, "import_ciselnik_2");
            sprievodcaC.DalejCommand.Execute(null);
            Pumpuj();
            var stavyC = sprievodcaC.RiadkyCiselnika.Select(r => r.Vysledok).ToList();
            Over(sprievodcaC.Krok == 3 && stavyC.Count == 5 && stavyC[0] == StavRiadku.Ok && stavyC[1] == StavRiadku.Duplicita
                 && stavyC[2] == StavRiadku.Chyba && stavyC[3] == StavRiadku.Ok && stavyC[4] == StavRiadku.Duplicita,
                $"číselník: kontrola miestností ({string.Join(", ", stavyC)})");
            Snimka(oknoC, "import_ciselnik_3");
            sprievodcaC.RiadkyCiselnika[2].Pole1 = "Opravená miestnosť";
            Pumpuj();
            Over(sprievodcaC.RiadkyCiselnika[2].MozeImportovat && sprievodcaC.RiadkyCiselnika[2].Importovat, "číselník: oprava riadku v tabuľke");
            sprievodcaC.DalejCommand.Execute(null);
            Pumpuj();
            Over(sprievodcaC.Krok == 4 && sprievodcaC.NahladPridat.Contains("3 záznamy"), $"číselník: náhľad ({sprievodcaC.NahladPridat})");
            sprievodcaC.ImportovatCommand.Execute(null);
            Pumpuj();
            bool kabinetNeaktivny;
            using (var dbc = Databaza.Otvor())
            {
                kabinetNeaktivny = dbc.Miestnosti.Any(m => m.Nazov == "Testovací kabinet B" && !m.Aktivna && m.Budova == "D");
            }

            Over(sprievodcaC.Krok == 5 && sprievodcaC.Vysledok?.Pridane == 3 && PocetMiestnosti() == predMiestnostami + 3 && kabinetNeaktivny,
                "číselník: import miestností");
            Over(sprievodcaC.Protokol.Contains("Opravená miestnosť"), "číselník: protokol");
            Snimka(oknoC, "import_ciselnik_5");
            sprievodcaC.VratitImportCommand.Execute(null);
            Pumpuj();
            Over(PocetMiestnosti() == predMiestnostami, "číselník: vrátenie importu");
            oknoC.Close();
            Pumpuj();

            var csvOsoby = Path.Combine(Databaza.Priecinok, "osoby_import.csv");
            File.WriteAllText(csvOsoby, "Osoba;Funkcia\r\nPeter Testovací;technik\r\nAnna Mária Skúšobná;učiteľka\r\n", new UTF8Encoding(true));
            var sprievodcaO = new ImportViewModel { Cesta = csvOsoby, DruhCiselnik = true, TypCiselnika = TypCiselnika.Osoby };
            sprievodcaO.DalejCommand.Execute(null);
            sprievodcaO.DalejCommand.Execute(null);
            sprievodcaO.DalejCommand.Execute(null);
            sprievodcaO.ImportovatCommand.Execute(null);
            bool osobaRozdelena;
            using (var dbc = Databaza.Otvor())
            {
                osobaRozdelena = dbc.Osoby.Any(o => o.Meno == "Anna Mária" && o.Priezvisko == "Skúšobná" && o.Funkcia == "učiteľka");
            }

            Over(sprievodcaO.Vysledok?.Pridane == 2 && osobaRozdelena, "číselník: osoby s menom a priezviskom v jednom stĺpci");
            var sprievodcaO2 = new ImportViewModel { Cesta = csvOsoby, DruhCiselnik = true, TypCiselnika = TypCiselnika.Osoby };
            sprievodcaO2.DalejCommand.Execute(null);
            sprievodcaO2.DalejCommand.Execute(null);
            Over(sprievodcaO2.RiadkyCiselnika.All(r => r.Vysledok == StavRiadku.Duplicita), "číselník: osoby už v evidencii sa nepridajú znova");
            sprievodcaO.VratitImportCommand.Execute(null);

            var sprievodcaK = new ImportViewModel { Cesta = Path.Combine(VzoryService.Priecinok, VzoryService.Json), DruhCiselnik = true, TypCiselnika = TypCiselnika.Kategorie };
            sprievodcaK.DalejCommand.Execute(null);
            sprievodcaK.DalejCommand.Execute(null);
            Over(sprievodcaK.Krok == 3 && sprievodcaK.RiadkyCiselnika.Count > 0, "číselník: kategórie zo súboru EviTA (JSON)");
            Krok("sprievodca: import číselníkov");
            model.NovaVerzia = vydanie;
            Pumpuj();
            Over(model.JeNovaVerzia, "pruh s novou verziou");
            okno.Close();
            Pumpuj();

            // ------------------------------------------------------------ jazyky: celé rozhranie v každom jazyku
            foreach (var jazyk in Preklad.Jazyky.Where(j => j.Kod != "sk"))
            {
                Preklad.Nastav(jazyk.Kod);
                Databaza.PrelozPociatocneNazvy();
                servis.NacitatUkazkoveData();
                Preklad.Chybajuce.Clear();
                Preklad.ZbieratChybajuce = true;
                var neprelozene = new SortedSet<string>(StringComparer.Ordinal);
                void Zbieraj(Window w)
                {
                    foreach (var text in PrekladacXaml.NeprelozeneTexty(w))
                    {
                        neprelozene.Add(text.Replace("\n", " ⏎ "));
                    }
                }

                var oknoJazyka = new MainWindow();
                aplikacia.MainWindow = oknoJazyka;
                oknoJazyka.Show();
                Pumpuj();
                var modelJazyka = (MainViewModel)oknoJazyka.DataContext;
                foreach (var sekcia in new[] { "dashboard", "evidencia", "kalendar", "ulohy", "poznamky", "tlac", "import", "kategorie", "miestnosti", "osoby", "sviatky", "servis", "nastavenia" })
                {
                    oknoJazyka.OznacSekciu(sekcia);
                    Pumpuj();
                    if (sekcia == "dashboard")
                    {
                        var cakanieJazyka = System.Diagnostics.Stopwatch.StartNew();
                        while (!modelJazyka.Dashboard.PosledneNacitanieZoSiete.IsCompleted && cakanieJazyka.Elapsed < TimeSpan.FromSeconds(20))
                        {
                            Pumpuj();
                            Thread.Sleep(50);
                        }

                        Pumpuj();
                    }

                    PrejdiZalozky(oknoJazyka);
                    Zbieraj(oknoJazyka);
                    if (sekcia is "dashboard" or "evidencia" or "kalendar" or "poznamky" or "nastavenia" or "servis")
                    {
                        Snimka(oknoJazyka, $"jazyk_{jazyk.Kod}_{sekcia}");
                    }
                }

                modelJazyka.Kalendar.Rezim = RezimKalendara.Tyzden;
                Pumpuj();
                Zbieraj(oknoJazyka);
                modelJazyka.Kalendar.Rezim = RezimKalendara.Zoznam;
                Pumpuj();
                Zbieraj(oknoJazyka);

                var riadkyJazyka = sluzba.NacitajRiadky();
                var udalostJazyka = kalendarSluzba.Vyskyty(DateTime.Today, DateTime.Today.AddDays(30), automaticke: false).First(v => v.UdalostId is not null);
                var oknaJazyka = new (Func<Window> Okno, string Nazov)[]
                {
                    (() => new PolozkaEditWindow(new PolozkaEditViewModel(riadkyJazyka[0].Id, null)), "polozka"),
                    (() => new UdalostWindow(new UdalostEditViewModel(udalostJazyka.UdalostId, null, null)), "udalost"),
                    (() => new PoznamkaWindow(new PoznamkaEditViewModel(null, DateTime.Today)), "poznamka"),
                    (() => new ExportWindow(new ExportViewModel(riadkyJazyka.Select(r => r.Id).ToList(), null, "")), "export"),
                    (() => new ImportWindow(new ImportViewModel()), "import"),
                    (() => new NastaveniaKalendaraWindow(), "nastavenia_kalendara"),
                    (() => new TypyUdalostiWindow(), "typy_udalosti"),
                    (() => new PrehladDnaWindow(), "prehlad_dna"),
                    (() => new VymazanieWindow(), "vymazanie"),
                    (() => new PrveSpustenieWindow(), "prve_spustenie"),
                    (() => new HesloWindow(true, null), "heslo"),
                    (() => new AktualizaciaWindow(vydanie), "aktualizacia"),
                    (() => new HromadnaZmenaWindow(new HromadnaZmenaViewModel(new List<int> { riadkyJazyka[0].Id })), "hromadna_zmena"),
                    (() => new VstupWindow(Preklad.T("Nová kategória"), Preklad.T("Názov novej kategórie:"), "", false), "vstup"),
                    (() => new UpozornenieWindow(udalostJazyka), "upozornenie"),
                    (() => modelJazyka.Evidencia.VytvorNahladTlace(), "nahlad"),
                };
                foreach (var (vytvor, nazov) in oknaJazyka)
                {
                    var w = vytvor();
                    w.Show();
                    Pumpuj();
                    PrejdiZalozky(w);
                    Zbieraj(w);
                    if (nazov is "polozka" or "udalost" or "export" or "prehlad_dna" or "prve_spustenie")
                    {
                        Snimka(w, $"jazyk_{jazyk.Kod}_okno_{nazov}");
                    }

                    w.Close();
                    Pumpuj();
                }

                // zostavy a exporty v jazyku
                Krok($"jazyk {jazyk.Kod}: písmo PDF {PdfZostava.Pismo} ({PismaPdf.NajdiSubory()?.Normalne}) {string.Join("; ", PdfZostava.ChybyPisma)}");
                zostavy.UlozPdf(new MoznostiZostavy
                {
                    Druh = DruhZostavy.PoMiestnostiach, Podpis = true,
                    Stlpce = new[] { StlpcePrenosu.Cislo, StlpcePrenosu.Nazov, StlpcePrenosu.Kategoria, StlpcePrenosu.Pridelene, StlpcePrenosu.Stav, StlpcePrenosu.Cena },
                }, Path.Combine(exportPriecinok, $"jazyk_{jazyk.Kod}_zostava.pdf"));
                zostavy.UlozPdf(new MoznostiZostavy { Druh = DruhZostavy.Kalendar, NaSirku = true }, Path.Combine(exportPriecinok, $"jazyk_{jazyk.Kod}_kalendar.pdf"));
                export.Exportuj(new MoznostiExportu(FormatExportu.Excel, Path.Combine(exportPriecinok, $"jazyk_{jazyk.Kod}.xlsx"), null,
                    StlpcePrenosu.Vsetky.Where(st => st.PredvolenyExport).Select(st => st.Kluc).ToList()));
                var zdrojJazyka = import.Nacitaj(Path.Combine(exportPriecinok, $"jazyk_{jazyk.Kod}.xlsx"));
                Over(ImportService.AutomatickePriradenie(zdrojJazyka.Hlavicky).Count(p => p is not null) >= 15,
                    $"{jazyk.Kod}: import rozpozná stĺpce exportu v tomto jazyku");

                oknoJazyka.Close();
                Pumpuj();
                Preklad.ZbieratChybajuce = false;
                var chybajuce = Preklad.Chybajuce.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => "[T] " + k.Replace("\n", " ⏎ ")).ToList();
                File.WriteAllLines(Path.Combine(exportPriecinok, $"preklad_{jazyk.Kod}.txt"), neprelozene.Concat(chybajuce), Encoding.UTF8);
                Krok($"jazyk {jazyk.Kod}: na obrazovkách {neprelozene.Count} textov so slovenskými znakmi, v kóde {chybajuce.Count} textov bez prekladu");
            }

            Preklad.Nastav("sk");
            Databaza.PrelozPociatocneNazvy();
            Over(Databaza.Otvor().Kategorie.Any(k => k.Nazov == "tlačiareň") && new KalendarService().Typy().Any(t => t.Nazov == "porada"), "počiatočné názvy sa po zmene jazyka vrátili do slovenčiny");
            servis.NacitatUkazkoveData();

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

    /// <summary>Malé logo na skúšku hlavičky zostáv.</summary>
    private static byte[] UkazkoveLogo()
    {
        var kresba = new System.Windows.Media.DrawingVisual();
        using (var kontext = kresba.RenderOpen())
        {
            kontext.DrawRoundedRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 103, 192)), null,
                new Rect(0, 0, 240, 80), 14, 14);
            var text = new System.Windows.Media.FormattedText("EviTA", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new System.Windows.Media.Typeface("Segoe UI"), 42, System.Windows.Media.Brushes.White, 1.0);
            kontext.DrawText(text, new Point(62, 12));
        }

        var obrazok = new System.Windows.Media.Imaging.RenderTargetBitmap(240, 80, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        obrazok.Render(kresba);
        var kodovac = new System.Windows.Media.Imaging.PngBitmapEncoder();
        kodovac.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(obrazok));
        using var prud = new MemoryStream();
        kodovac.Save(prud);
        return prud.ToArray();
    }

    /// <summary>Uloží obrázok okna do priečinka Export (na kontrolu vzhľadu z vývojových zostavení).</summary>
    private static void Snimka(Window okno, string nazov)
    {
        try
        {
            if (okno.Content is not FrameworkElement obsah || obsah.ActualWidth < 1 || obsah.ActualHeight < 1)
            {
                return;
            }

            var sirka = (int)Math.Ceiling(obsah.ActualWidth);
            var vyska = (int)Math.Ceiling(obsah.ActualHeight);
            var kresba = new System.Windows.Media.DrawingVisual();
            using (var kontext = kresba.RenderOpen())
            {
                var oblast = new Rect(0, 0, sirka, vyska);
                kontext.DrawRectangle(okno.Background ?? System.Windows.Media.Brushes.White, null, oblast);
                kontext.DrawRectangle(new System.Windows.Media.VisualBrush(obsah), null, oblast);
            }

            var obrazok = new System.Windows.Media.Imaging.RenderTargetBitmap(sirka, vyska, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            obrazok.Render(kresba);
            var kodovac = new System.Windows.Media.Imaging.PngBitmapEncoder();
            kodovac.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(obrazok));
            var priecinok = Path.Combine(Databaza.Priecinok, "Export");
            Directory.CreateDirectory(priecinok);
            using var subor = File.Create(Path.Combine(priecinok, nazov + ".png"));
            kodovac.Save(subor);
        }
        catch (Exception ex)
        {
            Krok($"snímka {nazov}: {ex.Message}");
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
