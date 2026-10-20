using Evita.Data;
using Evita.Models;
using MigraDoc.DocumentObjectModel;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

/// <summary>Inventúra v zozname (obrazovka Skenovanie).</summary>
public sealed record InventuraRiadok(int Id, string Nazov, DateTime Zaciatok, DateTime? Koniec, StavInventury Stav, int Najdene, int Celkom)
{
    public bool Prebieha => Stav == StavInventury.Prebieha;
    public string StavText => Prebieha ? Preklad.T("prebieha") : Preklad.T("ukončená");
    public string ObdobieText => Koniec is { } k ? $"{Sk.Datum(Zaciatok)} – {Sk.Datum(k)}" : Preklad.F($"od {Sk.Datum(Zaciatok)}");
    public string PostupText => Preklad.F($"{Najdene} z {Celkom} položiek");
    public double Percento => Celkom == 0 ? 0 : 100.0 * Najdene / Celkom;
}

/// <summary>Položka evidencie a jej stav v inventúre.</summary>
public sealed class PolozkaInventury
{
    public int Id { get; init; }
    public int PrirastkoveCislo { get; init; }
    public string Nazov { get; init; } = "";
    public string Kategoria { get; init; } = "";
    public int? MiestnostId { get; init; }
    public string? Miestnost { get; init; }
    public string? Osoba { get; init; }
    public string? Inventarne { get; init; }
    public string? Seriove { get; init; }
    public bool Vyradena { get; init; }

    /// <summary>Kde bola položka pri inventúre nájdená (null = zatiaľ nenájdená).</summary>
    public int? NajdenaV { get; set; }
    public string? NajdenaVNazov { get; set; }
    public bool Najdena { get; set; }
    public DateTime? Cas { get; set; }
    public string? Sposob { get; set; }

    /// <summary>Nájdená v inej miestnosti, než je zapísaná v evidencii.</summary>
    public bool Inde => Najdena && NajdenaV != MiestnostId;

    public string Popis => $"{PrirastkoveCislo} – {Nazov}";

    public string StavText => !Najdena
        ? Preklad.T("chýba")
        : Inde ? Preklad.F($"nájdená v {NajdenaVNazov ?? Preklad.T("(nezadaná)")}") : Preklad.T("nájdená");
}

/// <summary>Jedno naskenovanie v inventúre (zoznam v okne inventúry).</summary>
public sealed record ZaznamInventury(int Id, DateTime Cas, string Kod, int? PolozkaId, string? Polozka, int? MiestnostId, string? Miestnost, string Sposob)
{
    public string CasText => Sk.Cas(Cas);
    public string PolozkaText => Polozka ?? Preklad.T("neznámy kód");
    public string SposobText => Preklad.TAkJe(Sposob);
}

public enum VysledokZaznamu
{
    Najdena,
    NajdenaInde,
    UzNaskenovana,
    Vyradena,
    Neznamy,
    Nejednoznacny,
    BezMiestnosti,
}

/// <summary>Čo sa stalo po naskenovaní kódu v inventúre.</summary>
public sealed record VysledokInventury(VysledokZaznamu Druh, NajdenaPolozka? Polozka, string Text)
{
    public bool Uspech => Druh is VysledokZaznamu.Najdena or VysledokZaznamu.NajdenaInde or VysledokZaznamu.UzNaskenovana;
}

/// <summary>Inventúra skenovaním: položky sa po miestnostiach označia ako nájdené, zvyšok chýba.</summary>
public sealed class InventuraService
{
    public const string SposobCitacka = "čítačka";
    public const string SposobKamera = "kamera";
    public const string SposobMobil = "mobil";
    public const string SposobRucne = "ručne";

    /// <summary>Spôsoby sa ukladajú po slovensky a zobrazujú preložené (tu kvôli zoznamu textov na preklad).</summary>
    internal static readonly string[] TextySposobov = { Preklad.N("čítačka"), Preklad.N("kamera"), Preklad.N("mobil"), Preklad.N("ručne") };

    private readonly SkenovanieService _skenovanie = new();

    private static string Pouzivatel => Environment.UserName;

    public static string Sposob(ZdrojSkenu zdroj) => zdroj switch
    {
        ZdrojSkenu.Kamera => SposobKamera,
        ZdrojSkenu.Mobil => SposobMobil,
        ZdrojSkenu.Klavesnica => SposobRucne,
        _ => SposobCitacka,
    };

    public List<InventuraRiadok> Zoznam()
    {
        using var db = Databaza.Otvor();
        var celkom = db.Polozky.Count(p => p.Stav != StavPolozky.Vyradene);
        var inventury = db.Inventury.AsNoTracking().OrderByDescending(i => i.Zaciatok).ToList();
        var najdene = db.InventuraZaznamy.AsNoTracking()
            .Where(z => z.PolozkaId != null && z.Polozka!.Stav != StavPolozky.Vyradene)
            .Select(z => new { z.InventuraId, z.PolozkaId })
            .ToList()
            .GroupBy(z => z.InventuraId)
            .ToDictionary(g => g.Key, g => g.Select(z => z.PolozkaId).Distinct().Count());
        return inventury
            .Select(i => new InventuraRiadok(i.Id, i.Nazov, i.Zaciatok, i.Koniec, i.Stav, najdene.GetValueOrDefault(i.Id), celkom))
            .ToList();
    }

    public Inventura? Nacitaj(int id)
    {
        using var db = Databaza.Otvor();
        return db.Inventury.AsNoTracking().FirstOrDefault(i => i.Id == id);
    }

    public int Nova(string nazov)
    {
        if (string.IsNullOrWhiteSpace(nazov))
        {
            throw new EvitaException(Preklad.T("Zadajte názov inventúry."));
        }

        using var db = Databaza.Otvor();
        var inventura = new Inventura { Nazov = nazov.Trim(), Zaciatok = DateTime.Now };
        db.Inventury.Add(inventura);
        db.SaveChanges();
        return inventura.Id;
    }

    public static string PredvolenyNazov() => Preklad.F($"Inventúra {DateTime.Today.Year}");

    public void NastavMiestnost(int inventuraId, int? miestnostId)
    {
        using var db = Databaza.Otvor();
        if (db.Inventury.Find(inventuraId) is { } inventura)
        {
            inventura.AktualnaMiestnostId = miestnostId;
            db.SaveChanges();
        }
    }

    public void Ukonci(int inventuraId)
    {
        using var db = Databaza.Otvor();
        if (db.Inventury.Find(inventuraId) is { } inventura)
        {
            inventura.Stav = StavInventury.Ukoncena;
            inventura.Koniec = DateTime.Now;
            db.SaveChanges();
        }
    }

    public void ZnovaOtvor(int inventuraId)
    {
        using var db = Databaza.Otvor();
        if (db.Inventury.Find(inventuraId) is { } inventura)
        {
            inventura.Stav = StavInventury.Prebieha;
            inventura.Koniec = null;
            db.SaveChanges();
        }
    }

    public void Vymaz(int inventuraId)
    {
        using var db = Databaza.Otvor();
        db.InventuraZaznamy.Where(z => z.InventuraId == inventuraId).ExecuteDelete();
        db.Inventury.Where(i => i.Id == inventuraId).ExecuteDelete();
    }

    /// <summary>Spracuje naskenovaný kód v miestnosti, kde sa práve robí inventúra.</summary>
    public VysledokInventury Zaznamenaj(int inventuraId, Sken sken, int? miestnostId)
    {
        var najdene = _skenovanie.Najdi(sken);
        using var db = Databaza.Otvor();
        var inventura = db.Inventury.Find(inventuraId) ?? throw new EvitaException(Preklad.T("Inventúra už neexistuje."));
        if (inventura.Stav != StavInventury.Prebieha)
        {
            throw new EvitaException(Preklad.T("Inventúra je ukončená. Ak chcete pokračovať, otvorte ju znova."));
        }

        if (!najdene.Najdene)
        {
            var kod = najdene.PouzityKod;
            if (!db.InventuraZaznamy.Any(z => z.InventuraId == inventuraId && z.PolozkaId == null && z.Kod == kod && z.MiestnostId == miestnostId))
            {
                Pridaj(db, inventuraId, null, kod, miestnostId, null, sken.Zdroj);
            }

            return new VysledokInventury(VysledokZaznamu.Neznamy, null, najdene.Popis);
        }

        // rovnaký EAN môže mať viac položiek – v inventúre treba jednoznačný kód
        var kandidati = najdene.Polozky.Where(p => !p.Vyradena).ToList();
        if (kandidati.Count == 0)
        {
            kandidati = najdene.Polozky.ToList();
        }

        if (kandidati.Count > 1)
        {
            var nenaskenovane = kandidati
                .Where(p => !db.InventuraZaznamy.Any(z => z.InventuraId == inventuraId && z.PolozkaId == p.Id))
                .ToList();
            if (nenaskenovane.Count == 1 && najdene.Druh == DruhZhody.Ean)
            {
                kandidati = nenaskenovane;
            }
            else
            {
                return new VysledokInventury(VysledokZaznamu.Nejednoznacny, null,
                    Preklad.F($"Kód {najdene.PouzityKod} majú {kandidati.Count} položky ({najdene.DruhText}). Naskenujte štítok EviTA alebo sériové číslo, prípadne položku označte ručne."));
            }
        }

        var polozka = kandidati[0];
        return Zapis(db, inventuraId, polozka, najdene.PouzityKod, miestnostId, sken.Zdroj);
    }

    /// <summary>Ručné označenie položky ako nájdenej (položka bez štítku, poškodený kód).</summary>
    public VysledokInventury OznacRucne(int inventuraId, int polozkaId, int? miestnostId)
    {
        var polozka = SkenovanieService.NacitajPolozku(polozkaId) ?? throw new EvitaException(Preklad.T("Položka už neexistuje."));
        using var db = Databaza.Otvor();
        return Zapis(db, inventuraId, polozka, polozka.PrirastkoveCislo.ToString(Sk.Kultura), miestnostId, ZdrojSkenu.Klavesnica);
    }

    private static VysledokInventury Zapis(EvitaDbContext db, int inventuraId, NajdenaPolozka polozka, string kod, int? miestnostId, ZdrojSkenu zdroj)
    {
        var predtym = db.InventuraZaznamy.AsNoTracking()
            .Where(z => z.InventuraId == inventuraId && z.PolozkaId == polozka.Id)
            .OrderByDescending(z => z.Cas)
            .FirstOrDefault();
        if (predtym is not null && predtym.MiestnostId == miestnostId)
        {
            return new VysledokInventury(VysledokZaznamu.UzNaskenovana, polozka,
                Preklad.F($"{polozka.Popis} – už je naskenovaná ({Sk.Cas(predtym.Cas)})."));
        }

        Pridaj(db, inventuraId, polozka.Id, kod, miestnostId, polozka.MiestnostId, zdroj);

        if (polozka.Vyradena)
        {
            return new VysledokInventury(VysledokZaznamu.Vyradena, polozka, Preklad.F($"{polozka.Popis} – položka je vyradená!"));
        }

        if (miestnostId is null)
        {
            return new VysledokInventury(VysledokZaznamu.BezMiestnosti, polozka, Preklad.F($"{polozka.Popis} – nájdená (vyberte miestnosť)."));
        }

        return polozka.MiestnostId == miestnostId
            ? new VysledokInventury(VysledokZaznamu.Najdena, polozka, Preklad.F($"{polozka.Popis} – nájdená."))
            : new VysledokInventury(VysledokZaznamu.NajdenaInde, polozka,
                Preklad.F($"{polozka.Popis} – nájdená tu, v evidencii je {polozka.Miestnost ?? Preklad.T("bez miestnosti")}."));
    }

    private static void Pridaj(EvitaDbContext db, int inventuraId, int? polozkaId, string kod, int? miestnostId, int? evidovana, ZdrojSkenu zdroj)
    {
        db.InventuraZaznamy.Add(new InventuraZaznam
        {
            InventuraId = inventuraId,
            PolozkaId = polozkaId,
            Kod = kod,
            MiestnostId = miestnostId,
            EvidovanaMiestnostId = evidovana,
            Cas = DateTime.Now,
            Sposob = Sposob(zdroj),
            Pouzivatel = Pouzivatel,
        });
        db.SaveChanges();
    }

    /// <summary>Zruší naskenovanie (omyl).</summary>
    public void ZrusZaznam(int zaznamId)
    {
        using var db = Databaza.Otvor();
        db.InventuraZaznamy.Where(z => z.Id == zaznamId).ExecuteDelete();
    }

    /// <summary>Zruší všetky naskenovania položky v inventúre (ručné odznačenie).</summary>
    public void ZrusPolozku(int inventuraId, int polozkaId)
    {
        using var db = Databaza.Otvor();
        db.InventuraZaznamy.Where(z => z.InventuraId == inventuraId && z.PolozkaId == polozkaId).ExecuteDelete();
    }

    /// <summary>Všetky položky v prevádzke (aj nájdené vyradené) so stavom v inventúre.</summary>
    public List<PolozkaInventury> Polozky(int inventuraId)
    {
        using var db = Databaza.Otvor();
        var miestnosti = db.Miestnosti.AsNoTracking().ToDictionary(m => m.Id, m => m.Nazov);
        var zaznamy = db.InventuraZaznamy.AsNoTracking()
            .Where(z => z.InventuraId == inventuraId && z.PolozkaId != null)
            .OrderBy(z => z.Cas)
            .ToList()
            .GroupBy(z => z.PolozkaId!.Value)
            .ToDictionary(g => g.Key, g => g.Last());

        var naskenovane = zaznamy.Keys.ToList();
        var polozky = db.Polozky.AsNoTracking()
            .Where(p => p.Stav != StavPolozky.Vyradene || naskenovane.Contains(p.Id))
            .OrderBy(p => p.PrirastkoveCislo)
            .Select(p => new PolozkaInventury
            {
                Id = p.Id,
                PrirastkoveCislo = p.PrirastkoveCislo,
                Nazov = p.Nazov,
                Kategoria = p.Kategoria!.Nazov,
                MiestnostId = p.MiestnostId,
                Miestnost = p.Miestnost != null ? p.Miestnost.Nazov : null,
                Osoba = p.Osoba != null ? p.Osoba.Meno + " " + p.Osoba.Priezvisko : null,
                Inventarne = p.InventarneCislo,
                Seriove = p.SerioveCislo,
                Vyradena = p.Stav == StavPolozky.Vyradene,
            })
            .ToList();

        foreach (var p in polozky)
        {
            if (zaznamy.TryGetValue(p.Id, out var z))
            {
                p.Najdena = true;
                p.NajdenaV = z.MiestnostId;
                p.NajdenaVNazov = z.MiestnostId is int m && miestnosti.TryGetValue(m, out var nazov) ? nazov : null;
                p.Cas = z.Cas;
                p.Sposob = z.Sposob;
            }
        }

        return polozky;
    }

    /// <summary>Záznamy skenovania, najnovšie prvé.</summary>
    public List<ZaznamInventury> Zaznamy(int inventuraId, int pocet = 500)
    {
        using var db = Databaza.Otvor();
        var miestnosti = db.Miestnosti.AsNoTracking().ToDictionary(m => m.Id, m => m.Nazov);
        return db.InventuraZaznamy.AsNoTracking()
            .Where(z => z.InventuraId == inventuraId)
            .OrderByDescending(z => z.Cas)
            .ThenByDescending(z => z.Id)
            .Take(pocet)
            .Select(z => new { z.Id, z.Cas, z.Kod, z.PolozkaId, Polozka = z.Polozka != null ? z.Polozka.PrirastkoveCislo + " – " + z.Polozka.Nazov : null, z.MiestnostId, z.Sposob })
            .ToList()
            .Select(z => new ZaznamInventury(z.Id, z.Cas, z.Kod, z.PolozkaId, z.Polozka, z.MiestnostId,
                z.MiestnostId is int m && miestnosti.TryGetValue(m, out var n) ? n : null, z.Sposob))
            .ToList();
    }

    /// <summary>Neznáme kódy (nie sú v evidencii) podľa miestnosti.</summary>
    public List<ZaznamInventury> Nezname(int inventuraId) =>
        Zaznamy(inventuraId, int.MaxValue).Where(z => z.PolozkaId is null).OrderBy(z => z.Cas).ToList();

    /// <summary>Presunie položky nájdené inde do miestnosti, kde boli nájdené (zmena sa zapíše do histórie).</summary>
    public int PresunNajdene(IEnumerable<PolozkaInventury> polozky)
    {
        var poMiestnostiach = polozky.Where(p => p.Inde && p.NajdenaV is not null).GroupBy(p => p.NajdenaV!.Value);
        var sluzba = new PolozkaService();
        var spolu = 0;
        foreach (var skupina in poMiestnostiach)
        {
            var miestnost = skupina.Key;
            spolu += sluzba.HromadnaZmena(skupina.Select(p => p.Id), p => p.MiestnostId = miestnost);
        }

        return spolu;
    }

    // ------------------------------------------------------------------ protokol

    public Document Protokol(int inventuraId, bool ajNajdene = true)
    {
        var inventura = Nacitaj(inventuraId) ?? throw new EvitaException(Preklad.T("Inventúra už neexistuje."));
        var polozky = Polozky(inventuraId);
        var nezname = Nezname(inventuraId);
        var podtitulok = inventura.Koniec is { } koniec
            ? Preklad.F($"{Sk.DatumCas(inventura.Zaciatok)} – {Sk.DatumCas(koniec)}")
            : Preklad.F($"začatá {Sk.DatumCas(inventura.Zaciatok)}, ešte prebieha");
        var zostava = new PdfZostava(Preklad.F($"Inventúrny protokol – {inventura.Nazov}"), podtitulok, false);

        var aktivne = polozky.Where(p => !p.Vyradena).ToList();
        var najdeneCelkom = aktivne.Count(p => p.Najdena);
        zostava.Text(Preklad.F($"Položiek v evidencii: {aktivne.Count} · nájdené: {najdeneCelkom} · chýba: {aktivne.Count - najdeneCelkom} · nájdené v inej miestnosti: {aktivne.Count(p => p.Inde)} · neznáme kódy: {nezname.Count}"), tucne: true);

        // súhrn po miestnostiach
        var miestnosti = aktivne.Select(p => (p.MiestnostId, p.Miestnost))
            .Concat(polozky.Where(p => p.Najdena).Select(p => (p.NajdenaV, p.NajdenaVNazov)))
            .Concat(nezname.Select(z => (z.MiestnostId, z.Miestnost)))
            .GroupBy(m => m.Item1)
            .Select(g => (Id: g.Key, Nazov: g.Select(x => x.Item2).FirstOrDefault(n => n is not null) ?? Preklad.T("(bez miestnosti)")))
            .OrderBy(m => m.Id is null)
            .ThenBy(m => m.Nazov, Sk.Porovnanie)
            .ToList();

        var hlavickySuhrnu = new[] { Preklad.T("Miestnosť"), Preklad.T("V evidencii"), Preklad.T("Nájdené"), Preklad.T("Chýba"), Preklad.T("Nájdené tu, evidované inde") };
        var riadkySuhrnu = miestnosti.Select(m =>
        {
            var tu = aktivne.Where(p => p.MiestnostId == m.Id).ToList();
            return new[]
            {
                m.Nazov, tu.Count.ToString(Sk.Kultura), tu.Count(p => p.Najdena && !p.Inde).ToString(Sk.Kultura),
                tu.Count(p => !p.Najdena).ToString(Sk.Kultura),
                polozky.Count(p => p.Inde && p.NajdenaV == m.Id).ToString(Sk.Kultura),
            };
        }).ToList();
        zostava.NadpisSkupiny(Preklad.T("Súhrn po miestnostiach"));
        zostava.Tabulka(hlavickySuhrnu, new[] { false, true, true, true, true }, riadkySuhrnu, new[] { 7.0, 2.4, 2.2, 2.2, 4.8 });

        var hlavicky = new[] { Preklad.T("Č."), Preklad.T("Názov"), Preklad.T("Kategória"), Preklad.T("Pridelené"), Preklad.T("Inventárne číslo"), Preklad.T("Stav v inventúre") };
        var vpravo = new[] { true, false, false, false, false, false };
        var sirky = new[] { 1.3, 6.0, 2.6, 3.0, 2.6, 3.1 };
        string[] Riadok(PolozkaInventury p) => new[]
        {
            p.PrirastkoveCislo.ToString(Sk.Kultura), p.Nazov, p.Kategoria, p.Osoba ?? "", p.Inventarne ?? "",
            p.Vyradena ? Preklad.T("vyradená, nájdená") : p.StavText,
        };

        foreach (var m in miestnosti)
        {
            var evidovane = aktivne.Where(p => p.MiestnostId == m.Id).ToList();
            var cudzie = polozky.Where(p => p.Najdena && p.NajdenaV == m.Id && (p.Inde || p.Vyradena)).ToList();
            var neznameTu = nezname.Where(z => z.MiestnostId == m.Id).ToList();
            var zobrazit = ajNajdene ? evidovane : evidovane.Where(p => !p.Najdena || p.Inde).ToList();
            if (zobrazit.Count == 0 && cudzie.Count == 0 && neznameTu.Count == 0)
            {
                continue;
            }

            zostava.NadpisSkupiny(m.Nazov, Preklad.F($"nájdené {evidovane.Count(p => p.Najdena && !p.Inde)} z {evidovane.Count}"));
            if (zobrazit.Count > 0)
            {
                var poradie = zobrazit.OrderBy(p => p.Najdena && !p.Inde).ThenBy(p => p.PrirastkoveCislo).ToList();
                zostava.Tabulka(hlavicky, vpravo, poradie.Select(Riadok).ToList(), sirky, poradie.Select(p => p.Najdena && !p.Inde).ToList());
            }

            if (cudzie.Count > 0)
            {
                zostava.Text(Preklad.T("Nájdené v tejto miestnosti, ale v evidencii sú inde:"), tucne: true).Format.SpaceBefore = Unit.FromPoint(6);
                zostava.Tabulka(
                    new[] { Preklad.T("Č."), Preklad.T("Názov"), Preklad.T("Kategória"), Preklad.T("V evidencii"), Preklad.T("Nájdené") },
                    new[] { true, false, false, false, false },
                    cudzie.Select(p => new[]
                    {
                        p.PrirastkoveCislo.ToString(Sk.Kultura), p.Nazov, p.Kategoria,
                        p.Vyradena ? Preklad.T("vyradená") : p.Miestnost ?? Preklad.T("(bez miestnosti)"), p.Cas is { } c ? Sk.DatumCas(c) : "",
                    }).ToList(),
                    new[] { 1.3, 6.6, 2.8, 4.0, 3.9 });
            }

            if (neznameTu.Count > 0)
            {
                zostava.Text(Preklad.T("Naskenované kódy, ktoré nie sú v evidencii:"), tucne: true).Format.SpaceBefore = Unit.FromPoint(6);
                zostava.Tabulka(new[] { Preklad.T("Kód"), Preklad.T("Čas") }, new[] { false, false },
                    neznameTu.Select(z => new[] { z.Kod, Sk.DatumCas(z.Cas) }).ToList(), new[] { 12.0, 6.6 });
            }
        }

        zostava.Podpis(Preklad.T("Inventúru vykonal(a)"), Preklad.T("Dátum"), Preklad.T("Podpis"));
        return zostava.Dokument;
    }
}
