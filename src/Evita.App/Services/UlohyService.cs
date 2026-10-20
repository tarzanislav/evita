using Evita.Data;
using Evita.Models;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

/// <summary>Úloha pripravená na zobrazenie (dashboard, okno úloh).</summary>
public sealed record UlohaRiadok(
    int Id, string Text, string? Popis, DateTime? Termin, PrioritaUlohy Priorita, bool Hotovo, DateTime? DatumHotovo,
    DateTime Vytvorene, int? PolozkaId, int? PolozkaCislo, string? PolozkaNazov)
{
    public bool PoTermine => !Hotovo && Termin is { } t && t.Date < DateTime.Today;
    public bool Dnes => !Hotovo && Termin is { } t && t.Date == DateTime.Today;
    public bool Vysoka => Priorita == PrioritaUlohy.Vysoka;
    public bool Nizka => Priorita == PrioritaUlohy.Nizka;

    public string TerminText => Termin is not { } t
        ? ""
        : t.Date == DateTime.Today ? Preklad.T("dnes")
        : t.Date == DateTime.Today.AddDays(1) ? Preklad.T("zajtra")
        : t.Date == DateTime.Today.AddDays(-1) ? Preklad.T("včera")
        : Sk.KratkyDatum(t);

    public string PolozkaText => PolozkaCislo is int c ? $"{c} {PolozkaNazov}" : "";

    public bool MaPopis => !string.IsNullOrWhiteSpace(Popis);
}

/// <summary>Zoznam úloh (To Do) – samostatný, nezávislý od kalendára.</summary>
public sealed class UlohyService
{
    public static string NazovPriority(PrioritaUlohy p) => p switch
    {
        PrioritaUlohy.Vysoka => Preklad.T("vysoká"),
        PrioritaUlohy.Nizka => Preklad.T("nízka"),
        _ => Preklad.T("normálna"),
    };

    /// <summary>Úlohy zoradené: nedokončené (vysoká priorita, termín, poradie), potom dokončené (naposledy dokončené hore).</summary>
    public List<UlohaRiadok> Nacitaj(bool ajHotove, int? polozkaId = null)
    {
        using var db = Databaza.Otvor();
        var dotaz = db.Ulohy.AsNoTracking().Include(u => u.Polozka).AsQueryable();
        if (!ajHotove)
        {
            dotaz = dotaz.Where(u => !u.Hotovo);
        }

        if (polozkaId is int p)
        {
            dotaz = dotaz.Where(u => u.PolozkaId == p);
        }

        return dotaz.ToList()
            .OrderBy(u => u.Hotovo)
            .ThenByDescending(u => u.Hotovo ? u.DatumHotovo : null)
            .ThenByDescending(u => u.Priorita)
            .ThenBy(u => u.Termin ?? DateTime.MaxValue)
            .ThenBy(u => u.Poradie)
            .ThenBy(u => u.Id)
            .Select(Riadok)
            .ToList();
    }

    private static UlohaRiadok Riadok(Uloha u) => new(u.Id, u.Text, u.Popis, u.Termin, u.Priorita, u.Hotovo, u.DatumHotovo, u.Vytvorene,
        u.PolozkaId, u.Polozka?.PrirastkoveCislo, u.Polozka?.Nazov);

    public Uloha? Nacitaj(int id)
    {
        using var db = Databaza.Otvor();
        return db.Ulohy.AsNoTracking().Include(u => u.Polozka).FirstOrDefault(u => u.Id == id);
    }

    public (int Otvorene, int PoTermine, int Hotove) Pocty()
    {
        using var db = Databaza.Otvor();
        var dnes = DateTime.Today;
        return (db.Ulohy.Count(u => !u.Hotovo), db.Ulohy.Count(u => !u.Hotovo && u.Termin != null && u.Termin < dnes), db.Ulohy.Count(u => u.Hotovo));
    }

    /// <summary>Rýchle pridanie úlohy (dashboard). Vráti Id.</summary>
    public int Pridaj(string text, DateTime? termin = null, PrioritaUlohy priorita = PrioritaUlohy.Normalna, int? polozkaId = null, string? popis = null) =>
        Uloz(new Uloha { Text = text, Termin = termin, Priorita = priorita, PolozkaId = polozkaId, Popis = popis });

    /// <summary>Uloží novú (Id = 0) alebo upravenú úlohu. Vráti Id.</summary>
    public int Uloz(Uloha u)
    {
        var text = (u.Text ?? "").Trim();
        if (text.Length == 0)
        {
            throw new EvitaException(Preklad.T("Napíšte, čo treba urobiť."));
        }

        if (text.Length > 500)
        {
            throw new EvitaException(Preklad.T("Text úlohy je príliš dlhý (najviac 500 znakov)."));
        }

        using var db = Databaza.Otvor();
        if (u.PolozkaId is int p && !db.Polozky.Any(x => x.Id == p))
        {
            throw new EvitaException(Preklad.T("Položka, ku ktorej sa má úloha naviazať, už neexistuje."));
        }

        Uloha zaznam;
        if (u.Id == 0)
        {
            zaznam = new Uloha
            {
                Vytvorene = DateTime.Now,
                Poradie = (db.Ulohy.Max(x => (int?)x.Poradie) ?? 0) + 1,
            };
            db.Ulohy.Add(zaznam);
        }
        else
        {
            zaznam = db.Ulohy.Find(u.Id) ?? throw new EvitaException(Preklad.T("Úloha už neexistuje."));
        }

        zaznam.Text = text;
        zaznam.Popis = Sk.Prazdne(u.Popis);
        zaznam.Termin = u.Termin?.Date;
        zaznam.Priorita = u.Priorita;
        zaznam.PolozkaId = u.PolozkaId;
        if (zaznam.Hotovo != u.Hotovo && u.Id != 0)
        {
            zaznam.Hotovo = u.Hotovo;
            zaznam.DatumHotovo = u.Hotovo ? DateTime.Now : null;
        }

        db.SaveChanges();
        return zaznam.Id;
    }

    public void NastavHotovo(int id, bool hotovo)
    {
        using var db = Databaza.Otvor();
        var u = db.Ulohy.Find(id);
        if (u is null || u.Hotovo == hotovo)
        {
            return;
        }

        u.Hotovo = hotovo;
        u.DatumHotovo = hotovo ? DateTime.Now : null;
        db.SaveChanges();
    }

    public void Vymaz(int id)
    {
        using var db = Databaza.Otvor();
        db.Ulohy.Where(u => u.Id == id).ExecuteDelete();
    }

    /// <summary>Vymaže všetky dokončené úlohy. Vráti ich počet.</summary>
    public int VymazHotove()
    {
        using var db = Databaza.Otvor();
        return db.Ulohy.Where(u => u.Hotovo).ExecuteDelete();
    }
}
