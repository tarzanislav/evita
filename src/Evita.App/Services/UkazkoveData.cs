using Evita.Data;
using Evita.Models;

namespace Evita.Services;

/// <summary>
/// Vytvorí ukážkovú evidenciu: 200 položiek, 30 osôb, 25 miestností a históriu zmien.
/// Dáta sú vymyslené, ale realistické, aby sa dali vyskúšať všetky funkcie programu.
/// Generuje sa vždy rovnako (pevné „semienko“ náhodných čísel).
/// </summary>
public static class UkazkoveData
{
    private const string PouzivatelUkazky = "ukážka";

    private enum Kde
    {
        Osoba,
        Ucebna,
        PocitacovaUcebna,
        Kancelaria,
        Serverovna,
        Sklad,
        Kdekolvek,
    }

    private sealed record Sablona(
        string Kategoria, string Vyrobca, string Model, string Nazov,
        int CenaOd, int CenaDo, int ZarukaMesiacov, bool Revizia);

    private sealed record MiestnostVzor(string Nazov, string Budova, string Typ);

    private sealed record OsobaVzor(string Meno, string Priezvisko, string Funkcia, string Domov);

    private static readonly (string Kategoria, int Pocet, Kde Kde)[] Rozdelenie =
    {
        ("notebook", 40, Kde.Osoba),
        ("PC", 18, Kde.PocitacovaUcebna),
        ("PC", 12, Kde.Kancelaria),
        ("tablet", 15, Kde.Sklad),
        ("tablet", 15, Kde.Osoba),
        ("projektor", 15, Kde.Ucebna),
        ("príslušenstvo", 25, Kde.Kdekolvek),
        ("tlačiareň", 8, Kde.Kancelaria),
        ("multifunkcia", 8, Kde.Kancelaria),
        ("interaktívna tabuľa", 10, Kde.Ucebna),
        ("multiboard", 4, Kde.Ucebna),
        ("All-in-One", 8, Kde.Kancelaria),
        ("slúchadlá", 10, Kde.Osoba),
        ("switch", 2, Kde.Serverovna),
        ("switch", 2, Kde.PocitacovaUcebna),
        ("router", 3, Kde.Serverovna),
        ("skener", 3, Kde.Kancelaria),
        ("kopírka", 2, Kde.Kancelaria),
    };

    private static readonly Sablona[] Sablony =
    {
        new("All-in-One", "HP", "ProOne 440 G9", "Počítač All-in-One HP ProOne 440 G9", 750, 950, 36, true),
        new("All-in-One", "Lenovo", "IdeaCentre AIO 3 24", "Počítač All-in-One Lenovo IdeaCentre AIO 3", 650, 850, 24, true),
        new("interaktívna tabuľa", "SMART", "Board MX275-V4", "Interaktívny panel SMART Board MX275", 2800, 3600, 60, true),
        new("interaktívna tabuľa", "Promethean", "ActivPanel 9 75\"", "Interaktívny panel Promethean ActivPanel 9", 2600, 3400, 60, true),
        new("kopírka", "Konica Minolta", "bizhub C258", "Farebná kopírka Konica Minolta bizhub C258", 4200, 5200, 24, true),
        new("kopírka", "Canon", "imageRUNNER 2425", "Kopírka Canon imageRUNNER 2425", 1500, 1900, 24, true),
        new("multiboard", "Newline", "Q Series 86\"", "Multiboard Newline 86\" na pojazdnom stojane", 3200, 4200, 36, true),
        new("multifunkcia", "HP", "LaserJet Pro MFP 4102fdw", "Multifunkčná tlačiareň HP LaserJet Pro MFP 4102fdw", 380, 480, 24, true),
        new("multifunkcia", "Brother", "MFC-L2750DW", "Multifunkčná tlačiareň Brother MFC-L2750DW", 280, 360, 24, true),
        new("multifunkcia", "Epson", "EcoTank L6270", "Multifunkčná tlačiareň Epson EcoTank L6270", 300, 380, 24, true),
        new("notebook", "Lenovo", "ThinkPad E14 Gen 5", "Notebook Lenovo ThinkPad E14 Gen 5", 750, 950, 36, true),
        new("notebook", "HP", "ProBook 450 G10", "Notebook HP ProBook 450 G10", 700, 900, 36, true),
        new("notebook", "Dell", "Latitude 3540", "Notebook Dell Latitude 3540", 650, 850, 36, true),
        new("notebook", "Acer", "TravelMate P2", "Notebook Acer TravelMate P2", 550, 700, 24, true),
        new("PC", "Dell", "OptiPlex 7010 SFF", "Počítač Dell OptiPlex 7010", 650, 850, 36, true),
        new("PC", "HP", "ProDesk 400 G9 SFF", "Počítač HP ProDesk 400 G9", 600, 780, 36, true),
        new("PC", "Lenovo", "ThinkCentre M70s Gen 4", "Počítač Lenovo ThinkCentre M70s", 620, 800, 36, true),
        new("príslušenstvo", "Dell", "P2422H", "Monitor Dell P2422H 24\"", 170, 220, 36, true),
        new("príslušenstvo", "Logitech", "MK270", "Bezdrôtová klávesnica a myš Logitech MK270", 25, 35, 24, false),
        new("príslušenstvo", "Logitech", "C920", "Webkamera Logitech C920", 60, 80, 24, false),
        new("príslušenstvo", "Dell", "WD19S", "Dokovacia stanica Dell WD19S", 180, 230, 36, true),
        new("príslušenstvo", "Western Digital", "Elements 2 TB", "Externý disk WD Elements 2 TB", 70, 90, 24, false),
        new("príslušenstvo", "APC", "Back-UPS 700", "Záložný zdroj APC Back-UPS 700", 110, 140, 24, true),
        new("projektor", "Epson", "EB-W51", "Projektor Epson EB-W51", 420, 520, 36, true),
        new("projektor", "BenQ", "MW560", "Projektor BenQ MW560", 450, 560, 36, true),
        new("projektor", "Optoma", "W400LVe", "Projektor Optoma W400LVe", 430, 530, 24, true),
        new("router", "MikroTik", "hAP ax2", "Router MikroTik hAP ax2", 90, 120, 24, true),
        new("router", "TP-Link", "Archer AX55", "Router TP-Link Archer AX55", 80, 110, 36, true),
        new("skener", "Canon", "CanoScan LiDE 300", "Skener Canon CanoScan LiDE 300", 60, 80, 24, true),
        new("skener", "Epson", "Perfection V39 II", "Skener Epson Perfection V39 II", 85, 110, 24, true),
        new("slúchadlá", "Jabra", "Evolve2 30", "Slúchadlá s mikrofónom Jabra Evolve2 30", 55, 75, 24, false),
        new("slúchadlá", "Logitech", "H390", "Slúchadlá s mikrofónom Logitech H390", 25, 35, 24, false),
        new("switch", "Cisco", "CBS250-24T-4G", "Switch Cisco CBS250 24-portový", 280, 360, 36, true),
        new("switch", "TP-Link", "TL-SG1016D", "Switch TP-Link TL-SG1016D 16-portový", 60, 80, 36, true),
        new("tablet", "Apple", "iPad 10. generácie", "Tablet Apple iPad 10. generácie", 380, 460, 24, true),
        new("tablet", "Samsung", "Galaxy Tab A9+", "Tablet Samsung Galaxy Tab A9+", 220, 280, 24, true),
        new("tablet", "Lenovo", "Tab M10", "Tablet Lenovo Tab M10", 180, 230, 24, true),
        new("tlačiareň", "HP", "LaserJet Pro 4002dn", "Tlačiareň HP LaserJet Pro 4002dn", 230, 290, 24, true),
        new("tlačiareň", "Brother", "HL-L2350DW", "Tlačiareň Brother HL-L2350DW", 110, 150, 24, true),
        new("tlačiareň", "Canon", "i-SENSYS LBP233dw", "Tlačiareň Canon i-SENSYS LBP233dw", 190, 240, 24, true),
    };

    private static readonly MiestnostVzor[] MiestnostiVzor =
    {
        new("Riaditeľňa", "Hlavná budova", "kancelaria"),
        new("Sekretariát", "Hlavná budova", "kancelaria"),
        new("Ekonomické oddelenie", "Hlavná budova", "kancelaria"),
        new("Zasadačka", "Hlavná budova", "ucebna"),
        new("Zborovňa", "Hlavná budova", "kancelaria"),
        new("Kabinet IT", "Hlavná budova", "kabinet"),
        new("Kabinet matematiky", "Hlavná budova", "kabinet"),
        new("Kabinet jazykov", "Hlavná budova", "kabinet"),
        new("Kabinet fyziky a chémie", "Pavilón B", "kabinet"),
        new("Kabinet prírodovedy", "Pavilón B", "kabinet"),
        new("Učebňa 101", "Hlavná budova", "ucebna"),
        new("Učebňa 102", "Hlavná budova", "ucebna"),
        new("Učebňa 103", "Hlavná budova", "ucebna"),
        new("Učebňa 104", "Hlavná budova", "ucebna"),
        new("Učebňa 201", "Hlavná budova", "ucebna"),
        new("Učebňa 202", "Hlavná budova", "ucebna"),
        new("Jazyková učebňa", "Hlavná budova", "ucebna"),
        new("Počítačová učebňa 1", "Pavilón B", "pc"),
        new("Počítačová učebňa 2", "Pavilón B", "pc"),
        new("Fyzikálne laboratórium", "Pavilón B", "ucebna"),
        new("Knižnica", "Hlavná budova", "ucebna"),
        new("Telocvičňa", "Telocvičňa", "ucebna"),
        new("Školský klub", "Pavilón B", "ucebna"),
        new("Sklad IT", "Hlavná budova", "sklad"),
        new("Serverovňa", "Hlavná budova", "server"),
    };

    private static readonly OsobaVzor[] OsobyVzor =
    {
        new("Peter", "Horváth", "riaditeľ", "Riaditeľňa"),
        new("Zuzana", "Hrušková", "zástupkyňa riaditeľa", "Riaditeľňa"),
        new("Eva", "Mrázová", "ekonómka", "Ekonomické oddelenie"),
        new("Lucia", "Benková", "sekretárka", "Sekretariát"),
        new("Martin", "Kováč", "správca IT", "Kabinet IT"),
        new("Jozef", "Nagy", "školník", "Sklad IT"),
        new("Ján", "Novák", "učiteľ", "Kabinet matematiky"),
        new("Tomáš", "Varga", "učiteľ", "Kabinet fyziky a chémie"),
        new("Michal", "Tóth", "učiteľ", "Kabinet IT"),
        new("Marek", "Baláž", "učiteľ", "Kabinet jazykov"),
        new("Lukáš", "Molnár", "učiteľ", "Kabinet prírodovedy"),
        new("Juraj", "Szabó", "učiteľ", "Kabinet matematiky"),
        new("Andrej", "Lukáč", "učiteľ", "Kabinet fyziky a chémie"),
        new("Milan", "Kollár", "učiteľ", "Kabinet jazykov"),
        new("Pavol", "Šimko", "učiteľ", "Kabinet IT"),
        new("Róbert", "Gajdoš", "učiteľ", "Kabinet prírodovedy"),
        new("Jana", "Kováčová", "učiteľka", "Kabinet jazykov"),
        new("Katarína", "Polakovičová", "učiteľka", "Kabinet matematiky"),
        new("Martina", "Lenková", "učiteľka", "Kabinet prírodovedy"),
        new("Monika", "Fabianová", "učiteľka", "Kabinet jazykov"),
        new("Andrea", "Sekerová", "učiteľka", "Kabinet matematiky"),
        new("Veronika", "Krajčírová", "učiteľka", "Kabinet fyziky a chémie"),
        new("Ivana", "Hudáková", "učiteľka", "Kabinet jazykov"),
        new("Silvia", "Kubíková", "učiteľka", "Kabinet prírodovedy"),
        new("Dana", "Švecová", "učiteľka", "Zborovňa"),
        new("Lenka", "Oravcová", "učiteľka", "Zborovňa"),
        new("Petra", "Kraľová", "učiteľka", "Kabinet matematiky"),
        new("Alena", "Vašková", "vychovávateľka", "Školský klub"),
        new("Mária", "Gregorová", "vychovávateľka", "Školský klub"),
        new("Vladimír", "Hudec", "asistent učiteľa", "Zborovňa"),
    };

    private static readonly string[] Poznamky =
    {
        "Nabíjačka je uložená v kabinete.",
        "V roku 2025 vymenená batéria.",
        "Zapožičané na projektový týždeň, vrátené.",
        "Lampa vymenená, ďalšia výmena po približne 4 000 hodinách.",
        "Heslo k BIOS-u má správca IT.",
        "Súčasť sady pre jazykovú učebňu.",
        "Reklamácia vybavená výmenou kusa.",
        "Pomalé, navrhnuté na vyradenie v budúcom roku.",
        "Pripojené k sieti cez kábel, pevná IP adresa.",
        "Pri prenášaní používať ochranný obal.",
        "Toner objednávať u dodávateľa podľa zmluvy.",
        "Diaľkový ovládač je v zásuvke katedry.",
    };

    private static readonly string[] DovodyVyradenia =
    {
        "Neopraviteľná porucha",
        "Morálne zastarané",
        "Poškodenie pri prenášaní, oprava nerentabilná",
        "Nepodporovaný operačný systém",
    };

    public static void Generuj(EvitaDbContext db)
    {
        var r = new Random(20261020);
        var teraz = DateTime.Now;
        var dnes = DateTime.Today;

        // --- číselníky ---------------------------------------------------------------
        var kategorie = db.Kategorie.ToList();
        foreach (var nazov in Sablony.Select(s => s.Kategoria).Distinct())
        {
            if (!kategorie.Any(k => Sk.Porovnanie.Equals(k.Nazov, nazov)))
            {
                var nova = new Kategoria { Nazov = nazov };
                db.Kategorie.Add(nova);
                kategorie.Add(nova);
            }
        }

        var miestnosti = MiestnostiVzor
            .Select(m => new Miestnost { Nazov = m.Nazov, Budova = m.Budova })
            .ToList();
        db.Miestnosti.AddRange(miestnosti);

        var osoby = OsobyVzor
            .Select(o => new Osoba { Meno = o.Meno, Priezvisko = o.Priezvisko, Funkcia = o.Funkcia })
            .ToList();
        db.Osoby.AddRange(osoby);
        db.SaveChanges();

        Miestnost NajdiMiestnost(string nazov) => miestnosti.First(m => m.Nazov == nazov);
        Kategoria NajdiKategoriu(string nazov) => kategorie.First(k => Sk.Porovnanie.Equals(k.Nazov, nazov));

        var domov = new Dictionary<Osoba, Miestnost>();
        for (var i = 0; i < osoby.Count; i++)
        {
            domov[osoby[i]] = NajdiMiestnost(OsobyVzor[i].Domov);
        }

        var spravcaIt = osoby.First(o => o.Funkcia == "správca IT");
        var ucitelia = osoby.Where(o => o.Funkcia!.StartsWith("učiteľ")).ToList();
        var ucebne = miestnosti.Where((m, i) => MiestnostiVzor[i].Typ == "ucebna").ToList();
        var pcUcebne = miestnosti.Where((m, i) => MiestnostiVzor[i].Typ == "pc").ToList();
        var kancelarie = miestnosti.Where((m, i) => MiestnostiVzor[i].Typ is "kancelaria" or "kabinet").ToList();
        var bezSkolnika = osoby.Where(o => o.Funkcia != "školník").ToList();

        // --- položky ------------------------------------------------------------------
        var zaciatok = new DateTime(2019, 1, 7);
        var koniec = dnes.AddDays(-20);
        var rozsahDni = (koniec - zaciatok).Days;
        var polozky = new List<(Polozka P, Sablona S)>();

        foreach (var (nazovKategorie, pocet, kde) in Rozdelenie)
        {
            var sablony = Sablony.Where(s => s.Kategoria == nazovKategorie).ToArray();
            for (var i = 0; i < pocet; i++)
            {
                var s = sablony[r.Next(sablony.Length)];
                var nakup = zaciatok.AddDays(r.Next(rozsahDni));
                var cena = r.Next(s.CenaOd, s.CenaDo + 1) + (r.Next(3) switch { 0 => 0m, 1 => 0.90m, _ => 0.99m });

                var p = new Polozka
                {
                    Nazov = s.Nazov,
                    KategoriaId = NajdiKategoriu(s.Kategoria).Id,
                    Vyrobca = s.Vyrobca,
                    Model = s.Model,
                    SerioveCislo = SerioveCislo(r, s.Vyrobca),
                    Stav = StavPolozky.VPrevadzke,
                    DatumNakupu = nakup,
                    Cena = cena,
                    ZarukaDo = nakup.AddMonths(s.ZarukaMesiacov),
                    DalsiaRevizia = s.Revizia ? dnes.AddDays(r.Next(-15, 540)) : null,
                };

                Umiestni(p, kde, r, domov, spravcaIt, ucitelia, ucebne, pcUcebne, kancelarie, bezSkolnika, NajdiMiestnost);

                if (r.NextDouble() < 0.25)
                {
                    p.Poznamka = Poznamky[r.Next(Poznamky.Length)];
                }

                polozky.Add((p, s));
            }
        }

        // tablety v sklade sú časťou rezervné
        foreach (var (p, _) in polozky.Where(x => x.S.Kategoria == "tablet" && x.P.MiestnostId == NajdiMiestnost("Sklad IT").Id).Take(8))
        {
            p.Stav = StavPolozky.VSklade;
        }

        // vyradené: staršie kusy
        foreach (var (p, _) in polozky
                     .Where(x => x.P.Stav == StavPolozky.VPrevadzke && x.P.DatumNakupu < new DateTime(2021, 6, 1))
                     .OrderBy(_ => r.Next()).Take(10))
        {
            p.Stav = StavPolozky.Vyradene;
            var od = p.DatumNakupu!.Value.AddYears(3);
            var po = dnes.AddDays(-30);
            p.DatumVyradenia = od < po ? od.AddDays(r.Next(Math.Max(1, (po - od).Days))) : po;
            p.DovodVyradenia = DovodyVyradenia[r.Next(DovodyVyradenia.Length)];
        }

        // v oprave
        foreach (var (p, _) in polozky.Where(x => x.P.Stav == StavPolozky.VPrevadzke).OrderBy(_ => r.Next()).Take(6))
        {
            p.Stav = StavPolozky.VOprave;
        }

        var aktivne = polozky.Where(x => x.P.Stav != StavPolozky.Vyradene).OrderBy(_ => r.Next()).ToList();

        // záruky, ktoré čoskoro končia (aby bolo vidieť upozornenia)
        foreach (var (p, s) in aktivne.Take(4))
        {
            p.ZarukaDo = dnes.AddDays(r.Next(4, 28));
            p.DatumNakupu = p.ZarukaDo.Value.AddMonths(-s.ZarukaMesiacov);
        }

        // revízie, ktoré čoskoro končia
        foreach (var (p, _) in aktivne.Skip(4).Where(x => x.S.Revizia).Take(3))
        {
            p.DalsiaRevizia = dnes.AddDays(r.Next(2, 29));
        }

        // niekoľko položiek bez miestnosti (na ukážku upozornení)
        foreach (var (p, _) in aktivne.Skip(10).Take(3))
        {
            p.MiestnostId = null;
        }

        // vyradené nepotrebujú revíziu
        foreach (var (p, _) in polozky.Where(x => x.P.Stav == StavPolozky.Vyradene))
        {
            p.DalsiaRevizia = null;
        }

        // dátum pridania, prírastkové a inventárne čísla podľa poradia nákupu
        foreach (var (p, _) in polozky)
        {
            var pridanie = p.DatumNakupu!.Value.AddDays(r.Next(0, 8)).AddHours(r.Next(7, 15)).AddMinutes(r.Next(60));
            p.DatumPridania = pridanie > teraz ? teraz.AddMinutes(-r.Next(30, 600)) : pridanie;
            p.DatumZmeny = p.DatumPridania;
        }

        var poradie = polozky.OrderBy(x => x.P.DatumPridania).ToList();
        var inventarne = new Dictionary<string, int>();
        for (var i = 0; i < poradie.Count; i++)
        {
            var p = poradie[i].P;
            p.PrirastkoveCislo = i + 1;
            var predpona = p.Cena >= 1700m ? "DHM" : "DrHM";
            var rok = p.DatumNakupu!.Value.Year;
            var kluc = $"{predpona}-{rok}";
            inventarne[kluc] = inventarne.GetValueOrDefault(kluc) + 1;
            p.InventarneCislo = $"{predpona}-{rok}/{inventarne[kluc]:000}";
        }

        db.Polozky.AddRange(poradie.Select(x => x.P));
        db.SaveChanges();

        // --- história zmien -----------------------------------------------------------
        string NazovMiestnosti(int? id) => id is null ? "" : miestnosti.First(m => m.Id == id).Nazov;
        string MenoOsoby(int? id) => id is null ? "" : osoby.First(o => o.Id == id).CeleMeno;

        foreach (var (p, _) in poradie)
        {
            var zaznamy = new List<HistoriaZmeny>
            {
                Zaznam(p, p.DatumPridania, PolozkaService.AkciaVytvorenie, null, null, null),
            };

            if (p.MiestnostId is not null && r.NextDouble() < 0.3)
            {
                var stara = miestnosti[r.Next(miestnosti.Count)];
                if (stara.Id != p.MiestnostId)
                {
                    zaznamy.Add(Zaznam(p, Medzi(r, p.DatumPridania, teraz), PolozkaService.AkciaZmena,
                        "Miestnosť", stara.Nazov, NazovMiestnosti(p.MiestnostId)));
                }
            }

            if (p.OsobaId is not null && r.NextDouble() < 0.2)
            {
                var stara = osoby[r.Next(osoby.Count)];
                if (stara.Id != p.OsobaId)
                {
                    zaznamy.Add(Zaznam(p, Medzi(r, p.DatumPridania, teraz), PolozkaService.AkciaZmena,
                        "Pridelené", stara.CeleMeno, MenoOsoby(p.OsobaId)));
                }
            }

            var vPrevadzke = StavyPoloziek.Nazov(StavPolozky.VPrevadzke);
            switch (p.Stav)
            {
                case StavPolozky.VPrevadzke when r.NextDouble() < 0.12:
                {
                    var t1 = Medzi(r, p.DatumPridania, teraz.AddDays(-10));
                    var t2 = Medzi(r, t1, teraz);
                    var vOprave = StavyPoloziek.Nazov(StavPolozky.VOprave);
                    zaznamy.Add(Zaznam(p, t1, PolozkaService.AkciaZmena, "Stav", vPrevadzke, vOprave));
                    zaznamy.Add(Zaznam(p, t2, PolozkaService.AkciaZmena, "Stav", vOprave, vPrevadzke));
                    break;
                }
                case StavPolozky.VOprave:
                case StavPolozky.VSklade:
                {
                    var t = Medzi(r, MaxDatum(p.DatumPridania, teraz.AddDays(-40)), teraz);
                    zaznamy.Add(Zaznam(p, t, PolozkaService.AkciaZmena, "Stav", vPrevadzke, StavyPoloziek.Nazov(p.Stav)));
                    break;
                }
                case StavPolozky.Vyradene:
                {
                    var t = MaxDatum(p.DatumVyradenia!.Value.AddHours(10), p.DatumPridania.AddHours(1));
                    zaznamy.Add(Zaznam(p, t, PolozkaService.AkciaVyradenie, "Stav", vPrevadzke, StavyPoloziek.Nazov(p.Stav)));
                    zaznamy.Add(Zaznam(p, t, PolozkaService.AkciaVyradenie, "Dátum vyradenia", "", Sk.Datum(p.DatumVyradenia)));
                    zaznamy.Add(Zaznam(p, t, PolozkaService.AkciaVyradenie, "Dôvod vyradenia", "", p.DovodVyradenia));
                    break;
                }
            }

            p.DatumZmeny = zaznamy.Max(z => z.Cas);
            db.Historia.AddRange(zaznamy);
        }

        // --- pracovný kalendár ----------------------------------------------------------
        if (!db.TypyUdalosti.Any())
        {
            var por = 0;
            foreach (var (nazovTypu, farba) in Databaza.PociatocneTypyUdalosti)
            {
                db.TypyUdalosti.Add(new TypUdalosti { Nazov = nazovTypu, Farba = farba, Poradie = por++ });
            }

            db.SaveChanges();
        }

        var typy = db.TypyUdalosti.ToList();
        int Typ(string nazovTypu) => (typy.FirstOrDefault(t => t.Nazov == nazovTypu) ?? typy[0]).Id;
        var vsetkyPolozky = poradie.Select(x => x.P).ToList();
        var projektor = vsetkyPolozky.FirstOrDefault(p => p.Stav == StavPolozky.VOprave && p.Nazov.StartsWith("Projektor"))
                        ?? vsetkyPolozky.FirstOrDefault(p => p.Nazov.StartsWith("Projektor"));
        var tlaciaren = vsetkyPolozky.FirstOrDefault(p => p.Nazov.Contains("Tlačiareň") && p.Stav != StavPolozky.Vyradene);
        var ups = vsetkyPolozky.FirstOrDefault(p => p.Nazov.Contains("UPS"));
        var serverovna = miestnosti.FirstOrDefault(m => m.Nazov.Contains("Server")) ?? kancelarie[0];
        var pondelok = dnes.AddDays(-(((int)dnes.DayOfWeek + 6) % 7));
        var leto = new DateTime(dnes.Month > 7 ? dnes.Year + 1 : dnes.Year, 7, 1);

        Udalost Nova(string nazov, string typ, DateTime zaciatok, DateTime? koniec = null, bool celyDen = false,
            DruhUdalosti druh = DruhUdalosti.Udalost, StavUdalosti stav = StavUdalosti.Naplanovane,
            OpakovanieUdalosti opakovanie = OpakovanieUdalosti.Ziadne, PripomienkaUdalosti pripomienka = PripomienkaUdalosti.Ziadna,
            Polozka? polozka = null, Miestnost? miestnost = null, Osoba? osoba = null, string? popis = null) => new()
        {
            Nazov = nazov, Popis = popis, TypUdalostiId = Typ(typ), Zaciatok = zaciatok, Koniec = koniec, CelyDen = celyDen,
            Druh = druh, Stav = stav, Opakovanie = opakovanie, Pripomienka = pripomienka,
            PolozkaId = polozka?.Id, MiestnostId = miestnost?.Id, OsobaId = osoba?.Id, Vytvorene = teraz, Zmenene = teraz,
        };

        db.Udalosti.AddRange(new[]
        {
            Nova("Porada IT", "porada", pondelok.AddDays(-28).AddHours(8), pondelok.AddDays(-28).AddHours(8.5),
                opakovanie: OpakovanieUdalosti.Tyzdenne, pripomienka: PripomienkaUdalosti.VDen, miestnost: kancelarie[0],
                popis: "Pravidelná pondelková porada o stave techniky."),
            Nova("Porada vedenia", "porada", dnes.AddHours(11), dnes.AddHours(12), miestnost: kancelarie[0], osoba: spravcaIt),
            Nova("Výmena lampy v projektore", "servis", dnes.AddDays(2).AddHours(10), dnes.AddDays(2).AddHours(11),
                druh: DruhUdalosti.Uloha, pripomienka: PripomienkaUdalosti.DenVopred, polozka: projektor, osoba: spravcaIt,
                popis: "Lampa je objednaná, výmenu urobí servisný technik."),
            Nova("Servis tlačiarne – výmena valca", "servis", dnes.AddDays(-5).AddHours(9), dnes.AddDays(-5).AddHours(10),
                druh: DruhUdalosti.Uloha, stav: StavUdalosti.Hotovo, polozka: tlaciaren),
            Nova("Nákup tonerov do tlačiarní", "nákup", dnes.AddDays(-3), celyDen: true, druh: DruhUdalosti.Uloha,
                popis: "Čierny toner do všetkých laserových tlačiarní, 2 ks do rezervy."),
            Nova("Zálohovanie dokumentov na NAS", "iné", dnes.AddDays(-1).AddHours(15), druh: DruhUdalosti.Uloha, stav: StavUdalosti.Hotovo),
            Nova("Aktualizácia Windows na učebných PC", "iné", dnes.AddDays(1).AddHours(14), dnes.AddDays(1).AddHours(16),
                opakovanie: OpakovanieUdalosti.Mesacne, pripomienka: PripomienkaUdalosti.DenVopred, miestnost: pcUcebne.FirstOrDefault()),
            Nova("Kontrola záloh servera", "iné", new DateTime(dnes.Year, dnes.Month, 1).AddHours(7.5), druh: DruhUdalosti.Uloha,
                opakovanie: OpakovanieUdalosti.Mesacne, miestnost: serverovna),
            Nova("Prezentácia dodávateľa tabúľ", "porada", dnes.AddDays(3).AddHours(13), dnes.AddDays(3).AddHours(14),
                stav: StavUdalosti.Zrusene),
            Nova("Odovzdanie notebooku novej kolegyni", "iné", dnes.AddDays(4).AddHours(9), osoba: ucitelia.LastOrDefault(),
                druh: DruhUdalosti.Uloha, pripomienka: PripomienkaUdalosti.DenVopred),
            Nova("Školenie Microsoft 365 pre učiteľov", "školenie", dnes.AddDays(6).AddHours(13), dnes.AddDays(6).AddHours(15),
                pripomienka: PripomienkaUdalosti.TyzdenVopred, miestnost: pcUcebne.FirstOrDefault()),
            Nova("Jesenné prázdniny – údržba učební", "obdobie", dnes.AddDays(8), dnes.AddDays(10), celyDen: true,
                popis: "Čistenie projektorov, kontrola káblov a aktualizácie."),
            Nova("Objednávka interaktívnej tabule", "nákup", dnes.AddDays(14), celyDen: true, druh: DruhUdalosti.Uloha),
            Nova("Revízia elektrospotrebičov", "revízia", dnes.AddDays(21), celyDen: true, druh: DruhUdalosti.Uloha,
                pripomienka: PripomienkaUdalosti.TyzdenVopred, popis: "Revízny technik, všetky budovy."),
            Nova("Inventúrny súpis kabinetov", "inventúra", dnes.AddDays(25), celyDen: true, druh: DruhUdalosti.Uloha),
            Nova("Inventúra učební", "inventúra", dnes.AddDays(30), dnes.AddDays(34), celyDen: true,
                pripomienka: PripomienkaUdalosti.TyzdenVopred),
            Nova("Kontrola hasiacich prístrojov", "revízia", dnes.AddDays(45), celyDen: true),
            Nova("Ročný servis klimatizácie v serverovni", "servis", dnes.AddDays(60).AddHours(8), dnes.AddDays(60).AddHours(12),
                opakovanie: OpakovanieUdalosti.Rocne, miestnost: serverovna),
            Nova("Výmena batérie UPS", "servis", dnes.AddDays(-12).AddHours(10), druh: DruhUdalosti.Uloha, stav: StavUdalosti.Hotovo,
                polozka: ups),
            Nova("Letná údržba učební", "obdobie", leto, leto.AddDays(14), celyDen: true),
        });
        Databaza.Zapis(db, "Kalendar.DatumInventury", "11-30");

        Databaza.Zapis(db, Databaza.KlucPoslednePrirastkove, poradie.Count.ToString());
        Databaza.Zapis(db, Databaza.KlucUkazkoveData, "1");
        db.SaveChanges();
    }

    private static void Umiestni(
        Polozka p, Kde kde, Random r, Dictionary<Osoba, Miestnost> domov, Osoba spravcaIt,
        List<Osoba> ucitelia, List<Miestnost> ucebne, List<Miestnost> pcUcebne, List<Miestnost> kancelarie,
        List<Osoba> bezSkolnika, Func<string, Miestnost> miestnost)
    {
        switch (kde)
        {
            case Kde.Osoba:
            {
                var o = bezSkolnika[r.Next(bezSkolnika.Count)];
                p.OsobaId = o.Id;
                p.MiestnostId = domov[o].Id;
                break;
            }
            case Kde.Ucebna:
            {
                p.MiestnostId = ucebne[r.Next(ucebne.Count)].Id;
                if (r.NextDouble() < 0.3)
                {
                    p.OsobaId = ucitelia[r.Next(ucitelia.Count)].Id;
                }

                break;
            }
            case Kde.PocitacovaUcebna:
                p.MiestnostId = pcUcebne[r.Next(pcUcebne.Count)].Id;
                break;
            case Kde.Kancelaria:
            {
                var m = kancelarie[r.Next(kancelarie.Count)];
                p.MiestnostId = m.Id;
                var ludia = domov.Where(x => x.Value.Id == m.Id).Select(x => x.Key).ToList();
                if (ludia.Count > 0 && r.NextDouble() < 0.7)
                {
                    p.OsobaId = ludia[r.Next(ludia.Count)].Id;
                }

                break;
            }
            case Kde.Serverovna:
                p.MiestnostId = miestnost("Serverovňa").Id;
                p.OsobaId = spravcaIt.Id;
                break;
            case Kde.Sklad:
                p.MiestnostId = miestnost("Sklad IT").Id;
                p.OsobaId = spravcaIt.Id;
                break;
            default:
            {
                if (r.NextDouble() < 0.5)
                {
                    var o = bezSkolnika[r.Next(bezSkolnika.Count)];
                    p.OsobaId = o.Id;
                    p.MiestnostId = domov[o].Id;
                }
                else
                {
                    var vsetky = ucebne.Concat(kancelarie).ToList();
                    p.MiestnostId = vsetky[r.Next(vsetky.Count)].Id;
                }

                break;
            }
        }
    }

    private static HistoriaZmeny Zaznam(Polozka p, DateTime cas, string akcia, string? pole, string? stara, string? nova) => new()
    {
        PolozkaId = p.Id,
        PrirastkoveCislo = p.PrirastkoveCislo,
        NazovPolozky = p.Nazov,
        Cas = cas,
        Pouzivatel = PouzivatelUkazky,
        Akcia = akcia,
        Pole = pole,
        StaraHodnota = stara,
        NovaHodnota = nova,
    };

    private static DateTime Medzi(Random r, DateTime od, DateTime po)
    {
        if (po <= od)
        {
            return od;
        }

        return od.AddSeconds((po - od).TotalSeconds * r.NextDouble());
    }

    private static DateTime MaxDatum(DateTime a, DateTime b) => a > b ? a : b;

    private static string SerioveCislo(Random r, string vyrobca)
    {
        const string znaky = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
        var predpona = new string(vyrobca.Where(char.IsLetter).Take(2).Select(char.ToUpperInvariant).ToArray());
        var telo = new char[9];
        for (var i = 0; i < telo.Length; i++)
        {
            telo[i] = znaky[r.Next(znaky.Length)];
        }

        return predpona + new string(telo);
    }
}
