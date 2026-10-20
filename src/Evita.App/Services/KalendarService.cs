using System.Globalization;
using System.Text;
using Evita.Data;
using Evita.Models;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

/// <summary>Jeden výskyt udalosti v kalendári (pri opakovaných udalostiach každé opakovanie zvlášť) alebo automatický termín.</summary>
public sealed record VyskytUdalosti
{
    /// <summary>Id udalosti v databáze; null pri automatickom termíne z evidencie.</summary>
    public int? UdalostId { get; init; }

    public string Kluc { get; init; } = "";
    public string Nazov { get; init; } = "";
    public string? Popis { get; init; }
    public DateTime Zaciatok { get; init; }
    public DateTime? Koniec { get; init; }
    public bool CelyDen { get; init; }
    public string Typ { get; init; } = "";
    public string Farba { get; init; } = "#7F8C8D";
    public DruhUdalosti Druh { get; init; }
    public StavUdalosti Stav { get; init; }
    public OpakovanieUdalosti Opakovanie { get; init; }
    public PripomienkaUdalosti Pripomienka { get; init; }
    public int? PolozkaId { get; init; }
    public string? Naviazanie { get; init; }

    /// <summary>Zdroj automatického termínu: záruka, revízia, záloha, inventúra.</summary>
    public string? Zdroj { get; init; }

    public bool Automaticky => UdalostId is null;

    /// <summary>Posledný deň výskytu (pri udalosti cez viac dní).</summary>
    public DateTime PoslednyDen => (Koniec ?? Zaciatok).Date < Zaciatok.Date ? Zaciatok.Date : (Koniec ?? Zaciatok).Date;

    public bool ViacDni => PoslednyDen > Zaciatok.Date;

    public string CasText => CelyDen || ViacDni
        ? ViacDni ? $"{Sk.Datum(Zaciatok)} – {Sk.Datum(PoslednyDen)}" : Preklad.T("celý deň")
        : Koniec is { } k && k > Zaciatok ? $"{Zaciatok:H:mm} – {k:H:mm}" : $"{Zaciatok:H:mm}";

    public string StavText => Stav switch
    {
        StavUdalosti.Hotovo => Preklad.T("hotovo"),
        StavUdalosti.Zrusene => Preklad.T("zrušené"),
        _ => Preklad.T("naplánované"),
    };

    public string DruhText => Druh == DruhUdalosti.Uloha ? Preklad.T("úloha") : Preklad.T("udalosť");

    /// <summary>Text do bunky kalendára, napr. „8:00 Porada“.</summary>
    public string KratkyText => CelyDen || ViacDni ? Nazov : $"{Zaciatok:H:mm} {Nazov}";
}

public sealed record PrehladDna(List<VyskytUdalosti> Dnes, List<VyskytUdalosti> Pripomienky, List<VyskytUdalosti> PoTermine)
{
    public bool JePrazdny => Dnes.Count == 0 && Pripomienky.Count == 0 && PoTermine.Count == 0;
}

public sealed record NastaveniaKalendara(
    bool Zaruky,
    bool Revizie,
    bool Zalohy,
    bool Inventura,
    DateTime? DatumInventury,
    bool PrehladPriSpusteni,
    bool Upozornenia);

/// <summary>Pracovný kalendár: udalosti a úlohy, opakovanie, automatické termíny z evidencie, prehľad dňa a export .ics.</summary>
public sealed class KalendarService
{
    private const string KlucZaruky = "Kalendar.AutoZaruky";
    private const string KlucRevizie = "Kalendar.AutoRevizie";
    private const string KlucZalohy = "Kalendar.AutoZalohy";
    private const string KlucInventura = "Kalendar.AutoInventura";
    private const string KlucDatumInventury = "Kalendar.DatumInventury";
    private const string KlucPrehlad = "Kalendar.PrehladPriSpusteni";
    private const string KlucUpozornenia = "Kalendar.Upozornenia";

    public const string FarbaZaruky = "#B9770E";
    public const string FarbaZalohy = "#566573";

    /// <summary>Paleta farieb na výber pri typoch udalostí.</summary>
    public static readonly string[] Paleta =
    {
        "#C0392B", "#E74C3C", "#E67E22", "#D4AC0D", "#27AE60", "#16A085", "#2E86C1", "#1F618D",
        "#8E44AD", "#C2185B", "#6D4C41", "#7F8C8D",
    };

    // ------------------------------------------------------------------ nastavenia

    /// <summary>
    /// Typ udalosti podľa počiatočného (slovenského) názvu – nájde ho, aj keď bol vytvorený v inom jazyku programu
    /// (napr. „servis“ aj „service“).
    /// </summary>
    public static TypUdalosti? NajdiTyp(IEnumerable<TypUdalosti> typy, string slovenskyNazov)
    {
        var zoznam = typy as IList<TypUdalosti> ?? typy.ToList();
        return zoznam.FirstOrDefault(t => Sk.Porovnanie.Equals(t.Nazov, slovenskyNazov))
               ?? zoznam.FirstOrDefault(t => Sk.Porovnanie.Equals(t.Nazov, Preklad.TAkJe(slovenskyNazov)));
    }

    public NastaveniaKalendara NacitajNastavenia()
    {
        using var db = Databaza.Otvor();
        bool Zapnute(string kluc) => Databaza.Citaj(db, kluc) != "0";
        DateTime? inventura = DateTime.TryParseExact(Databaza.Citaj(db, KlucDatumInventury), "MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;
        return new NastaveniaKalendara(Zapnute(KlucZaruky), Zapnute(KlucRevizie), Zapnute(KlucZalohy), Zapnute(KlucInventura),
            inventura, Zapnute(KlucPrehlad), Zapnute(KlucUpozornenia));
    }

    public void UlozNastavenia(NastaveniaKalendara n)
    {
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucZaruky, n.Zaruky ? "1" : "0");
        Databaza.Zapis(db, KlucRevizie, n.Revizie ? "1" : "0");
        Databaza.Zapis(db, KlucZalohy, n.Zalohy ? "1" : "0");
        Databaza.Zapis(db, KlucInventura, n.Inventura ? "1" : "0");
        Databaza.Zapis(db, KlucDatumInventury, n.DatumInventury?.ToString("MM-dd", CultureInfo.InvariantCulture));
        Databaza.Zapis(db, KlucPrehlad, n.PrehladPriSpusteni ? "1" : "0");
        Databaza.Zapis(db, KlucUpozornenia, n.Upozornenia ? "1" : "0");
        db.SaveChanges();
    }

    // ------------------------------------------------------------------ typy udalostí

    public List<TypUdalosti> Typy(bool lenAktivne = false)
    {
        using var db = Databaza.Otvor();
        return db.TypyUdalosti.AsNoTracking()
            .Where(t => !lenAktivne || t.Aktivny)
            .OrderBy(t => t.Poradie).ThenBy(t => t.Id)
            .ToList();
    }

    public int PocetPouziti(int typId)
    {
        using var db = Databaza.Otvor();
        return db.Udalosti.Count(u => u.TypUdalostiId == typId);
    }

    /// <summary>Pridá alebo upraví typ udalosti. Vráti jeho Id.</summary>
    public int UlozTyp(int id, string nazov, string farba)
    {
        nazov = (nazov ?? "").Trim();
        if (nazov.Length == 0)
        {
            throw new EvitaException(Preklad.T("Zadajte názov typu udalosti."));
        }

        using var db = Databaza.Otvor();
        if (db.TypyUdalosti.Where(t => t.Id != id).AsEnumerable().Any(t => Sk.Porovnanie.Equals(t.Nazov, nazov)))
        {
            throw new EvitaException(Preklad.F($"Typ „{nazov}“ už existuje."));
        }

        var typ = id == 0
            ? db.TypyUdalosti.Add(new TypUdalosti { Poradie = (db.TypyUdalosti.Max(t => (int?)t.Poradie) ?? 0) + 1 }).Entity
            : db.TypyUdalosti.Find(id) ?? throw new EvitaException(Preklad.T("Typ udalosti už neexistuje."));
        typ.Nazov = nazov;
        typ.Farba = string.IsNullOrWhiteSpace(farba) ? "#7F8C8D" : farba.Trim();
        typ.Aktivny = true;
        db.SaveChanges();
        return typ.Id;
    }

    /// <summary>Vymaže typ. Ak ho používa niektorá udalosť, len ho skryje z ponuky. Vráti true, ak bol vymazaný.</summary>
    public bool VymazTyp(int id)
    {
        using var db = Databaza.Otvor();
        var typ = db.TypyUdalosti.Find(id) ?? throw new EvitaException(Preklad.T("Typ udalosti už neexistuje."));
        if (db.Udalosti.Any(u => u.TypUdalostiId == id))
        {
            typ.Aktivny = false;
            db.SaveChanges();
            return false;
        }

        if (db.TypyUdalosti.Count() <= 1)
        {
            throw new EvitaException(Preklad.T("Musí zostať aspoň jeden typ udalosti."));
        }

        db.TypyUdalosti.Remove(typ);
        db.SaveChanges();
        return true;
    }

    // ------------------------------------------------------------------ udalosti

    public Udalost? Nacitaj(int id)
    {
        using var db = Databaza.Otvor();
        return db.Udalosti.AsNoTracking().FirstOrDefault(u => u.Id == id);
    }

    /// <summary>Uloží novú alebo upravenú udalosť. Vráti jej Id.</summary>
    public int Uloz(Udalost u)
    {
        if (string.IsNullOrWhiteSpace(u.Nazov))
        {
            throw new EvitaException(Preklad.T("Zadajte názov udalosti."));
        }

        if (u.CelyDen)
        {
            u.Zaciatok = u.Zaciatok.Date;
            u.Koniec = u.Koniec?.Date;
        }

        if (u.Koniec is { } koniec && koniec < u.Zaciatok)
        {
            throw new EvitaException(Preklad.T("Koniec udalosti je skôr ako jej začiatok."));
        }

        if (u.Opakovanie == OpakovanieUdalosti.Ziadne)
        {
            u.OpakovanieDo = null;
        }
        else if (u.OpakovanieDo is { } doDna && doDna.Date < u.Zaciatok.Date)
        {
            throw new EvitaException(Preklad.T("Dátum ukončenia opakovania je skôr ako začiatok udalosti."));
        }

        using var db = Databaza.Otvor();
        if (!db.TypyUdalosti.Any(t => t.Id == u.TypUdalostiId))
        {
            throw new EvitaException(Preklad.T("Vyberte typ udalosti."));
        }

        var teraz = DateTime.Now;
        Udalost cielova;
        if (u.Id == 0)
        {
            cielova = new Udalost { Guid = Guid.NewGuid(), Vytvorene = teraz };
            db.Udalosti.Add(cielova);
        }
        else
        {
            cielova = db.Udalosti.Find(u.Id) ?? throw new EvitaException(Preklad.T("Udalosť medzičasom zanikla (bola vymazaná)."));
        }

        cielova.Nazov = u.Nazov.Trim();
        cielova.Popis = string.IsNullOrWhiteSpace(u.Popis) ? null : u.Popis.TrimEnd();
        cielova.TypUdalostiId = u.TypUdalostiId;
        cielova.Druh = u.Druh;
        cielova.Zaciatok = u.Zaciatok;
        cielova.Koniec = u.Koniec;
        cielova.CelyDen = u.CelyDen;
        cielova.Opakovanie = u.Opakovanie;
        cielova.OpakovanieDo = u.OpakovanieDo?.Date;
        cielova.Pripomienka = u.Pripomienka;
        cielova.PolozkaId = u.PolozkaId;
        cielova.MiestnostId = u.MiestnostId;
        cielova.OsobaId = u.OsobaId;
        cielova.Stav = u.Stav;
        cielova.Zmenene = teraz;
        db.SaveChanges();
        return cielova.Id;
    }

    public void Vymaz(int id)
    {
        using var db = Databaza.Otvor();
        if (db.Udalosti.Find(id) is { } u)
        {
            db.Udalosti.Remove(u);
            db.SaveChanges();
        }
    }

    public void NastavStav(int id, StavUdalosti stav)
    {
        using var db = Databaza.Otvor();
        if (db.Udalosti.Find(id) is { } u)
        {
            u.Stav = stav;
            u.Zmenene = DateTime.Now;
            db.SaveChanges();
        }
    }

    // ------------------------------------------------------------------ výskyty v období

    /// <summary>Všetky výskyty udalostí a automatických termínov, ktoré zasahujú do obdobia od – do (vrátane).</summary>
    public List<VyskytUdalosti> Vyskyty(DateTime od, DateTime po, int? polozkaId = null, bool automaticke = true)
    {
        var od0 = od.Date;
        var po1 = po.Date.AddDays(1);
        var vysledok = new List<VyskytUdalosti>();

        using var db = Databaza.Otvor();
        var typy = db.TypyUdalosti.AsNoTracking().ToDictionary(t => t.Id);
        var dotaz = db.Udalosti.AsNoTracking()
            .Include(u => u.Polozka)
            .Include(u => u.Miestnost)
            .Include(u => u.Osoba)
            .Where(u => u.Zaciatok < po1);
        if (polozkaId is not null)
        {
            dotaz = dotaz.Where(u => u.PolozkaId == polozkaId);
        }

        foreach (var u in dotaz.ToList())
        {
            var typ = typy.TryGetValue(u.TypUdalostiId, out var t) ? t : null;
            foreach (var zaciatok in Zaciatky(u, od0, po1))
            {
                var posun = zaciatok - u.Zaciatok;
                vysledok.Add(new VyskytUdalosti
                {
                    UdalostId = u.Id,
                    Kluc = $"{u.Guid:N}-{zaciatok:yyyyMMddHHmm}",
                    Nazov = u.Nazov,
                    Popis = u.Popis,
                    Zaciatok = zaciatok,
                    Koniec = u.Koniec + posun,
                    CelyDen = u.CelyDen,
                    Typ = typ?.Nazov ?? "",
                    Farba = typ?.Farba ?? "#7F8C8D",
                    Druh = u.Druh,
                    Stav = u.Stav,
                    Opakovanie = u.Opakovanie,
                    Pripomienka = u.Pripomienka,
                    PolozkaId = u.PolozkaId,
                    Naviazanie = Naviazanie(u),
                });
            }
        }

        if (automaticke)
        {
            vysledok.AddRange(AutomatickeTerminy(db, od0, po1, polozkaId, typy.Values));
        }

        return vysledok
            .OrderBy(v => v.Zaciatok.Date)
            .ThenBy(v => v.ViacDni ? 0 : v.CelyDen ? 1 : 2)
            .ThenBy(v => v.Zaciatok)
            .ThenBy(v => v.Nazov, Sk.Porovnanie)
            .ToList();
    }

    /// <summary>Začiatky opakovaní udalosti, ktoré zasahujú do obdobia [od0, po1).</summary>
    public static IEnumerable<DateTime> Zaciatky(Udalost u, DateTime od0, DateTime po1)
    {
        var trvanie = (u.Koniec ?? u.Zaciatok) - u.Zaciatok;
        if (trvanie < TimeSpan.Zero)
        {
            trvanie = TimeSpan.Zero;
        }

        if (u.Opakovanie == OpakovanieUdalosti.Ziadne)
        {
            if (u.Zaciatok < po1 && (u.Zaciatok + trvanie).Date >= od0)
            {
                yield return u.Zaciatok;
            }

            yield break;
        }

        var koniecOpakovania = u.OpakovanieDo?.Date.AddDays(1) ?? DateTime.MaxValue;
        var krok = u.Opakovanie switch
        {
            OpakovanieUdalosti.Denne => 1,
            OpakovanieUdalosti.Tyzdenne => 7,
            _ => 0,
        };

        // pri dennom a týždennom opakovaní preskočíme rovno blízko k začiatku obdobia
        var k = 0;
        if (krok > 0 && od0 > u.Zaciatok)
        {
            k = Math.Max(0, (int)((od0 - u.Zaciatok - trvanie).TotalDays / krok) - 1);
        }

        for (var pocet = 0; pocet < 50000; pocet++, k++)
        {
            var zaciatok = u.Opakovanie switch
            {
                OpakovanieUdalosti.Mesacne => u.Zaciatok.AddMonths(k),
                OpakovanieUdalosti.Rocne => u.Zaciatok.AddYears(k),
                _ => u.Zaciatok.AddDays((double)k * krok),
            };

            if (zaciatok >= po1 || zaciatok >= koniecOpakovania)
            {
                yield break;
            }

            if ((zaciatok + trvanie).Date >= od0)
            {
                yield return zaciatok;
            }
        }
    }

    private static string? Naviazanie(Udalost u)
    {
        var casti = new List<string>();
        if (u.Polozka is { } p)
        {
            casti.Add(Preklad.F($"č. {p.PrirastkoveCislo} {p.Nazov}"));
        }

        if (u.Miestnost is { } m)
        {
            casti.Add(m.Nazov);
        }

        if (u.Osoba is { } o)
        {
            casti.Add(o.CeleMeno);
        }

        return casti.Count == 0 ? null : string.Join(" · ", casti);
    }

    /// <summary>Termíny, ktoré vyplývajú z evidencie a nastavení: záruky, revízie, pravidelná záloha, ročná inventúra.</summary>
    private List<VyskytUdalosti> AutomatickeTerminy(EvitaDbContext db, DateTime od0, DateTime po1, int? polozkaId, IEnumerable<TypUdalosti> typy)
    {
        var vysledok = new List<VyskytUdalosti>();
        var nastavenia = NacitajNastavenia();
        string Farba(string nazovTypu, string nahradna) =>
            NajdiTyp(typy, nazovTypu)?.Farba ?? nahradna;

        if (nastavenia.Zaruky || nastavenia.Revizie)
        {
            var dotaz = db.Polozky.AsNoTracking().Where(p => p.Stav != StavPolozky.Vyradene);
            if (polozkaId is not null)
            {
                dotaz = dotaz.Where(p => p.Id == polozkaId);
            }

            var polozky = dotaz
                .Where(p => (p.ZarukaDo >= od0 && p.ZarukaDo < po1) || (p.DalsiaRevizia >= od0 && p.DalsiaRevizia < po1))
                .Select(p => new { p.Id, p.PrirastkoveCislo, p.Nazov, p.ZarukaDo, p.DalsiaRevizia })
                .ToList();
            var farbaRevizie = Farba("revízia", "#C0392B");
            foreach (var p in polozky)
            {
                if (nastavenia.Zaruky && p.ZarukaDo is { } zaruka && zaruka >= od0 && zaruka < po1)
                {
                    vysledok.Add(new VyskytUdalosti
                    {
                        Kluc = $"zaruka-{p.Id}",
                        Nazov = Preklad.F($"Koniec záruky: {p.Nazov}"),
                        Popis = Preklad.F($"Záruka položky č. {p.PrirastkoveCislo} končí. Termín je z poľa „Záruka do“ v karte položky."),
                        Zaciatok = zaruka.Date,
                        CelyDen = true,
                        Typ = Preklad.T("koniec záruky"),
                        Farba = FarbaZaruky,
                        Pripomienka = PripomienkaUdalosti.TyzdenVopred,
                        PolozkaId = p.Id,
                        Naviazanie = Preklad.F($"č. {p.PrirastkoveCislo} {p.Nazov}"),
                        Zdroj = "záruka",
                    });
                }

                if (nastavenia.Revizie && p.DalsiaRevizia is { } revizia && revizia >= od0 && revizia < po1)
                {
                    vysledok.Add(new VyskytUdalosti
                    {
                        Kluc = $"revizia-{p.Id}",
                        Nazov = Preklad.F($"Revízia: {p.Nazov}"),
                        Popis = Preklad.F($"Revízia položky č. {p.PrirastkoveCislo}. Termín je z poľa „Ďalšia revízia“ v karte položky."),
                        Zaciatok = revizia.Date,
                        CelyDen = true,
                        Typ = Preklad.T("revízia"),
                        Farba = farbaRevizie,
                        Druh = DruhUdalosti.Uloha,
                        Pripomienka = PripomienkaUdalosti.TyzdenVopred,
                        PolozkaId = p.Id,
                        Naviazanie = Preklad.F($"č. {p.PrirastkoveCislo} {p.Nazov}"),
                        Zdroj = "revízia",
                    });
                }
            }
        }

        if (polozkaId is not null)
        {
            return vysledok;
        }

        if (nastavenia.Zalohy)
        {
            try
            {
                var zalohy = new ZalohaService();
                var nastaveniaZaloh = zalohy.NacitajNastavenia();
                if (nastaveniaZaloh.Zapnuta)
                {
                    var dnes = DateTime.Today;
                    var termin = ZalohaService.DalsiTermin(zalohy.PoslednaUspesna() ?? dnes.AddYears(-1), nastaveniaZaloh.Perioda);
                    if (termin < dnes)
                    {
                        termin = dnes;
                    }

                    for (var i = 0; i < 400 && termin < po1; i++)
                    {
                        if (termin >= od0)
                        {
                            vysledok.Add(new VyskytUdalosti
                            {
                                Kluc = $"zaloha-{termin:yyyyMMdd}",
                                Nazov = Preklad.F($"Pravidelná záloha ({ZalohaService.NazovPeriody(nastaveniaZaloh.Perioda)})"),
                                Popis = Preklad.F($"Automatická záloha databázy podľa nastavení v časti Servis → Zálohy, okolo {nastaveniaZaloh.Cas}."),
                                Zaciatok = termin.Date,
                                CelyDen = true,
                                Typ = Preklad.T("záloha"),
                                Farba = FarbaZalohy,
                                Zdroj = "záloha",
                            });
                        }

                        termin = ZalohaService.DalsiTermin(termin, nastaveniaZaloh.Perioda);
                    }
                }
            }
            catch (Exception)
            {
                // kalendár funguje aj bez údajov o zálohách
            }
        }

        if (nastavenia.Inventura && nastavenia.DatumInventury is { } inventura)
        {
            for (var rok = od0.Year; rok <= po1.AddDays(-1).Year; rok++)
            {
                var den = Math.Min(inventura.Day, DateTime.DaysInMonth(rok, inventura.Month));
                var datum = new DateTime(rok, inventura.Month, den);
                if (datum >= od0 && datum < po1)
                {
                    vysledok.Add(new VyskytUdalosti
                    {
                        Kluc = $"inventura-{rok}",
                        Nazov = Preklad.T("Ročná inventúra"),
                        Popis = Preklad.T("Ročná inventúra majetku. Termín sa nastavuje v nastaveniach kalendára."),
                        Zaciatok = datum,
                        CelyDen = true,
                        Typ = Preklad.T("inventúra"),
                        Farba = Farba("inventúra", "#8E44AD"),
                        Druh = DruhUdalosti.Uloha,
                        Pripomienka = PripomienkaUdalosti.TyzdenVopred,
                        Zdroj = "inventúra",
                    });
                }
            }
        }

        return vysledok;
    }

    // ------------------------------------------------------------------ prehľad dňa

    /// <summary>Čo je dnes, na čo treba pamätať (pripomienky) a ktoré úlohy sú po termíne.</summary>
    public PrehladDna Prehlad(DateTime dnes)
    {
        dnes = dnes.Date;
        var dnesne = Vyskyty(dnes, dnes).Where(v => v.Stav != StavUdalosti.Zrusene).ToList();
        var najblizsie = Vyskyty(dnes.AddDays(1), dnes.AddDays(7))
            .Where(v => v.Stav == StavUdalosti.Naplanovane && v.Zaciatok.Date > dnes)
            .Where(v => v.Pripomienka switch
            {
                PripomienkaUdalosti.DenVopred => v.Zaciatok.Date.AddDays(-1) <= dnes,
                PripomienkaUdalosti.TyzdenVopred => v.Zaciatok.Date.AddDays(-7) <= dnes,
                _ => false,
            })
            .ToList();

        using var db = Databaza.Otvor();
        var typy = db.TypyUdalosti.AsNoTracking().ToDictionary(t => t.Id);
        var poTermine = db.Udalosti.AsNoTracking()
            .Include(u => u.Polozka).Include(u => u.Miestnost).Include(u => u.Osoba)
            .Where(u => u.Druh == DruhUdalosti.Uloha && u.Stav == StavUdalosti.Naplanovane && u.Opakovanie == OpakovanieUdalosti.Ziadne)
            .Where(u => u.Zaciatok < dnes)
            .ToList()
            .Where(u => (u.Koniec ?? u.Zaciatok).Date < dnes)
            .OrderBy(u => u.Zaciatok)
            .Select(u => new VyskytUdalosti
            {
                UdalostId = u.Id,
                Kluc = $"{u.Guid:N}",
                Nazov = u.Nazov,
                Popis = u.Popis,
                Zaciatok = u.Zaciatok,
                Koniec = u.Koniec,
                CelyDen = u.CelyDen,
                Typ = typy.TryGetValue(u.TypUdalostiId, out var t) ? t.Nazov : "",
                Farba = typy.TryGetValue(u.TypUdalostiId, out var t2) ? t2.Farba : "#7F8C8D",
                Druh = u.Druh,
                Stav = u.Stav,
                PolozkaId = u.PolozkaId,
                Naviazanie = Naviazanie(u),
            })
            .ToList();

        return new PrehladDna(dnesne, najblizsie, poTermine);
    }

    public static int CisloTyzdna(DateTime datum) => ISOWeek.GetWeekOfYear(datum);

    // ------------------------------------------------------------------ export .ics

    /// <summary>Kalendár vo formáte iCalendar (.ics), ktorý otvorí Outlook aj Google Kalendár.</summary>
    public string Ics(DateTime od, DateTime po, bool automaticke)
    {
        var sb = new StringBuilder();
        void Riadok(string text) => sb.Append(Zalom(text)).Append("\r\n");

        Riadok("BEGIN:VCALENDAR");
        Riadok("VERSION:2.0");
        Riadok("PRODID:-//EviTA//Pracovny kalendar//SK");
        Riadok("CALSCALE:GREGORIAN");
        Riadok("METHOD:PUBLISH");
        Riadok("X-WR-CALNAME:EviTA");
        var razitko = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        // udalosti z databázy – opakované ako jedna udalosť s pravidlom opakovania
        using (var db = Databaza.Otvor())
        {
            var od0 = od.Date;
            var po1 = po.Date.AddDays(1);
            var typy = db.TypyUdalosti.AsNoTracking().ToDictionary(t => t.Id);
            var udalosti = db.Udalosti.AsNoTracking()
                .Include(u => u.Polozka).Include(u => u.Miestnost).Include(u => u.Osoba)
                .Where(u => u.Zaciatok < po1)
                .ToList()
                .Where(u => Zaciatky(u, od0, po1).Any())
                .OrderBy(u => u.Zaciatok)
                .ToList();

            foreach (var u in udalosti)
            {
                Riadok("BEGIN:VEVENT");
                Riadok($"UID:{u.Guid:D}@evita");
                Riadok($"DTSTAMP:{razitko}");
                ZapisCas(Riadok, u.Zaciatok, u.Koniec, u.CelyDen);
                Riadok("SUMMARY:" + Text(u.Nazov));
                var popis = new List<string>();
                if (!string.IsNullOrWhiteSpace(u.Popis))
                {
                    popis.Add(u.Popis);
                }

                if (Naviazanie(u) is { } naviazanie)
                {
                    popis.Add(Preklad.F($"Naviazanie: {naviazanie}"));
                }

                if (popis.Count > 0)
                {
                    Riadok("DESCRIPTION:" + Text(string.Join("\n", popis)));
                }

                if (u.Miestnost is { } miestnost)
                {
                    Riadok("LOCATION:" + Text(miestnost.Nazov));
                }

                if (typy.TryGetValue(u.TypUdalostiId, out var typ))
                {
                    Riadok("CATEGORIES:" + Text(typ.Nazov));
                }

                if (u.Opakovanie != OpakovanieUdalosti.Ziadne)
                {
                    var frekvencia = u.Opakovanie switch
                    {
                        OpakovanieUdalosti.Denne => "DAILY",
                        OpakovanieUdalosti.Tyzdenne => "WEEKLY",
                        OpakovanieUdalosti.Mesacne => "MONTHLY",
                        _ => "YEARLY",
                    };
                    var pravidlo = "RRULE:FREQ=" + frekvencia;
                    if (u.OpakovanieDo is { } doDna)
                    {
                        pravidlo += ";UNTIL=" + (u.CelyDen
                            ? doDna.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                            : doDna.Date.AddDays(1).AddSeconds(-1).ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));
                    }

                    Riadok(pravidlo);
                }

                Riadok("STATUS:" + (u.Stav == StavUdalosti.Zrusene ? "CANCELLED" : "CONFIRMED"));
                if (u.Pripomienka != PripomienkaUdalosti.Ziadna)
                {
                    Riadok("BEGIN:VALARM");
                    Riadok("ACTION:DISPLAY");
                    Riadok("DESCRIPTION:" + Text(u.Nazov));
                    var spustac = u.Pripomienka switch
                    {
                        PripomienkaUdalosti.DenVopred => "-P1D",
                        PripomienkaUdalosti.TyzdenVopred => "-P7D",
                        _ => "-PT15M",
                    };
                    Riadok("TRIGGER:" + spustac);
                    Riadok("END:VALARM");
                }

                Riadok("END:VEVENT");
            }
        }

        if (automaticke)
        {
            foreach (var v in Vyskyty(od, po).Where(v => v.Automaticky))
            {
                Riadok("BEGIN:VEVENT");
                Riadok($"UID:{v.Kluc}@evita");
                Riadok($"DTSTAMP:{razitko}");
                ZapisCas(Riadok, v.Zaciatok, v.Koniec, true);
                Riadok("SUMMARY:" + Text(v.Nazov));
                if (!string.IsNullOrWhiteSpace(v.Popis))
                {
                    Riadok("DESCRIPTION:" + Text(v.Popis));
                }

                Riadok("CATEGORIES:" + Text(v.Typ));
                Riadok("TRANSP:TRANSPARENT");
                Riadok("END:VEVENT");
            }
        }

        Riadok("END:VCALENDAR");
        return sb.ToString();
    }

    private static void ZapisCas(Action<string> riadok, DateTime zaciatok, DateTime? koniec, bool celyDen)
    {
        if (celyDen)
        {
            riadok("DTSTART;VALUE=DATE:" + zaciatok.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            var posledny = (koniec ?? zaciatok).Date < zaciatok.Date ? zaciatok.Date : (koniec ?? zaciatok).Date;
            riadok("DTEND;VALUE=DATE:" + posledny.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        }
        else
        {
            riadok("DTSTART:" + zaciatok.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));
            var k = koniec is { } x && x > zaciatok ? x : zaciatok.AddHours(1);
            riadok("DTEND:" + k.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Text pre iCalendar: spätná lomka, bodkočiarka, čiarka a zalomenie riadku sa zapíšu so spätnou lomkou.</summary>
    private static string Text(string text) =>
        text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "");

    /// <summary>Riadky dlhšie ako 75 bajtov sa podľa normy zalomia (pokračovanie začína medzerou).</summary>
    private static string Zalom(string riadok)
    {
        var utf8 = Encoding.UTF8;
        if (utf8.GetByteCount(riadok) <= 75)
        {
            return riadok;
        }

        var sb = new StringBuilder();
        var bajty = 0;
        var limit = 75;
        foreach (var znak in riadok)
        {
            var dlzka = utf8.GetByteCount(znak.ToString());
            if (char.IsHighSurrogate(znak))
            {
                dlzka = 2;
            }
            else if (char.IsLowSurrogate(znak))
            {
                dlzka = 2;
            }

            if (bajty + dlzka > limit)
            {
                sb.Append("\r\n ");
                bajty = 1;
                limit = 75;
            }

            sb.Append(znak);
            bajty += dlzka;
        }

        return sb.ToString();
    }
}
