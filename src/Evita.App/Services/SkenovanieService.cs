using System.Media;
using Evita.Data;
using Evita.Models;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

/// <summary>Odkiaľ prišiel naskenovaný kód.</summary>
public enum ZdrojSkenu
{
    Citacka,
    Klavesnica,
    Kamera,
    Mobil,
}

/// <summary>Podľa čoho sa položka našla.</summary>
public enum DruhZhody
{
    Stitok,
    Prirastkove,
    Inventarne,
    Seriove,
    Ean,
}

/// <summary>Naskenovaný kód: text a jeho ďalšie možné podoby (iné rozloženie klávesnice).</summary>
public sealed record Sken(string Text, IReadOnlyList<string> Varianty, ZdrojSkenu Zdroj)
{
    public DateTime Cas { get; } = DateTime.Now;

    public static Sken Z(string text, ZdrojSkenu zdroj, IEnumerable<string>? dalsie = null)
    {
        var varianty = KodyEvita.Varianty(text, dalsie);
        return new Sken(varianty.FirstOrDefault() ?? KodyEvita.Ocisti(text), varianty, zdroj);
    }
}

/// <summary>Položka nájdená podľa kódu (stručne – na zobrazenie).</summary>
public sealed record NajdenaPolozka(
    int Id, int PrirastkoveCislo, string Nazov, string Kategoria, int? MiestnostId, string? Miestnost, string? Osoba, StavPolozky Stav)
{
    public string StavText => StavyPoloziek.Nazov(Stav);
    public bool Vyradena => Stav == StavPolozky.Vyradene;
    public string Popis => $"{PrirastkoveCislo} – {Nazov}";

    public string Detail => string.Join(" · ", new[] { Kategoria, Miestnost, Osoba, Vyradena ? StavText : null }
        .Where(x => !string.IsNullOrWhiteSpace(x)));
}

/// <summary>Výsledok vyhľadania naskenovaného kódu v evidencii.</summary>
public sealed record VysledokSkenu(Sken Sken, string PouzityKod, DruhZhody? Druh, IReadOnlyList<NajdenaPolozka> Polozky)
{
    public bool Najdene => Polozky.Count > 0;

    /// <summary>Kód je platný EAN výrobku (dá sa podľa neho hľadať na internete).</summary>
    public string? Ean => Sken.Varianty.Select(KodyEvita.NormalizujEan).FirstOrDefault(e => e is not null);

    /// <summary>Kód je štítok EviTA, ale položka s týmto číslom už v evidencii nie je.</summary>
    public bool JeStitokEviTA => Sken.Varianty.Any(v => KodyEvita.PrirastkoveZKodu(v) is not null);

    public string DruhText => Druh switch
    {
        DruhZhody.Stitok => Preklad.T("štítok EviTA"),
        DruhZhody.Prirastkove => Preklad.T("prírastkové číslo"),
        DruhZhody.Inventarne => Preklad.T("inventárne číslo"),
        DruhZhody.Seriove => Preklad.T("sériové číslo"),
        DruhZhody.Ean => Preklad.T("EAN výrobku"),
        _ => "",
    };

    /// <summary>Stručný popis výsledku (zoznam posledných skenov, odpoveď mobilu).</summary>
    public string Popis => Polozky.Count switch
    {
        0 when JeStitokEviTA => Preklad.F($"Kód {PouzityKod}: položka s týmto číslom nie je v evidencii."),
        0 => Preklad.F($"Kód {PouzityKod} sa v evidencii nenašiel."),
        1 => $"{Polozky[0].Popis}" + (Polozky[0].Miestnost is { } m ? $" ({m})" : ""),
        _ => Preklad.F($"Kód {PouzityKod}: {Polozky.Count} položiek ({DruhText})"),
    };
}

/// <summary>Vyhľadanie položky podľa naskenovaného kódu (štítok EviTA, inventárne, sériové a prírastkové číslo, EAN).</summary>
public sealed class SkenovanieService
{
    public const string KlucZvuk = "Skenovanie.Zvuk";
    public const string KlucOtvoritKartu = "Skenovanie.OtvoritKartu";
    public const string KlucCitackaVsade = "Skenovanie.CitackaVsade";
    public const string KlucPort = "Skenovanie.PortMobilu";
    public const string KlucKamera = "Skenovanie.Kamera";
    public const int PredvolenyPort = 8765;

    public VysledokSkenu Najdi(Sken sken)
    {
        using var db = Databaza.Otvor();
        foreach (var kod in sken.Varianty)
        {
            var (druh, idcka) = NajdiId(db, kod);
            if (idcka.Count > 0)
            {
                return new VysledokSkenu(sken, kod, druh, Nacitaj(db, idcka));
            }
        }

        return new VysledokSkenu(sken, sken.Text, null, Array.Empty<NajdenaPolozka>());
    }

    public VysledokSkenu Najdi(string kod) => Najdi(Sken.Z(kod, ZdrojSkenu.Klavesnica));

    private static (DruhZhody? Druh, List<int> Idcka) NajdiId(EvitaDbContext db, string kod)
    {
        var polozky = db.Polozky.AsNoTracking();

        if (KodyEvita.PrirastkoveZKodu(kod) is int zoStitku)
        {
            return (DruhZhody.Stitok, polozky.Where(p => p.PrirastkoveCislo == zoStitku).Select(p => p.Id).ToList());
        }

        var velke = kod.ToUpperInvariant();
        var inventarne = polozky.Where(p => p.InventarneCislo != null && p.InventarneCislo.ToUpper() == velke).Select(p => p.Id).ToList();
        if (inventarne.Count > 0)
        {
            return (DruhZhody.Inventarne, inventarne);
        }

        var seriove = polozky.Where(p => p.SerioveCislo != null && p.SerioveCislo.ToUpper() == velke).Select(p => p.Id).ToList();
        if (seriove.Count > 0)
        {
            return (DruhZhody.Seriove, seriove);
        }

        if (KodyEvita.NormalizujEan(kod) is { } ean)
        {
            // UPC-A (12 číslic) je to isté ako EAN-13 s nulou na začiatku
            var ean13 = ean.Length == 12 ? "0" + ean : ean;
            var upc = ean.Length == 13 && ean[0] == '0' ? ean[1..] : ean;
            var podlaEan = polozky.Where(p => p.Ean == ean || p.Ean == ean13 || p.Ean == upc).Select(p => p.Id).ToList();
            if (podlaEan.Count > 0)
            {
                return (DruhZhody.Ean, podlaEan);
            }
        }

        if (kod.Length <= 9 && kod.All(char.IsAsciiDigit) && int.TryParse(kod, out var cislo) && cislo > 0)
        {
            return (DruhZhody.Prirastkove, polozky.Where(p => p.PrirastkoveCislo == cislo).Select(p => p.Id).ToList());
        }

        return (null, new List<int>());
    }

    /// <summary>Jedna položka v stručnom tvare (ručné označenie v inventúre).</summary>
    public static NajdenaPolozka? NacitajPolozku(int id)
    {
        using var db = Databaza.Otvor();
        return Nacitaj(db, new List<int> { id }).FirstOrDefault();
    }

    private static List<NajdenaPolozka> Nacitaj(EvitaDbContext db, List<int> idcka) =>
        db.Polozky.AsNoTracking()
            .Where(p => idcka.Contains(p.Id))
            .OrderBy(p => p.Stav == StavPolozky.Vyradene)
            .ThenBy(p => p.PrirastkoveCislo)
            .Select(p => new NajdenaPolozka(
                p.Id, p.PrirastkoveCislo, p.Nazov, p.Kategoria!.Nazov, p.MiestnostId,
                p.Miestnost != null ? p.Miestnost.Nazov : null,
                p.Osoba != null ? p.Osoba.Meno + " " + p.Osoba.Priezvisko : null,
                p.Stav))
            .ToList();

    // ------------------------------------------------------------------ nastavenia

    public static bool Nastavenie(string kluc, bool predvolene)
    {
        try
        {
            using var db = Databaza.Otvor();
            return Databaza.Citaj(db, kluc) is { } hodnota ? hodnota == "1" : predvolene;
        }
        catch (Exception)
        {
            return predvolene;
        }
    }

    public static void UlozNastavenie(string kluc, string? hodnota)
    {
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, kluc, hodnota);
        db.SaveChanges();
    }

    public static int PortMobilu()
    {
        try
        {
            using var db = Databaza.Otvor();
            return int.TryParse(Databaza.Citaj(db, KlucPort), out var port) && port is > 1024 and < 65536 ? port : PredvolenyPort;
        }
        catch (Exception)
        {
            return PredvolenyPort;
        }
    }

    /// <summary>Krátky zvuk po naskenovaní (dá sa vypnúť v nastaveniach skenovania).</summary>
    public static void Zvuk(bool uspech)
    {
        if (Pomocne.Dialogy.TichyRezim || !Nastavenie(KlucZvuk, true))
        {
            return;
        }

        try
        {
            (uspech ? SystemSounds.Asterisk : SystemSounds.Hand).Play();
        }
        catch (Exception)
        {
            // bez zvuku
        }
    }
}

/// <summary>Časť programu, ktorá spracúva naskenované kódy (obrazovka Skenovanie, inventúra).</summary>
public interface IPrijimacSkenov
{
    /// <summary>Spracuje kód a vráti stručný popis výsledku (zobrazí sa aj v mobile).</summary>
    VysledokSpracovania Prijmi(Sken sken);
}

public sealed record VysledokSpracovania(bool Uspech, string Text);

/// <summary>
/// Rozdeľovač naskenovaných kódov: kód z čítačky, kamery alebo mobilu dostane naposledy otvorená časť programu,
/// ktorá kódy spracúva (inventúra, obrazovka Skenovanie), inak predvolená akcia hlavného okna (otvorenie položky).
/// Volá sa vždy vo vlákne používateľského rozhrania.
/// </summary>
public static class SkenerCentrum
{
    private static readonly List<IPrijimacSkenov> Prijimace = new();

    /// <summary>Predvolené spracovanie (nastaví hlavné okno).</summary>
    public static Func<Sken, VysledokSpracovania>? Predvolene { get; set; }

    /// <summary>Každý spracovaný kód (pre zoznam posledných skenov a okno kamery).</summary>
    public static event Action<Sken, VysledokSpracovania>? Spracovane;

    public static void Zaregistruj(IPrijimacSkenov prijimac)
    {
        Prijimace.Remove(prijimac);
        Prijimace.Add(prijimac);
    }

    public static void Odregistruj(IPrijimacSkenov prijimac) => Prijimace.Remove(prijimac);

    public static bool JePrijimac(IPrijimacSkenov prijimac) => Prijimace.Contains(prijimac);

    public static VysledokSpracovania Odosli(Sken sken)
    {
        VysledokSpracovania vysledok;
        try
        {
            if (Prijimace.Count > 0)
            {
                vysledok = Prijimace[^1].Prijmi(sken);
            }
            else if (Predvolene is { } predvolene)
            {
                vysledok = predvolene(sken);
            }
            else
            {
                vysledok = new VysledokSpracovania(false, Preklad.T("Kód sa teraz nedá spracovať."));
            }
        }
        catch (Exception ex)
        {
            vysledok = new VysledokSpracovania(false, ex.GetBaseException().Message);
        }

        SkenovanieService.Zvuk(vysledok.Uspech);
        Spracovane?.Invoke(sken, vysledok);
        return vysledok;
    }
}
