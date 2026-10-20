using Evita.Data;
using Evita.Models;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

/// <summary>Chyba, ktorú treba ukázať používateľovi (nie programátorská chyba).</summary>
public sealed class EvitaException : Exception
{
    public EvitaException(string sprava) : base(sprava)
    {
    }
}

/// <summary>Evidencia položiek vrátane zápisu každej zmeny do histórie.</summary>
public sealed class PolozkaService
{
    public const string AkciaVytvorenie = "vytvorenie";
    public const string AkciaZmena = "zmena";
    public const string AkciaVyradenie = "vyradenie";
    public const string AkciaObnovenie = "obnovenie";
    public const string AkciaVymazanie = "vymazanie";

    private static string Pouzivatel => Environment.UserName;

    public List<PolozkaRiadok> NacitajRiadky()
    {
        using var db = Databaza.Otvor();
        var riadky = db.Polozky.AsNoTracking()
            .Select(p => new PolozkaRiadok
            {
                Id = p.Id,
                PrirastkoveCislo = p.PrirastkoveCislo,
                Nazov = p.Nazov,
                KategoriaId = p.KategoriaId,
                Kategoria = p.Kategoria!.Nazov,
                MiestnostId = p.MiestnostId,
                Miestnost = p.Miestnost != null ? p.Miestnost.Nazov : null,
                OsobaId = p.OsobaId,
                Osoba = p.Osoba != null ? p.Osoba.Meno + " " + p.Osoba.Priezvisko : null,
                Vyrobca = p.Vyrobca,
                Model = p.Model,
                SerioveCislo = p.SerioveCislo,
                InventarneCislo = p.InventarneCislo,
                StavHodnota = p.Stav,
                ZarukaDo = p.ZarukaDo,
                DalsiaRevizia = p.DalsiaRevizia,
                DatumPridania = p.DatumPridania,
                DatumZmeny = p.DatumZmeny,
                Poznamka = p.Poznamka,
            })
            .ToList();

        foreach (var r in riadky)
        {
            r.Hladanie = Sk.BezDiakritiky(string.Join(' ',
                r.PrirastkoveCislo, r.Nazov, r.Kategoria, r.Miestnost, r.Osoba, r.Vyrobca, r.Model,
                r.SerioveCislo, r.InventarneCislo, r.Poznamka));
        }

        return riadky;
    }

    public Polozka? Nacitaj(int id)
    {
        using var db = Databaza.Otvor();
        return db.Polozky.AsNoTracking().FirstOrDefault(p => p.Id == id);
    }

    public List<HistoriaZmeny> Historia(int polozkaId)
    {
        using var db = Databaza.Otvor();
        return db.Historia.AsNoTracking()
            .Where(h => h.PolozkaId == polozkaId)
            .OrderByDescending(h => h.Cas)
            .ThenByDescending(h => h.Id)
            .ToList();
    }

    /// <summary>
    /// Pridá jednu alebo viac rovnakých položiek s po sebe idúcimi prírastkovými číslami.
    /// Vráti Id nových položiek.
    /// </summary>
    public List<int> Pridaj(Polozka vzor, int pocet, IReadOnlyList<string> serioveCisla)
    {
        if (pocet < 1 || pocet > 500)
        {
            throw new EvitaException("Počet kusov musí byť od 1 do 500.");
        }

        using var db = Databaza.Otvor();
        using var transakcia = db.Database.BeginTransaction();

        var prveCislo = Databaza.DalsiePrirastkoveCislo(db);
        var teraz = DateTime.Now;
        var nove = new List<Polozka>();

        for (var i = 0; i < pocet; i++)
        {
            var p = new Polozka();
            Prenes(vzor, p);
            p.Guid = Guid.NewGuid();
            p.PrirastkoveCislo = prveCislo + i;
            p.DatumPridania = teraz;
            p.DatumZmeny = teraz;
            p.Verzia = 1;
            if (i < serioveCisla.Count && !string.IsNullOrWhiteSpace(serioveCisla[i]))
            {
                p.SerioveCislo = serioveCisla[i].Trim();
            }
            else if (pocet > 1)
            {
                // sériové číslo je jedinečné – pri hromadnom pridaní ho nekopírujeme na všetky kusy
                p.SerioveCislo = null;
            }

            db.Polozky.Add(p);
            nove.Add(p);
        }

        db.SaveChanges();

        foreach (var p in nove)
        {
            db.Historia.Add(new HistoriaZmeny
            {
                PolozkaId = p.Id,
                PrirastkoveCislo = p.PrirastkoveCislo,
                NazovPolozky = p.Nazov,
                Cas = teraz,
                Pouzivatel = Pouzivatel,
                Akcia = AkciaVytvorenie,
                NovaHodnota = pocet > 1 ? $"hromadné pridanie {pocet} ks" : null,
            });
        }

        Databaza.Zapis(db, Databaza.KlucPoslednePrirastkove, (prveCislo + pocet - 1).ToString());
        db.SaveChanges();
        transakcia.Commit();

        return nove.Select(p => p.Id).ToList();
    }

    /// <summary>Uloží úpravu jednej položky a každé zmenené pole zapíše do histórie.</summary>
    public void Uloz(Polozka upravena)
    {
        using var db = Databaza.Otvor();
        var p = db.Polozky.FirstOrDefault(x => x.Id == upravena.Id)
            ?? throw new EvitaException("Položka medzičasom zanikla (bola vymazaná).");

        if (p.Verzia != upravena.Verzia)
        {
            throw new EvitaException(
                "Položku medzičasom zmenil niekto iný. Zatvorte okno, obnovte zoznam a urobte úpravu znova.");
        }

        var pred = Popis(db, p);
        Prenes(upravena, p);
        var teraz = DateTime.Now;
        if (ZapisZmeny(db, p, pred, AkciaZmena, teraz))
        {
            db.SaveChanges();
        }
    }

    /// <summary>Spoločná úprava viacerých položiek naraz (presun, pridelenie, stav, vyradenie…).</summary>
    public int HromadnaZmena(IEnumerable<int> idPoloziek, Action<Polozka> uprava, string akcia = AkciaZmena)
    {
        var idList = idPoloziek.Distinct().ToList();
        if (idList.Count == 0)
        {
            return 0;
        }

        using var db = Databaza.Otvor();
        using var transakcia = db.Database.BeginTransaction();
        var teraz = DateTime.Now;
        var zmenene = 0;

        foreach (var p in db.Polozky.Where(x => idList.Contains(x.Id)).ToList())
        {
            var pred = Popis(db, p);
            uprava(p);
            if (ZapisZmeny(db, p, pred, akcia, teraz))
            {
                zmenene++;
            }
        }

        db.SaveChanges();
        transakcia.Commit();
        return zmenene;
    }

    public int Vyrad(IEnumerable<int> idPoloziek, DateTime datum, string? dovod) =>
        HromadnaZmena(idPoloziek, p =>
        {
            p.Stav = StavPolozky.Vyradene;
            p.DatumVyradenia = datum.Date;
            p.DovodVyradenia = Sk.Prazdne(dovod);
        }, AkciaVyradenie);

    public int Obnov(IEnumerable<int> idPoloziek) =>
        HromadnaZmena(idPoloziek, p =>
        {
            if (p.Stav == StavPolozky.Vyradene)
            {
                p.Stav = StavPolozky.VPrevadzke;
                p.DatumVyradenia = null;
                p.DovodVyradenia = null;
            }
        }, AkciaObnovenie);

    /// <summary>Vymaže položky. Do histórie sa zapíše, čo bolo vymazané; prírastkové čísla sa už nepoužijú.</summary>
    public int Vymaz(IEnumerable<int> idPoloziek)
    {
        var idList = idPoloziek.Distinct().ToList();
        using var db = Databaza.Otvor();
        using var transakcia = db.Database.BeginTransaction();
        var teraz = DateTime.Now;
        var polozky = db.Polozky.Where(x => idList.Contains(x.Id)).ToList();

        foreach (var p in polozky)
        {
            db.Historia.Add(new HistoriaZmeny
            {
                PolozkaId = p.Id,
                PrirastkoveCislo = p.PrirastkoveCislo,
                NazovPolozky = p.Nazov,
                Cas = teraz,
                Pouzivatel = Pouzivatel,
                Akcia = AkciaVymazanie,
                StaraHodnota = $"{p.PrirastkoveCislo} – {p.Nazov}",
            });
            db.Polozky.Remove(p);
        }

        // zapamätať najvyššie číslo, aby sa po vymazaní posledných položiek znova nepoužilo
        var dalsie = Databaza.DalsiePrirastkoveCislo(db);
        Databaza.Zapis(db, Databaza.KlucPoslednePrirastkove, (dalsie - 1).ToString());

        db.SaveChanges();
        transakcia.Commit();
        return polozky.Count;
    }

    /// <summary>Prenesie upraviteľné polia (nie Id, číslo, dátumy pridania a verziu).</summary>
    public static void Prenes(Polozka z, Polozka na)
    {
        na.Nazov = z.Nazov.Trim();
        na.KategoriaId = z.KategoriaId;
        na.MiestnostId = z.MiestnostId;
        na.OsobaId = z.OsobaId;
        na.Vyrobca = Sk.Prazdne(z.Vyrobca);
        na.Model = Sk.Prazdne(z.Model);
        na.SerioveCislo = Sk.Prazdne(z.SerioveCislo);
        na.InventarneCislo = Sk.Prazdne(z.InventarneCislo);
        na.Stav = z.Stav;
        na.DatumNakupu = z.DatumNakupu?.Date;
        na.Cena = z.Cena;
        na.ZarukaDo = z.ZarukaDo?.Date;
        na.DalsiaRevizia = z.DalsiaRevizia?.Date;
        na.DatumVyradenia = z.Stav == StavPolozky.Vyradene ? (z.DatumVyradenia ?? DateTime.Today).Date : null;
        na.DovodVyradenia = z.Stav == StavPolozky.Vyradene ? Sk.Prazdne(z.DovodVyradenia) : null;
        na.Poznamka = string.IsNullOrWhiteSpace(z.Poznamka) ? null : z.Poznamka.TrimEnd();
    }

    /// <summary>Porovná stav pred a po úprave a každé zmenené pole zapíše do histórie.</summary>
    private static bool ZapisZmeny(EvitaDbContext db, Polozka p, Dictionary<string, string> pred, string akcia, DateTime cas)
    {
        var po = Popis(db, p);
        var zmena = false;

        foreach (var (pole, novaHodnota) in po)
        {
            var staraHodnota = pred.TryGetValue(pole, out var s) ? s : "";
            if (staraHodnota == novaHodnota)
            {
                continue;
            }

            var akciaRiadku = akcia;
            if (pole == "Stav")
            {
                if (p.Stav == StavPolozky.Vyradene)
                {
                    akciaRiadku = AkciaVyradenie;
                }
                else if (staraHodnota == StavyPoloziek.Nazov(StavPolozky.Vyradene))
                {
                    akciaRiadku = AkciaObnovenie;
                }
            }

            db.Historia.Add(new HistoriaZmeny
            {
                PolozkaId = p.Id,
                PrirastkoveCislo = p.PrirastkoveCislo,
                NazovPolozky = p.Nazov,
                Cas = cas,
                Pouzivatel = Pouzivatel,
                Akcia = akciaRiadku,
                Pole = pole,
                StaraHodnota = staraHodnota,
                NovaHodnota = novaHodnota,
            });
            zmena = true;
        }

        if (zmena)
        {
            p.DatumZmeny = cas;
            p.Verzia++;
        }

        return zmena;
    }

    /// <summary>Čitateľné hodnoty polí položky – tak, ako sa zapisujú do histórie.</summary>
    private static Dictionary<string, string> Popis(EvitaDbContext db, Polozka p) => new()
    {
        ["Názov"] = p.Nazov,
        ["Kategória"] = NazovKategorie(db, p.KategoriaId),
        ["Miestnosť"] = NazovMiestnosti(db, p.MiestnostId),
        ["Pridelené"] = MenoOsoby(db, p.OsobaId),
        ["Výrobca"] = p.Vyrobca ?? "",
        ["Model"] = p.Model ?? "",
        ["Sériové číslo"] = p.SerioveCislo ?? "",
        ["Inventárne číslo"] = p.InventarneCislo ?? "",
        ["Stav"] = StavyPoloziek.Nazov(p.Stav),
        ["Dátum nákupu"] = Sk.Datum(p.DatumNakupu),
        ["Cena"] = Sk.Suma(p.Cena),
        ["Záruka do"] = Sk.Datum(p.ZarukaDo),
        ["Ďalšia revízia"] = Sk.Datum(p.DalsiaRevizia),
        ["Dátum vyradenia"] = Sk.Datum(p.DatumVyradenia),
        ["Dôvod vyradenia"] = p.DovodVyradenia ?? "",
        ["Poznámka"] = p.Poznamka ?? "",
    };

    private static string NazovKategorie(EvitaDbContext db, int id) =>
        db.Kategorie.Where(k => k.Id == id).Select(k => k.Nazov).FirstOrDefault() ?? "";

    private static string NazovMiestnosti(EvitaDbContext db, int? id) =>
        id is null ? "" : db.Miestnosti.Where(m => m.Id == id).Select(m => m.Nazov).FirstOrDefault() ?? "";

    private static string MenoOsoby(EvitaDbContext db, int? id) =>
        id is null ? "" : db.Osoby.Where(o => o.Id == id).Select(o => o.Meno + " " + o.Priezvisko).FirstOrDefault() ?? "";
}
