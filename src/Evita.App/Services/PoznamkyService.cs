using Evita.Data;
using Evita.Models;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

/// <summary>Poznámka pripravená na zobrazenie v zozname.</summary>
public sealed record PoznamkaRiadok(
    int Id, string Nazov, string Text, string Farba, bool Pripnuta, DateTime Vytvorene, DateTime Zmenene, DateTime? Datum,
    int? PolozkaId, int? PolozkaCislo, string? PolozkaNazov, int? UdalostId, string? UdalostNazov, DateTime? UdalostZaciatok)
{
    /// <summary>Prvé riadky textu bez prázdnych riadkov (náhľad v zozname).</summary>
    public string Nahlad
    {
        get
        {
            var riadky = Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var nahlad = string.Join(" · ", riadky.Take(3));
            return nahlad.Length > 160 ? nahlad[..160] + "…" : nahlad;
        }
    }

    public string ZmeneneText => Sk.DatumCas(Zmenene);
    public bool MaPolozku => PolozkaId is not null;
    public bool MaUdalost => UdalostId is not null;
    public bool MaDatum => Datum is not null;
    public string PolozkaText => PolozkaCislo is int c ? $"{c} {PolozkaNazov}" : "";
    public string UdalostText => UdalostNazov is null ? "" : $"{Sk.KratkyDatum(UdalostZaciatok)} {UdalostNazov}";
    public string DatumText => Sk.Datum(Datum);
}

/// <summary>Poznámkový blok – poznámky prepojené s položkami, dňami v kalendári a udalosťami.</summary>
public sealed class PoznamkyService
{
    /// <summary>Farby lístkov (jemné, dobre čitateľné s tmavým textom).</summary>
    public static readonly string[] Farby =
    {
        "#FFF59D", "#FFCC80", "#F8BBD0", "#CE93D8", "#90CAF9", "#80DEEA", "#A5D6A7", "#E0E0E0",
    };

    public static bool JePlatnaFarba(string? farba) =>
        farba is { Length: 7 } && farba[0] == '#' && farba.Skip(1).All(Uri.IsHexDigit);

    /// <summary>Názov z prvého riadku textu (ak používateľ názov nezadal).</summary>
    public static string NazovZTextu(string? text)
    {
        var prvy = (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        if (prvy.Length == 0)
        {
            return Preklad.T("Poznámka");
        }

        return prvy.Length > 60 ? prvy[..60].TrimEnd() + "…" : prvy;
    }

    private static IQueryable<Poznamka> Dotaz(EvitaDbContext db) =>
        db.Poznamky.AsNoTracking().Include(p => p.Polozka).Include(p => p.Udalost);

    private static PoznamkaRiadok Riadok(Poznamka p) => new(
        p.Id, p.Nazov, p.Text, p.Farba, p.Pripnuta, p.Vytvorene, p.Zmenene, p.Datum,
        p.PolozkaId, p.Polozka?.PrirastkoveCislo, p.Polozka?.Nazov, p.UdalostId, p.Udalost?.Nazov, p.Udalost?.Zaciatok);

    /// <summary>Všetky poznámky (pripnuté hore, potom naposledy zmenené), voliteľne len tie, ktoré obsahujú hľadaný text.</summary>
    public List<PoznamkaRiadok> Zoznam(string? hladat = null)
    {
        using var db = Databaza.Otvor();
        var vsetky = Dotaz(db).ToList().Select(Riadok);
        if (!string.IsNullOrWhiteSpace(hladat))
        {
            var slova = Sk.BezDiakritiky(hladat).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            vsetky = vsetky.Where(p =>
            {
                var text = Sk.BezDiakritiky($"{p.Nazov} {p.Text} {p.PolozkaText} {p.UdalostNazov}");
                return slova.All(text.Contains);
            });
        }

        return vsetky.OrderByDescending(p => p.Pripnuta).ThenByDescending(p => p.Zmenene).ToList();
    }

    public List<PoznamkaRiadok> PrePolozku(int polozkaId)
    {
        using var db = Databaza.Otvor();
        return Dotaz(db).Where(p => p.PolozkaId == polozkaId).ToList().Select(Riadok)
            .OrderByDescending(p => p.Pripnuta).ThenByDescending(p => p.Zmenene).ToList();
    }

    public List<PoznamkaRiadok> PreUdalost(int udalostId)
    {
        using var db = Databaza.Otvor();
        return Dotaz(db).Where(p => p.UdalostId == udalostId).ToList().Select(Riadok)
            .OrderByDescending(p => p.Zmenene).ToList();
    }

    /// <summary>Poznámky naviazané na deň v kalendári (od – po vrátane).</summary>
    public List<PoznamkaRiadok> PreDni(DateTime od, DateTime po)
    {
        using var db = Databaza.Otvor();
        var zaciatok = od.Date;
        var koniec = po.Date.AddDays(1);
        return Dotaz(db).Where(p => p.Datum != null && p.Datum >= zaciatok && p.Datum < koniec).ToList().Select(Riadok)
            .OrderBy(p => p.Datum).ThenBy(p => p.Vytvorene).ToList();
    }

    public List<PoznamkaRiadok> Pripnute(int pocet)
    {
        using var db = Databaza.Otvor();
        return Dotaz(db).Where(p => p.Pripnuta).OrderByDescending(p => p.Zmenene).Take(pocet).ToList().Select(Riadok).ToList();
    }

    public int Pocet()
    {
        using var db = Databaza.Otvor();
        return db.Poznamky.Count();
    }

    public Poznamka? Nacitaj(int id)
    {
        using var db = Databaza.Otvor();
        return Dotaz(db).FirstOrDefault(p => p.Id == id);
    }

    /// <summary>Uloží novú (Id = 0) alebo upravenú poznámku. Vráti Id.</summary>
    public int Uloz(Poznamka p)
    {
        var text = (p.Text ?? "").Replace("\r\n", "\n").TrimEnd();
        var nazov = (p.Nazov ?? "").Trim();
        if (nazov.Length == 0 && text.Trim().Length == 0)
        {
            throw new EvitaException(Preklad.T("Poznámka je prázdna – napíšte názov alebo text."));
        }

        if (nazov.Length == 0)
        {
            nazov = NazovZTextu(text);
        }

        if (nazov.Length > 200)
        {
            throw new EvitaException(Preklad.T("Názov poznámky je príliš dlhý (najviac 200 znakov)."));
        }

        using var db = Databaza.Otvor();
        if (p.PolozkaId is int pid && !db.Polozky.Any(x => x.Id == pid))
        {
            throw new EvitaException(Preklad.T("Položka, ku ktorej sa má poznámka naviazať, už neexistuje."));
        }

        if (p.UdalostId is int uid && !db.Udalosti.Any(x => x.Id == uid))
        {
            throw new EvitaException(Preklad.T("Udalosť, ku ktorej sa má poznámka naviazať, už neexistuje."));
        }

        Poznamka zaznam;
        var teraz = DateTime.Now;
        if (p.Id == 0)
        {
            zaznam = new Poznamka { Vytvorene = teraz };
            db.Poznamky.Add(zaznam);
        }
        else
        {
            zaznam = db.Poznamky.Find(p.Id) ?? throw new EvitaException(Preklad.T("Poznámka už neexistuje."));
        }

        zaznam.Nazov = nazov;
        zaznam.Text = text.Replace("\n", Environment.NewLine);
        zaznam.Farba = JePlatnaFarba(p.Farba) ? p.Farba.ToUpperInvariant() : Farby[0];
        zaznam.Pripnuta = p.Pripnuta;
        zaznam.Datum = p.Datum?.Date;
        zaznam.PolozkaId = p.PolozkaId;
        zaznam.UdalostId = p.UdalostId;
        zaznam.Zmenene = teraz;
        db.SaveChanges();
        return zaznam.Id;
    }

    public void NastavPripnutie(int id, bool pripnuta)
    {
        using var db = Databaza.Otvor();
        var p = db.Poznamky.Find(id);
        if (p is null)
        {
            return;
        }

        p.Pripnuta = pripnuta;
        db.SaveChanges();
    }

    public void Vymaz(int id)
    {
        using var db = Databaza.Otvor();
        db.Poznamky.Where(p => p.Id == id).ExecuteDelete();
    }
}
