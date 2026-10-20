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
        (Preklad.N("notebook"), 40, Kde.Osoba),
        (Preklad.N("PC"), 18, Kde.PocitacovaUcebna),
        (Preklad.N("PC"), 12, Kde.Kancelaria),
        (Preklad.N("tablet"), 15, Kde.Sklad),
        (Preklad.N("tablet"), 15, Kde.Osoba),
        (Preklad.N("projektor"), 15, Kde.Ucebna),
        (Preklad.N("príslušenstvo"), 25, Kde.Kdekolvek),
        (Preklad.N("tlačiareň"), 8, Kde.Kancelaria),
        (Preklad.N("multifunkcia"), 8, Kde.Kancelaria),
        (Preklad.N("interaktívna tabuľa"), 10, Kde.Ucebna),
        (Preklad.N("multiboard"), 4, Kde.Ucebna),
        (Preklad.N("All-in-One"), 8, Kde.Kancelaria),
        (Preklad.N("slúchadlá"), 10, Kde.Osoba),
        (Preklad.N("switch"), 2, Kde.Serverovna),
        (Preklad.N("switch"), 2, Kde.PocitacovaUcebna),
        (Preklad.N("router"), 3, Kde.Serverovna),
        (Preklad.N("skener"), 3, Kde.Kancelaria),
        (Preklad.N("kopírka"), 2, Kde.Kancelaria),
    };

    private static readonly Sablona[] Sablony =
    {
        new(Preklad.N("All-in-One"), "HP", "ProOne 440 G9", Preklad.N("Počítač All-in-One HP ProOne 440 G9"), 750, 950, 36, true),
        new(Preklad.N("All-in-One"), "Lenovo", "IdeaCentre AIO 3 24", Preklad.N("Počítač All-in-One Lenovo IdeaCentre AIO 3"), 650, 850, 24, true),
        new(Preklad.N("interaktívna tabuľa"), "SMART", "Board MX275-V4", Preklad.N("Interaktívny panel SMART Board MX275"), 2800, 3600, 60, true),
        new(Preklad.N("interaktívna tabuľa"), "Promethean", "ActivPanel 9 75\"", Preklad.N("Interaktívny panel Promethean ActivPanel 9"), 2600, 3400, 60, true),
        new(Preklad.N("kopírka"), "Konica Minolta", "bizhub C258", Preklad.N("Farebná kopírka Konica Minolta bizhub C258"), 4200, 5200, 24, true),
        new(Preklad.N("kopírka"), "Canon", "imageRUNNER 2425", Preklad.N("Kopírka Canon imageRUNNER 2425"), 1500, 1900, 24, true),
        new(Preklad.N("multiboard"), "Newline", "Q Series 86\"", Preklad.N("Multiboard Newline 86\" na pojazdnom stojane"), 3200, 4200, 36, true),
        new(Preklad.N("multifunkcia"), "HP", "LaserJet Pro MFP 4102fdw", Preklad.N("Multifunkčná tlačiareň HP LaserJet Pro MFP 4102fdw"), 380, 480, 24, true),
        new(Preklad.N("multifunkcia"), "Brother", "MFC-L2750DW", Preklad.N("Multifunkčná tlačiareň Brother MFC-L2750DW"), 280, 360, 24, true),
        new(Preklad.N("multifunkcia"), "Epson", "EcoTank L6270", Preklad.N("Multifunkčná tlačiareň Epson EcoTank L6270"), 300, 380, 24, true),
        new(Preklad.N("notebook"), "Lenovo", "ThinkPad E14 Gen 5", Preklad.N("Notebook Lenovo ThinkPad E14 Gen 5"), 750, 950, 36, true),
        new(Preklad.N("notebook"), "HP", "ProBook 450 G10", Preklad.N("Notebook HP ProBook 450 G10"), 700, 900, 36, true),
        new(Preklad.N("notebook"), "Dell", "Latitude 3540", Preklad.N("Notebook Dell Latitude 3540"), 650, 850, 36, true),
        new(Preklad.N("notebook"), "Acer", "TravelMate P2", Preklad.N("Notebook Acer TravelMate P2"), 550, 700, 24, true),
        new(Preklad.N("PC"), "Dell", "OptiPlex 7010 SFF", Preklad.N("Počítač Dell OptiPlex 7010"), 650, 850, 36, true),
        new(Preklad.N("PC"), "HP", "ProDesk 400 G9 SFF", Preklad.N("Počítač HP ProDesk 400 G9"), 600, 780, 36, true),
        new(Preklad.N("PC"), "Lenovo", "ThinkCentre M70s Gen 4", Preklad.N("Počítač Lenovo ThinkCentre M70s"), 620, 800, 36, true),
        new(Preklad.N("príslušenstvo"), "Dell", "P2422H", Preklad.N("Monitor Dell P2422H 24\""), 170, 220, 36, true),
        new(Preklad.N("príslušenstvo"), "Logitech", "MK270", Preklad.N("Bezdrôtová klávesnica a myš Logitech MK270"), 25, 35, 24, false),
        new(Preklad.N("príslušenstvo"), "Logitech", "C920", Preklad.N("Webkamera Logitech C920"), 60, 80, 24, false),
        new(Preklad.N("príslušenstvo"), "Dell", "WD19S", Preklad.N("Dokovacia stanica Dell WD19S"), 180, 230, 36, true),
        new(Preklad.N("príslušenstvo"), "Western Digital", "Elements 2 TB", Preklad.N("Externý disk WD Elements 2 TB"), 70, 90, 24, false),
        new(Preklad.N("príslušenstvo"), "APC", "Back-UPS 700", Preklad.N("Záložný zdroj APC Back-UPS 700"), 110, 140, 24, true),
        new(Preklad.N("projektor"), "Epson", "EB-W51", Preklad.N("Projektor Epson EB-W51"), 420, 520, 36, true),
        new(Preklad.N("projektor"), "BenQ", "MW560", Preklad.N("Projektor BenQ MW560"), 450, 560, 36, true),
        new(Preklad.N("projektor"), "Optoma", "W400LVe", Preklad.N("Projektor Optoma W400LVe"), 430, 530, 24, true),
        new(Preklad.N("router"), "MikroTik", "hAP ax2", Preklad.N("Router MikroTik hAP ax2"), 90, 120, 24, true),
        new(Preklad.N("router"), "TP-Link", "Archer AX55", Preklad.N("Router TP-Link Archer AX55"), 80, 110, 36, true),
        new(Preklad.N("skener"), "Canon", "CanoScan LiDE 300", Preklad.N("Skener Canon CanoScan LiDE 300"), 60, 80, 24, true),
        new(Preklad.N("skener"), "Epson", "Perfection V39 II", Preklad.N("Skener Epson Perfection V39 II"), 85, 110, 24, true),
        new(Preklad.N("slúchadlá"), "Jabra", "Evolve2 30", Preklad.N("Slúchadlá s mikrofónom Jabra Evolve2 30"), 55, 75, 24, false),
        new(Preklad.N("slúchadlá"), "Logitech", "H390", Preklad.N("Slúchadlá s mikrofónom Logitech H390"), 25, 35, 24, false),
        new(Preklad.N("switch"), "Cisco", "CBS250-24T-4G", Preklad.N("Switch Cisco CBS250 24-portový"), 280, 360, 36, true),
        new(Preklad.N("switch"), "TP-Link", "TL-SG1016D", Preklad.N("Switch TP-Link TL-SG1016D 16-portový"), 60, 80, 36, true),
        new(Preklad.N("tablet"), "Apple", "iPad 10. generácie", Preklad.N("Tablet Apple iPad 10. generácie"), 380, 460, 24, true),
        new(Preklad.N("tablet"), "Samsung", "Galaxy Tab A9+", Preklad.N("Tablet Samsung Galaxy Tab A9+"), 220, 280, 24, true),
        new(Preklad.N("tablet"), "Lenovo", "Tab M10", Preklad.N("Tablet Lenovo Tab M10"), 180, 230, 24, true),
        new(Preklad.N("tlačiareň"), "HP", "LaserJet Pro 4002dn", Preklad.N("Tlačiareň HP LaserJet Pro 4002dn"), 230, 290, 24, true),
        new(Preklad.N("tlačiareň"), "Brother", "HL-L2350DW", Preklad.N("Tlačiareň Brother HL-L2350DW"), 110, 150, 24, true),
        new(Preklad.N("tlačiareň"), "Canon", "i-SENSYS LBP233dw", Preklad.N("Tlačiareň Canon i-SENSYS LBP233dw"), 190, 240, 24, true),
    };

    private static readonly MiestnostVzor[] MiestnostiVzor =
    {
        new(Preklad.N("Riaditeľňa"), Preklad.N("Hlavná budova"), "kancelaria"),
        new(Preklad.N("Sekretariát"), Preklad.N("Hlavná budova"), "kancelaria"),
        new(Preklad.N("Ekonomické oddelenie"), Preklad.N("Hlavná budova"), "kancelaria"),
        new(Preklad.N("Zasadačka"), Preklad.N("Hlavná budova"), "ucebna"),
        new(Preklad.N("Zborovňa"), Preklad.N("Hlavná budova"), "kancelaria"),
        new(Preklad.N("Kabinet IT"), Preklad.N("Hlavná budova"), "kabinet"),
        new(Preklad.N("Kabinet matematiky"), Preklad.N("Hlavná budova"), "kabinet"),
        new(Preklad.N("Kabinet jazykov"), Preklad.N("Hlavná budova"), "kabinet"),
        new(Preklad.N("Kabinet fyziky a chémie"), Preklad.N("Pavilón B"), "kabinet"),
        new(Preklad.N("Kabinet prírodovedy"), Preklad.N("Pavilón B"), "kabinet"),
        new(Preklad.N("Učebňa 101"), Preklad.N("Hlavná budova"), "ucebna"),
        new(Preklad.N("Učebňa 102"), Preklad.N("Hlavná budova"), "ucebna"),
        new(Preklad.N("Učebňa 103"), Preklad.N("Hlavná budova"), "ucebna"),
        new(Preklad.N("Učebňa 104"), Preklad.N("Hlavná budova"), "ucebna"),
        new(Preklad.N("Učebňa 201"), Preklad.N("Hlavná budova"), "ucebna"),
        new(Preklad.N("Učebňa 202"), Preklad.N("Hlavná budova"), "ucebna"),
        new(Preklad.N("Jazyková učebňa"), Preklad.N("Hlavná budova"), "ucebna"),
        new(Preklad.N("Počítačová učebňa 1"), Preklad.N("Pavilón B"), "pc"),
        new(Preklad.N("Počítačová učebňa 2"), Preklad.N("Pavilón B"), "pc"),
        new(Preklad.N("Fyzikálne laboratórium"), Preklad.N("Pavilón B"), "ucebna"),
        new(Preklad.N("Knižnica"), Preklad.N("Hlavná budova"), "ucebna"),
        new(Preklad.N("Telocvičňa"), Preklad.N("Telocvičňa"), "ucebna"),
        new(Preklad.N("Školský klub"), Preklad.N("Pavilón B"), "ucebna"),
        new(Preklad.N("Sklad IT"), Preklad.N("Hlavná budova"), "sklad"),
        new(Preklad.N("Serverovňa"), Preklad.N("Hlavná budova"), "server"),
    };

    private static readonly OsobaVzor[] OsobyVzor =
    {
        new("Peter", "Horváth", Preklad.N("riaditeľ"), "Riaditeľňa"),
        new("Zuzana", "Hrušková", Preklad.N("zástupkyňa riaditeľa"), "Riaditeľňa"),
        new("Eva", "Mrázová", Preklad.N("ekonómka"), "Ekonomické oddelenie"),
        new("Lucia", "Benková", Preklad.N("sekretárka"), "Sekretariát"),
        new("Martin", "Kováč", Preklad.N("správca IT"), "Kabinet IT"),
        new("Jozef", "Nagy", Preklad.N("školník"), "Sklad IT"),
        new("Ján", "Novák", Preklad.N("učiteľ"), "Kabinet matematiky"),
        new("Tomáš", "Varga", Preklad.N("učiteľ"), "Kabinet fyziky a chémie"),
        new("Michal", "Tóth", Preklad.N("učiteľ"), "Kabinet IT"),
        new("Marek", "Baláž", Preklad.N("učiteľ"), "Kabinet jazykov"),
        new("Lukáš", "Molnár", Preklad.N("učiteľ"), "Kabinet prírodovedy"),
        new("Juraj", "Szabó", Preklad.N("učiteľ"), "Kabinet matematiky"),
        new("Andrej", "Lukáč", Preklad.N("učiteľ"), "Kabinet fyziky a chémie"),
        new("Milan", "Kollár", Preklad.N("učiteľ"), "Kabinet jazykov"),
        new("Pavol", "Šimko", Preklad.N("učiteľ"), "Kabinet IT"),
        new("Róbert", "Gajdoš", Preklad.N("učiteľ"), "Kabinet prírodovedy"),
        new("Jana", "Kováčová", Preklad.N("učiteľka"), "Kabinet jazykov"),
        new("Katarína", "Polakovičová", Preklad.N("učiteľka"), "Kabinet matematiky"),
        new("Martina", "Lenková", Preklad.N("učiteľka"), "Kabinet prírodovedy"),
        new("Monika", "Fabianová", Preklad.N("učiteľka"), "Kabinet jazykov"),
        new("Andrea", "Sekerová", Preklad.N("učiteľka"), "Kabinet matematiky"),
        new("Veronika", "Krajčírová", Preklad.N("učiteľka"), "Kabinet fyziky a chémie"),
        new("Ivana", "Hudáková", Preklad.N("učiteľka"), "Kabinet jazykov"),
        new("Silvia", "Kubíková", Preklad.N("učiteľka"), "Kabinet prírodovedy"),
        new("Dana", "Švecová", Preklad.N("učiteľka"), "Zborovňa"),
        new("Lenka", "Oravcová", Preklad.N("učiteľka"), "Zborovňa"),
        new("Petra", "Kraľová", Preklad.N("učiteľka"), "Kabinet matematiky"),
        new("Alena", "Vašková", Preklad.N("vychovávateľka"), "Školský klub"),
        new("Mária", "Gregorová", Preklad.N("vychovávateľka"), "Školský klub"),
        new("Vladimír", "Hudec", Preklad.N("asistent učiteľa"), "Zborovňa"),
    };

    private static readonly string[] Poznamky =
    {
        Preklad.N("Nabíjačka je uložená v kabinete."),
        Preklad.N("V roku 2025 vymenená batéria."),
        Preklad.N("Zapožičané na projektový týždeň, vrátené."),
        Preklad.N("Lampa vymenená, ďalšia výmena po približne 4 000 hodinách."),
        Preklad.N("Heslo k BIOS-u má správca IT."),
        Preklad.N("Súčasť sady pre jazykovú učebňu."),
        Preklad.N("Reklamácia vybavená výmenou kusa."),
        Preklad.N("Pomalé, navrhnuté na vyradenie v budúcom roku."),
        Preklad.N("Pripojené k sieti cez kábel, pevná IP adresa."),
        Preklad.N("Pri prenášaní používať ochranný obal."),
        Preklad.N("Toner objednávať u dodávateľa podľa zmluvy."),
        Preklad.N("Diaľkový ovládač je v zásuvke katedry."),
    };

    private static readonly string[] DovodyVyradenia =
    {
        Preklad.N("Neopraviteľná porucha"),
        Preklad.N("Morálne zastarané"),
        Preklad.N("Poškodenie pri prenášaní, oprava nerentabilná"),
        Preklad.N("Nepodporovaný operačný systém"),
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
            if (!kategorie.Any(k => Sk.Porovnanie.Equals(k.Nazov, nazov) || Sk.Porovnanie.Equals(k.Nazov, Preklad.T(nazov))))
            {
                var nova = new Kategoria { Nazov = Preklad.T(nazov) };
                db.Kategorie.Add(nova);
                kategorie.Add(nova);
            }
        }

        var miestnosti = MiestnostiVzor
            .Select(m => new Miestnost { Nazov = Preklad.T(m.Nazov), Budova = Preklad.T(m.Budova) })
            .ToList();
        db.Miestnosti.AddRange(miestnosti);

        var osoby = OsobyVzor
            .Select(o => new Osoba { Meno = o.Meno, Priezvisko = o.Priezvisko, Funkcia = Preklad.T(o.Funkcia) })
            .ToList();
        db.Osoby.AddRange(osoby);
        db.SaveChanges();

        // miestnosti a osoby sa hľadajú podľa slovenských vzorov (v databáze sú v jazyku programu)
        Miestnost NajdiMiestnost(string nazov) => miestnosti[Array.FindIndex(MiestnostiVzor, m => m.Nazov == nazov)];
        Kategoria NajdiKategoriu(string nazov) =>
            kategorie.FirstOrDefault(k => Sk.Porovnanie.Equals(k.Nazov, Preklad.T(nazov))) ?? kategorie.First(k => Sk.Porovnanie.Equals(k.Nazov, nazov));
        List<Osoba> OsobySFunkciou(Func<string, bool> podmienka) => osoby.Where((_, i) => podmienka(OsobyVzor[i].Funkcia)).ToList();

        var domov = new Dictionary<Osoba, Miestnost>();
        for (var i = 0; i < osoby.Count; i++)
        {
            domov[osoby[i]] = NajdiMiestnost(OsobyVzor[i].Domov);
        }

        var spravcaIt = OsobySFunkciou(f => f == "správca IT")[0];
        var ucitelia = OsobySFunkciou(f => f.StartsWith("učiteľ"));
        var ucebne = miestnosti.Where((m, i) => MiestnostiVzor[i].Typ == "ucebna").ToList();
        var pcUcebne = miestnosti.Where((m, i) => MiestnostiVzor[i].Typ == "pc").ToList();
        var kancelarie = miestnosti.Where((m, i) => MiestnostiVzor[i].Typ is "kancelaria" or "kabinet").ToList();
        var bezSkolnika = OsobySFunkciou(f => f != "školník");

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
                    Nazov = Preklad.T(s.Nazov),
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
                    p.Poznamka = Preklad.T(Poznamky[r.Next(Poznamky.Length)]);
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
            p.DovodVyradenia = Preklad.T(DovodyVyradenia[r.Next(DovodyVyradenia.Length)]);
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
                db.TypyUdalosti.Add(new TypUdalosti { Nazov = Preklad.TAkJe(nazovTypu), Farba = farba, Poradie = por++ });
            }

            db.SaveChanges();
        }

        var typy = db.TypyUdalosti.ToList();
        int Typ(string nazovTypu) => (KalendarService.NajdiTyp(typy, nazovTypu) ?? typy[0]).Id;
        // položky sa hľadajú podľa slovenských vzorov (názvy v databáze sú v jazyku programu)
        Polozka? Polozka(Func<Sablona, bool> vzor, Func<Polozka, bool>? podmienka = null) =>
            poradie.Where(x => vzor(x.S) && (podmienka is null || podmienka(x.P))).Select(x => x.P).FirstOrDefault();
        var projektor = Polozka(s => s.Nazov.StartsWith("Projektor"), p => p.Stav == StavPolozky.VOprave)
                        ?? Polozka(s => s.Nazov.StartsWith("Projektor"));
        var tlaciaren = Polozka(s => s.Nazov.Contains("Tlačiareň"), p => p.Stav != StavPolozky.Vyradene);
        var ups = Polozka(s => s.Nazov.Contains("UPS"));
        var serverovna = NajdiMiestnost("Serverovňa");
        var pondelok = dnes.AddDays(-(((int)dnes.DayOfWeek + 6) % 7));
        var leto = new DateTime(dnes.Month > 7 ? dnes.Year + 1 : dnes.Year, 7, 1);

        Udalost Nova(string nazov, string typ, DateTime zaciatok, DateTime? koniec = null, bool celyDen = false,
            DruhUdalosti druh = DruhUdalosti.Udalost, StavUdalosti stav = StavUdalosti.Naplanovane,
            OpakovanieUdalosti opakovanie = OpakovanieUdalosti.Ziadne, PripomienkaUdalosti pripomienka = PripomienkaUdalosti.Ziadna,
            Polozka? polozka = null, Miestnost? miestnost = null, Osoba? osoba = null, string? popis = null) => new()
        {
            Nazov = Preklad.T(nazov), Popis = popis is null ? null : Preklad.T(popis), TypUdalostiId = Typ(typ), Zaciatok = zaciatok, Koniec = koniec, CelyDen = celyDen,
            Druh = druh, Stav = stav, Opakovanie = opakovanie, Pripomienka = pripomienka,
            PolozkaId = polozka?.Id, MiestnostId = miestnost?.Id, OsobaId = osoba?.Id, Vytvorene = teraz, Zmenene = teraz,
        };

        db.Udalosti.AddRange(new[]
        {
            Nova(Preklad.N("Porada IT"), "porada", pondelok.AddDays(-28).AddHours(8), pondelok.AddDays(-28).AddHours(8.5),
                opakovanie: OpakovanieUdalosti.Tyzdenne, pripomienka: PripomienkaUdalosti.VDen, miestnost: kancelarie[0],
                popis: Preklad.N("Pravidelná pondelková porada o stave techniky.")),
            Nova(Preklad.N("Porada vedenia"), "porada", dnes.AddHours(11), dnes.AddHours(12), miestnost: kancelarie[0], osoba: spravcaIt),
            Nova(Preklad.N("Výmena lampy v projektore"), "servis", dnes.AddDays(2).AddHours(10), dnes.AddDays(2).AddHours(11),
                druh: DruhUdalosti.Uloha, pripomienka: PripomienkaUdalosti.DenVopred, polozka: projektor, osoba: spravcaIt,
                popis: Preklad.N("Lampa je objednaná, výmenu urobí servisný technik.")),
            Nova(Preklad.N("Servis tlačiarne – výmena valca"), "servis", dnes.AddDays(-5).AddHours(9), dnes.AddDays(-5).AddHours(10),
                druh: DruhUdalosti.Uloha, stav: StavUdalosti.Hotovo, polozka: tlaciaren),
            Nova(Preklad.N("Nákup tonerov do tlačiarní"), "nákup", dnes.AddDays(-3), celyDen: true, druh: DruhUdalosti.Uloha,
                popis: Preklad.N("Čierny toner do všetkých laserových tlačiarní, 2 ks do rezervy.")),
            Nova(Preklad.N("Zálohovanie dokumentov na NAS"), "iné", dnes.AddDays(-1).AddHours(15), druh: DruhUdalosti.Uloha, stav: StavUdalosti.Hotovo),
            Nova(Preklad.N("Aktualizácia Windows na učebných PC"), "iné", dnes.AddDays(1).AddHours(14), dnes.AddDays(1).AddHours(16),
                opakovanie: OpakovanieUdalosti.Mesacne, pripomienka: PripomienkaUdalosti.DenVopred, miestnost: pcUcebne.FirstOrDefault()),
            Nova(Preklad.N("Kontrola záloh servera"), "iné", new DateTime(dnes.Year, dnes.Month, 1).AddHours(7.5), druh: DruhUdalosti.Uloha,
                opakovanie: OpakovanieUdalosti.Mesacne, miestnost: serverovna),
            Nova(Preklad.N("Prezentácia dodávateľa tabúľ"), "porada", dnes.AddDays(3).AddHours(13), dnes.AddDays(3).AddHours(14),
                stav: StavUdalosti.Zrusene),
            Nova(Preklad.N("Odovzdanie notebooku novej kolegyni"), "iné", dnes.AddDays(4).AddHours(9), osoba: ucitelia.LastOrDefault(),
                druh: DruhUdalosti.Uloha, pripomienka: PripomienkaUdalosti.DenVopred),
            Nova(Preklad.N("Školenie Microsoft 365 pre učiteľov"), "školenie", dnes.AddDays(6).AddHours(13), dnes.AddDays(6).AddHours(15),
                pripomienka: PripomienkaUdalosti.TyzdenVopred, miestnost: pcUcebne.FirstOrDefault()),
            Nova(Preklad.N("Jesenné prázdniny – údržba učební"), "obdobie", dnes.AddDays(8), dnes.AddDays(10), celyDen: true,
                popis: Preklad.N("Čistenie projektorov, kontrola káblov a aktualizácie.")),
            Nova(Preklad.N("Objednávka interaktívnej tabule"), "nákup", dnes.AddDays(14), celyDen: true, druh: DruhUdalosti.Uloha),
            Nova(Preklad.N("Revízia elektrospotrebičov"), "revízia", dnes.AddDays(21), celyDen: true, druh: DruhUdalosti.Uloha,
                pripomienka: PripomienkaUdalosti.TyzdenVopred, popis: Preklad.N("Revízny technik, všetky budovy.")),
            Nova(Preklad.N("Inventúrny súpis kabinetov"), "inventúra", dnes.AddDays(25), celyDen: true, druh: DruhUdalosti.Uloha),
            Nova(Preklad.N("Inventúra učební"), "inventúra", dnes.AddDays(30), dnes.AddDays(34), celyDen: true,
                pripomienka: PripomienkaUdalosti.TyzdenVopred),
            Nova(Preklad.N("Kontrola hasiacich prístrojov"), "revízia", dnes.AddDays(45), celyDen: true),
            Nova(Preklad.N("Ročný servis klimatizácie v serverovni"), "servis", dnes.AddDays(60).AddHours(8), dnes.AddDays(60).AddHours(12),
                opakovanie: OpakovanieUdalosti.Rocne, miestnost: serverovna),
            Nova(Preklad.N("Výmena batérie UPS"), "servis", dnes.AddDays(-12).AddHours(10), druh: DruhUdalosti.Uloha, stav: StavUdalosti.Hotovo,
                polozka: ups),
            Nova(Preklad.N("Letná údržba učební"), "obdobie", leto, leto.AddDays(14), celyDen: true),
        });
        Databaza.Zapis(db, "Kalendar.DatumInventury", "11-30");

        // --- úlohy a poznámky -----------------------------------------------------------
        db.SaveChanges();
        var notebook = Polozka(s => s.Nazov.StartsWith("Notebook"), p => p.Stav == StavPolozky.VPrevadzke);
        Uloha UlohaNova(string text, DateTime? termin = null, PrioritaUlohy priorita = PrioritaUlohy.Normalna, bool hotovo = false,
            Polozka? polozka = null, string? popis = null, int poradieUlohy = 0) => new()
        {
            Text = Preklad.T(text), Termin = termin, Priorita = priorita, Hotovo = hotovo, DatumHotovo = hotovo ? teraz.AddDays(-1) : null,
            PolozkaId = polozka?.Id, Popis = popis is null ? null : Preklad.T(popis), Vytvorene = teraz.AddDays(-7), Poradie = poradieUlohy,
        };
        db.Ulohy.AddRange(new[]
        {
            UlohaNova(Preklad.N("Objednať toner do tlačiarne v zborovni"), dnes.AddDays(-1), PrioritaUlohy.Vysoka, polozka: tlaciaren, poradieUlohy: 1),
            UlohaNova(Preklad.N("Nastaviť e-mail novej kolegyni"), dnes, PrioritaUlohy.Vysoka, poradieUlohy: 2),
            UlohaNova(Preklad.N("Skontrolovať licencie Microsoft 365"), dnes.AddDays(5), poradieUlohy: 3,
                popis: Preklad.N("Počet licencií porovnať s počtom zamestnancov a žiakov.")),
            UlohaNova(Preklad.N("Vymeniť batérie v bezdrôtových myšiach"), poradieUlohy: 4),
            UlohaNova(Preklad.N("Aktualizovať firmvér routera"), dnes.AddDays(10), PrioritaUlohy.Nizka, poradieUlohy: 5),
            UlohaNova(Preklad.N("Pripraviť notebook na zapožičanie"), dnes.AddDays(2), polozka: notebook, poradieUlohy: 6),
            UlohaNova(Preklad.N("Vyčistiť filtre projektorov"), hotovo: true, poradieUlohy: 7),
        });

        Poznamka PoznamkaNova(string nazov, string text, string farba, bool pripnuta = false, DateTime? datum = null,
            Polozka? polozka = null, Udalost? udalost = null) => new()
        {
            Nazov = Preklad.T(nazov), Text = Preklad.T(text), Farba = farba, Pripnuta = pripnuta, Datum = datum, PolozkaId = polozka?.Id, Udalost = udalost,
            Vytvorene = teraz.AddDays(-3), Zmenene = teraz.AddHours(-5),
        };
        var skolenie = db.Udalosti.Local.FirstOrDefault(u => u.Nazov == Preklad.T("Školenie Microsoft 365 pre učiteľov"));
        db.Poznamky.AddRange(new[]
        {
            PoznamkaNova(Preklad.N("Wi-Fi pre hostí"), Preklad.N("Sieť: Skola-Host\nHeslo sa mení každý mesiac – aktuálne je na nástenke v zborovni.\nPripojenie platí 8 hodín."),
                PoznamkyService.Farby[0], pripnuta: true),
            PoznamkaNova(Preklad.N("Kontakty na servis"), Preklad.N("Tlačiarne: servis Kopírky s. r. o., 0900 123 456\nProjektory: AV Technik, 0911 222 333\nSieť a internet: poskytovateľ, linka 24/7"),
                PoznamkyService.Farby[4], pripnuta: true),
            PoznamkaNova(Preklad.N("Projektor – lampa"), Preklad.N("Lampa má odsvietených 3 850 hodín z 4 000. Náhradná je objednaná, dodanie do týždňa."),
                PoznamkyService.Farby[1], polozka: projektor),
            PoznamkaNova(Preklad.N("Školenie – čo pripraviť"), Preklad.N("• prihlasovacie údaje pre všetkých učiteľov\n• projektor a reproduktory v učebni\n• krátky návod na OneDrive"),
                PoznamkyService.Farby[6], datum: skolenie?.Zaciatok.Date, udalost: skolenie),
            PoznamkaNova(Preklad.N("Nápady na budúci rok"), Preklad.N("Nové interaktívne tabule do dvoch tried, výmena najstarších PC v učebni informatiky."),
                PoznamkyService.Farby[3]),
        });

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
