using Evita.Data;
using Evita.Models;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

/// <summary>Kategórie, miestnosti a osoby. Používanú položku číselníka nemožno vymazať, len deaktivovať.</summary>
public sealed class CiselnikService
{
    public List<CiselnikRiadok> Nacitaj(TypCiselnika typ)
    {
        using var db = Databaza.Otvor();
        List<CiselnikRiadok> riadky;

        switch (typ)
        {
            case TypCiselnika.Kategorie:
            {
                var pocty = db.Polozky.GroupBy(p => p.KategoriaId)
                    .Select(g => new { g.Key, Pocet = g.Count() })
                    .ToDictionary(x => x.Key, x => x.Pocet);
                riadky = db.Kategorie.AsNoTracking().ToList()
                    .Select(k => new CiselnikRiadok
                    {
                        Id = k.Id,
                        Nazov = k.Nazov,
                        Aktivna = k.Aktivna,
                        Pocet = pocty.GetValueOrDefault(k.Id),
                        Pole1 = k.Nazov,
                        Ikona = IkonyKategorii.Pre(k.Ikona, k.Farba, k.Nazov),
                        IkonaKluc = k.Ikona,
                        IkonaFarba = k.Farba,
                    })
                    .ToList();
                break;
            }
            case TypCiselnika.Miestnosti:
            {
                var pocty = db.Polozky.Where(p => p.MiestnostId != null)
                    .GroupBy(p => p.MiestnostId!.Value)
                    .Select(g => new { g.Key, Pocet = g.Count() })
                    .ToDictionary(x => x.Key, x => x.Pocet);
                riadky = db.Miestnosti.AsNoTracking().ToList()
                    .Select(m => new CiselnikRiadok
                    {
                        Id = m.Id,
                        Nazov = m.Nazov,
                        Detail = m.Budova,
                        Aktivna = m.Aktivna,
                        Pocet = pocty.GetValueOrDefault(m.Id),
                        Pole1 = m.Nazov,
                        Pole2 = m.Budova,
                        Pole3 = m.Poznamka,
                    })
                    .ToList();
                break;
            }
            default:
            {
                var pocty = db.Polozky.Where(p => p.OsobaId != null)
                    .GroupBy(p => p.OsobaId!.Value)
                    .Select(g => new { g.Key, Pocet = g.Count() })
                    .ToDictionary(x => x.Key, x => x.Pocet);
                riadky = db.Osoby.AsNoTracking().ToList()
                    .OrderBy(o => o.Priezvisko, Sk.Porovnanie)
                    .ThenBy(o => o.Meno, Sk.Porovnanie)
                    .Select(o => new CiselnikRiadok
                    {
                        Id = o.Id,
                        Nazov = $"{o.Priezvisko} {o.Meno}".Trim(),
                        Detail = o.Funkcia,
                        Aktivna = o.Aktivna,
                        Pocet = pocty.GetValueOrDefault(o.Id),
                        Pole1 = o.Meno,
                        Pole2 = o.Priezvisko,
                        Pole3 = o.Funkcia,
                    })
                    .ToList();
                return riadky;
            }
        }

        return riadky.OrderBy(r => r.Nazov, Sk.Porovnanie).ToList();
    }

    /// <summary>Výberový zoznam do formulára: aktívne položky + práve použitá (aj keď je neaktívna).</summary>
    public List<Volba> Volby(TypCiselnika typ, int? vratane, string? prazdnaVolba)
    {
        var volby = Nacitaj(typ)
            .Where(r => r.Aktivna || r.Id == vratane)
            .Select(r => new Volba(r.Id, typ == TypCiselnika.Osoby ? $"{r.Pole1} {r.Pole2}".Trim() : r.Nazov));

        var vysledok = new List<Volba>();
        if (prazdnaVolba is not null)
        {
            vysledok.Add(new Volba(0, prazdnaVolba));
        }

        vysledok.AddRange(volby);
        return vysledok;
    }

    /// <summary>Uloží (pridá alebo upraví) položku číselníka. Vráti jej Id.</summary>
    public int Uloz(TypCiselnika typ, int id, string pole1, string? pole2, string? pole3)
    {
        pole1 = (pole1 ?? "").Trim();
        using var db = Databaza.Otvor();

        switch (typ)
        {
            case TypCiselnika.Kategorie:
            {
                if (pole1.Length == 0)
                {
                    throw new EvitaException(Preklad.T("Zadajte názov kategórie."));
                }

                KontrolaDuplicity(db.Kategorie.Where(k => k.Id != id).Select(k => k.Nazov).ToList(), pole1, Preklad.T("Kategória"));
                var k = id == 0 ? db.Kategorie.Add(new Kategoria()).Entity : db.Kategorie.Find(id)
                    ?? throw new EvitaException(Preklad.T("Kategória už neexistuje."));
                k.Nazov = pole1;
                db.SaveChanges();
                return k.Id;
            }
            case TypCiselnika.Miestnosti:
            {
                if (pole1.Length == 0)
                {
                    throw new EvitaException(Preklad.T("Zadajte názov miestnosti."));
                }

                KontrolaDuplicity(db.Miestnosti.Where(m => m.Id != id).Select(m => m.Nazov).ToList(), pole1, Preklad.T("Miestnosť"));
                var m = id == 0 ? db.Miestnosti.Add(new Miestnost()).Entity : db.Miestnosti.Find(id)
                    ?? throw new EvitaException(Preklad.T("Miestnosť už neexistuje."));
                m.Nazov = pole1;
                m.Budova = Sk.Prazdne(pole2);
                m.Poznamka = Sk.Prazdne(pole3);
                db.SaveChanges();
                return m.Id;
            }
            default:
            {
                var meno = pole1;
                var priezvisko = (pole2 ?? "").Trim();
                if (priezvisko.Length == 0)
                {
                    throw new EvitaException(Preklad.T("Zadajte aspoň priezvisko (alebo názov útvaru)."));
                }

                var o = id == 0 ? db.Osoby.Add(new Osoba()).Entity : db.Osoby.Find(id)
                    ?? throw new EvitaException(Preklad.T("Osoba už neexistuje."));
                o.Meno = meno;
                o.Priezvisko = priezvisko;
                o.Funkcia = Sk.Prazdne(pole3);
                db.SaveChanges();
                return o.Id;
            }
        }
    }

    public void PrepniAktivnu(TypCiselnika typ, int id)
    {
        using var db = Databaza.Otvor();
        switch (typ)
        {
            case TypCiselnika.Kategorie:
                if (db.Kategorie.Find(id) is { } k)
                {
                    k.Aktivna = !k.Aktivna;
                }

                break;
            case TypCiselnika.Miestnosti:
                if (db.Miestnosti.Find(id) is { } m)
                {
                    m.Aktivna = !m.Aktivna;
                }

                break;
            default:
                if (db.Osoby.Find(id) is { } o)
                {
                    o.Aktivna = !o.Aktivna;
                }

                break;
        }

        db.SaveChanges();
    }

    public void Vymaz(TypCiselnika typ, int id)
    {
        using var db = Databaza.Otvor();
        var pouzite = typ switch
        {
            TypCiselnika.Kategorie => db.Polozky.Any(p => p.KategoriaId == id),
            TypCiselnika.Miestnosti => db.Polozky.Any(p => p.MiestnostId == id),
            _ => db.Polozky.Any(p => p.OsobaId == id),
        };

        if (pouzite)
        {
            throw new EvitaException(Preklad.T("Túto položku číselníka používajú položky v evidencii, preto ju nemožno vymazať. Môžete ju deaktivovať."));
        }

        switch (typ)
        {
            case TypCiselnika.Kategorie:
                db.Kategorie.Where(k => k.Id == id).ExecuteDelete();
                break;
            case TypCiselnika.Miestnosti:
                db.Miestnosti.Where(m => m.Id == id).ExecuteDelete();
                break;
            default:
                db.Osoby.Where(o => o.Id == id).ExecuteDelete();
                break;
        }
    }

    private static void KontrolaDuplicity(IEnumerable<string> existujuce, string nazov, string co)
    {
        if (existujuce.Any(e => Sk.Porovnanie.Equals(e.Trim(), nazov)))
        {
            throw new EvitaException(Preklad.F($"{co} s názvom „{nazov}“ už existuje."));
        }
    }

    /// <summary>Ikona a farba kategórie (null = automaticky podľa názvu / predvolená farba ikony).</summary>
    public void NastavIkonu(int idKategorie, string? ikona, string? farba)
    {
        using var db = Databaza.Otvor();
        var kategoria = db.Kategorie.Find(idKategorie) ?? throw new EvitaException(Preklad.T("Kategória už neexistuje."));
        kategoria.Ikona = ikona is null ? null : IkonyKategorii.Podla(ikona).Kluc;
        kategoria.Farba = IkonyKategorii.PlatnaFarba(farba);
        db.SaveChanges();
    }
}
