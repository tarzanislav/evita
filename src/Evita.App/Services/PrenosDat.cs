using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Evita.Models;

namespace Evita.Services;

/// <summary>Druh hodnoty v stĺpci exportu a importu.</summary>
public enum TypStlpca
{
    Text,
    CeleCislo,
    Datum,
    DatumCas,
    Suma,
    Stav,
    Identifikator,
}

/// <summary>
/// Jeden stĺpec, ktorý sa dá exportovať aj importovať (rovnaký názov v oboch smeroch). Názov, popis a príklad sú
/// zapísané po slovensky a zobrazujú sa v jazyku programu; import rozpozná názov stĺpca v ktoromkoľvek jazyku.
/// </summary>
public sealed record StlpecPrenosu(
    string Kluc,
    string NazovSk,
    TypStlpca Typ,
    bool Povinny,
    bool PredvolenyExport,
    double Sirka,
    string PopisSk,
    string PrikladSk,
    string[] Aliasy)
{
    public string Nazov => Preklad.T(NazovSk);
    public string Popis => Preklad.T(PopisSk);
    public string Priklad => Preklad.TAkJe(PrikladSk);
}

/// <summary>Stĺpce evidencie pre export a import a ich rozpoznanie podľa názvu.</summary>
public static class StlpcePrenosu
{
    public const string Cislo = "cislo";
    public const string Nazov = "nazov";
    public const string Kategoria = "kategoria";
    public const string Miestnost = "miestnost";
    public const string Pridelene = "pridelene";
    public const string Vyrobca = "vyrobca";
    public const string Model = "model";
    public const string Seriove = "seriove";
    public const string Inventarne = "inventarne";
    public const string Stav = "stav";
    public const string DatumNakupu = "datumNakupu";
    public const string Cena = "cena";
    public const string ZarukaDo = "zarukaDo";
    public const string DalsiaRevizia = "dalsiaRevizia";
    public const string DatumPridania = "datumPridania";
    public const string DatumZmeny = "datumZmeny";
    public const string DatumVyradenia = "datumVyradenia";
    public const string DovodVyradenia = "dovodVyradenia";
    public const string Poznamka = "poznamka";
    public const string Identifikator = "guid";

    public static IReadOnlyList<StlpecPrenosu> Vsetky { get; } = new StlpecPrenosu[]
    {
        new(Cislo, Preklad.N("Prírastkové číslo"), TypStlpca.CeleCislo, false, true, 1.4,
            Preklad.N("Pôvodné číslo položky. Pri doplnení do evidencie dostane položka nové číslo (od posledného + 1) a pôvodné sa zapíše do histórie."),
            "57", new[] { "prirastkovecislo", "prircislo", "cislo", "c", "poradovecislo", "evidencnecislo", "id" }),
        new(Nazov, Preklad.N("Názov"), TypStlpca.Text, true, true, 5.5,
            Preklad.N("Názov položky. Jediný povinný údaj."),
            Preklad.N("Notebook Dell Latitude 5540"), new[] { "nazov", "nazovpolozky", "name", "polozka", "zariadenie", "predmet" }),
        new(Kategoria, Preklad.N("Kategória"), TypStlpca.Text, false, true, 2.4,
            Preklad.N("Kategória zo zoznamu. Neznáma kategória sa vytvorí. Bez kategórie sa položka zaradí do kategórie „nezaradené“."),
            Preklad.N("notebook"), new[] { "kategoria", "category", "druh", "typzariadenia", "skupina" }),
        new(Miestnost, Preklad.N("Miestnosť"), TypStlpca.Text, false, true, 2.6,
            Preklad.N("Kde je položka umiestnená. Neznáma miestnosť sa vytvorí."),
            Preklad.N("Kancelária 101"), new[] { "miestnost", "room", "umiestnenie", "lokalita", "kancelaria", "ucebna", "miesto" }),
        new(Pridelene, Preklad.N("Pridelené"), TypStlpca.Text, false, true, 2.8,
            Preklad.N("Komu je položka pridelená, v tvare Meno Priezvisko. Neznáma osoba sa vytvorí."),
            "Ján Novák", new[] { "pridelene", "pridelenie", "osoba", "pouzivatel", "uzivatel", "zodpovednaosoba", "zamestnanec", "user", "owner", "drzitel" }),
        new(Vyrobca, Preklad.N("Výrobca"), TypStlpca.Text, false, true, 2.0,
            Preklad.N("Výrobca alebo značka."), "Dell", new[] { "vyrobca", "manufacturer", "znacka", "brand", "vendor" }),
        new(Model, Preklad.N("Model"), TypStlpca.Text, false, true, 2.4,
            Preklad.N("Model alebo typové označenie."), "Latitude 5540", new[] { "model", "typ", "modelzariadenia" }),
        new(Seriove, Preklad.N("Sériové číslo"), TypStlpca.Text, false, true, 2.6,
            Preklad.N("Výrobné (sériové) číslo. Podľa neho sa pri importe zisťuje, či položka už v evidencii je."),
            "5CD1234XYZ", new[] { "seriovecislo", "sn", "serial", "serialnumber", "sernumber", "vyrobnecislo", "sc" }),
        new(Inventarne, Preklad.N("Inventárne číslo"), TypStlpca.Text, false, true, 2.2,
            Preklad.N("Inventárne číslo z účtovníctva, ak sa používa."), "INV-2026-001", new[] { "inventarnecislo", "invcislo", "inventarne", "inventory", "inventorynumber" }),
        new(Stav, Preklad.N("Stav"), TypStlpca.Stav, false, true, 1.8,
            Preklad.N("v prevádzke, v sklade, v oprave alebo vyradené. Prázdne = v prevádzke."),
            Preklad.N("v prevádzke"), new[] { "stav", "status", "state" }),
        new(DatumNakupu, Preklad.N("Dátum nákupu"), TypStlpca.Datum, false, true, 1.8,
            Preklad.N("Dátum v tvare 20.10.2026, 20. 10. 2026 alebo 2026-10-20."),
            "15.3.2026", new[] { "datumnakupu", "nakup", "zakupene", "datumobstarania", "obstarane", "purchasedate", "datumkupy" }),
        new(Cena, Preklad.N("Cena"), TypStlpca.Suma, false, true, 1.6,
            Preklad.N("Cena v eurách, s desatinnou čiarkou alebo bodkou."), "1089,00", new[] { "cena", "price", "obstaravaciacena", "cenasdph", "hodnota" }),
        new(ZarukaDo, Preklad.N("Záruka do"), TypStlpca.Datum, false, true, 1.8,
            Preklad.N("Dátum konca záruky."), "15.3.2029", new[] { "zarukado", "zaruka", "warranty", "konieczaruky" }),
        new(DalsiaRevizia, Preklad.N("Ďalšia revízia"), TypStlpca.Datum, false, false, 1.8,
            Preklad.N("Dátum najbližšej revízie alebo kontroly."), "", new[] { "dalsiarevizia", "revizia", "revizdo", "datumrevizie" }),
        new(DatumPridania, Preklad.N("Dátum pridania"), TypStlpca.DatumCas, false, true, 2.2,
            Preklad.N("Kedy bola položka zaradená do evidencie. Prázdne = deň importu."),
            "20.10.2026", new[] { "datumpridania", "pridane", "datumzaradenia", "zaradene", "created" }),
        new(DatumZmeny, Preklad.N("Dátum zmeny"), TypStlpca.DatumCas, false, false, 2.2,
            Preklad.N("Kedy sa položka naposledy zmenila (len pri prenose celej databázy)."), "", new[] { "datumzmeny", "zmenene", "upravene", "modified" }),
        new(DatumVyradenia, Preklad.N("Dátum vyradenia"), TypStlpca.Datum, false, false, 1.8,
            Preklad.N("Len pri stave vyradené."), "", new[] { "datumvyradenia" }),
        new(DovodVyradenia, Preklad.N("Dôvod vyradenia"), TypStlpca.Text, false, false, 3.0,
            Preklad.N("Len pri stave vyradené."), "", new[] { "dovodvyradenia" }),
        new(Poznamka, Preklad.N("Poznámka"), TypStlpca.Text, false, true, 4.0,
            Preklad.N("Ľubovoľný text."), Preklad.N("nabíjačka v taške"), new[] { "poznamka", "poznamky", "note", "notes", "komentar", "popis" }),
        new(Identifikator, Preklad.N("Identifikátor (GUID)"), TypStlpca.Identifikator, false, false, 3.4,
            Preklad.N("Skrytý jedinečný identifikátor z inej EviTA. Podľa neho sa spozná položka, ktorá už v evidencii je. Pri novej položke nechajte prázdne."),
            "", new[] { "guid", "identifikator", "identifikatorguid", "uuid" }),
    };

    private static readonly Dictionary<string, StlpecPrenosu> PodlaKluca =
        Vsetky.ToDictionary(s => s.Kluc, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> PodlaAliasu = VytvorAliasy();

    public static StlpecPrenosu Podla(string kluc) => PodlaKluca[kluc];

    /// <summary>Rozpozná stĺpec podľa názvu v súbore („Sériové číslo“, „SN“, „serial number“ …). Vráti kľúč alebo null.</summary>
    public static string? Rozpoznaj(string? hlavicka)
    {
        var kluc = Normalizuj(hlavicka);
        return kluc.Length > 0 && PodlaAliasu.TryGetValue(kluc, out var stlpec) ? stlpec : null;
    }

    /// <summary>Názov bez diakritiky, medzier a interpunkcie, malými písmenami.</summary>
    public static string Normalizuj(string? text)
    {
        var bez = Sk.BezDiakritiky(text);
        var sb = new StringBuilder(bez.Length);
        foreach (var znak in bez)
        {
            if (char.IsLetterOrDigit(znak))
            {
                sb.Append(znak);
            }
        }

        return sb.ToString();
    }

    private static Dictionary<string, string> VytvorAliasy()
    {
        var aliasy = new Dictionary<string, string>(StringComparer.Ordinal);
        // názvy stĺpcov vo všetkých jazykoch programu (súbor mohol vzniknúť v inom jazyku)
        var slovniky = Preklad.Jazyky.Where(j => j.Kod != "sk").Select(j => Preklad.NacitajSlovnik(j.Kod)).ToList();
        foreach (var stlpec in Vsetky)
        {
            aliasy.TryAdd(Normalizuj(stlpec.NazovSk), stlpec.Kluc);
            aliasy.TryAdd(Normalizuj(stlpec.Kluc), stlpec.Kluc);
            foreach (var slovnik in slovniky)
            {
                if (slovnik.TryGetValue(stlpec.NazovSk, out var preklad))
                {
                    aliasy.TryAdd(Normalizuj(preklad), stlpec.Kluc);
                }
            }

            foreach (var alias in stlpec.Aliasy)
            {
                aliasy.TryAdd(alias, stlpec.Kluc);
            }
        }

        // názvy prvkov a vlastností z XML a JSON exportu EviTA
        aliasy.TryAdd("serioveci", Seriove);
        return aliasy;
    }

    /// <summary>Hodnota stĺpca pre položku (s načítanou kategóriou, miestnosťou a osobou) v pôvodnom type.</summary>
    public static object? Hodnota(Polozka p, string kluc) => kluc switch
    {
        Cislo => p.PrirastkoveCislo,
        Nazov => p.Nazov,
        Kategoria => p.Kategoria?.Nazov,
        Miestnost => p.Miestnost?.Nazov,
        Pridelene => p.Osoba?.CeleMeno,
        Vyrobca => p.Vyrobca,
        Model => p.Model,
        Seriove => p.SerioveCislo,
        Inventarne => p.InventarneCislo,
        Stav => StavyPoloziek.Nazov(p.Stav),
        DatumNakupu => p.DatumNakupu,
        Cena => p.Cena,
        ZarukaDo => p.ZarukaDo,
        DalsiaRevizia => p.DalsiaRevizia,
        DatumPridania => p.DatumPridania,
        DatumZmeny => p.DatumZmeny,
        DatumVyradenia => p.DatumVyradenia,
        DovodVyradenia => p.DovodVyradenia,
        Poznamka => p.Poznamka,
        Identifikator => p.Guid.ToString(),
        _ => null,
    };
}

/// <summary>Rozpoznávanie hodnôt z importovaných súborov (dátumy, sumy, stav …).</summary>
public static class Prevod
{
    private static readonly string[] FormatyDatumu =
    {
        "d.M.yyyy", "d. M. yyyy", "d.M. yyyy", "d. M.yyyy", "dd.MM.yyyy", "yyyy-MM-dd", "yyyy-M-d", "d/M/yyyy", "dd/MM/yyyy",
        "yyyy.MM.dd.", "yyyy. MM. dd.", "yyyy.M.d.", "yyyy. M. d.", "yyyy/M/d", "yyyy/MM/dd",
        "d.M.yyyy H:mm", "d.M.yyyy H:mm:ss", "d. M. yyyy H:mm", "d. M. yyyy H:mm:ss", "dd.MM.yyyy H:mm", "dd.MM.yyyy HH:mm",
        "d/M/yyyy H:mm", "dd/MM/yyyy HH:mm", "yyyy.MM.dd. H:mm", "yyyy. MM. dd. H:mm", "yyyy/M/d H:mm",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
    };

    /// <summary>Dátum vo formátoch 20.10.2026, 20. 10. 2026, 2026-10-20 (aj s časom). Prázdny text = bez dátumu.</summary>
    public static bool SkusDatum(string? text, out DateTime? datum)
    {
        datum = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var cisty = text.Trim().Replace(' ', ' ');
        if (DateTime.TryParseExact(cisty, FormatyDatumu, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var hodnota))
        {
            datum = hodnota.Kind == DateTimeKind.Utc ? hodnota.ToLocalTime() : hodnota;
            return datum.Value.Year is >= 1900 and <= 2200;
        }

        return false;
    }

    /// <summary>Názvy stavov vo všetkých jazykoch programu (normalizované) → stav.</summary>
    private static readonly Lazy<Dictionary<string, StavPolozky>> StavyVJazykoch = new(() =>
    {
        var vysledok = new Dictionary<string, StavPolozky>(StringComparer.Ordinal);
        var slovniky = Preklad.Jazyky.Where(j => j.Kod != "sk").Select(j => Preklad.NacitajSlovnik(j.Kod)).ToList();
        foreach (var stav in Enum.GetValues<StavPolozky>())
        {
            var nazov = StavyPoloziek.NazovSk(stav);
            vysledok.TryAdd(StlpcePrenosu.Normalizuj(nazov), stav);
            foreach (var slovnik in slovniky)
            {
                if (slovnik.TryGetValue(nazov, out var preklad))
                {
                    vysledok.TryAdd(StlpcePrenosu.Normalizuj(preklad), stav);
                }
            }
        }

        return vysledok;
    });

    /// <summary>Stav podľa textu (v prevádzke, v sklade, v oprave, vyradené – aj bez diakritiky). Prázdny = v prevádzke.</summary>
    public static bool SkusStav(string? text, out StavPolozky stav)
    {
        stav = StavPolozky.VPrevadzke;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var t = StlpcePrenosu.Normalizuj(text);
        if (StavyVJazykoch.Value.TryGetValue(t, out var najdeny))
        {
            stav = najdeny;
            return true;
        }

        if (t.Contains("prevadz") || t is "aktivne" or "aktivny" or "pouziva" or "0")
        {
            stav = StavPolozky.VPrevadzke;
            return true;
        }

        if (t.Contains("sklad") || t == "1")
        {
            stav = StavPolozky.VSklade;
            return true;
        }

        if (t.Contains("oprav") || t.Contains("servis") || t == "2")
        {
            stav = StavPolozky.VOprave;
            return true;
        }

        if (t.Contains("vyrad") || t == "3")
        {
            stav = StavPolozky.Vyradene;
            return true;
        }

        return false;
    }

    public static bool SkusCeleCislo(string? text, out int? cislo)
    {
        cislo = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var cisty = text.Trim().Replace(" ", "").Replace(" ", "");
        if (int.TryParse(cisty, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hodnota) && hodnota > 0)
        {
            cislo = hodnota;
            return true;
        }

        // Excel niekedy uloží celé číslo ako 57.0
        if (double.TryParse(cisty.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && d > 0 && d < int.MaxValue && Math.Abs(d - Math.Round(d)) < 1e-9)
        {
            cislo = (int)Math.Round(d);
            return true;
        }

        return false;
    }

    /// <summary>Formát dátumu v CSV podľa jazyka (tak, ako ho otvorí Excel v danom jazyku).</summary>
    private static string FormatCsv => Preklad.Kod switch
    {
        "sk" or "cs" => "d.M.yyyy",
        "en" => "dd/MM/yyyy",
        "hu" => "yyyy.MM.dd.",
        "zh" => "yyyy-MM-dd",
        _ => "dd.MM.yyyy",
    };

    /// <summary>Hodnota ako text pre CSV (dátumy 20.10.2026, sumy 1089,00 – tak, ako ich otvorí slovenský Excel).</summary>
    public static string TextCsv(object? hodnota) => hodnota switch
    {
        null => "",
        DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString(FormatCsv, CultureInfo.InvariantCulture),
        DateTime d => d.ToString(FormatCsv + " H:mm", CultureInfo.InvariantCulture),
        decimal s => s.ToString("0.00", Sk.Kultura),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => hodnota.ToString() ?? "",
    };

    /// <summary>Hodnota ako text na zobrazenie (HTML, PDF).</summary>
    public static string TextZobrazenia(object? hodnota, TypStlpca typ) => hodnota switch
    {
        null => "",
        DateTime d when typ == TypStlpca.DatumCas && d.TimeOfDay != TimeSpan.Zero => Sk.DatumCas(d),
        DateTime d => Sk.Datum(d),
        decimal s => Sk.Suma(s),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => hodnota.ToString() ?? "",
    };

    /// <summary>Dátum pre JSON a XML (2026-10-20 alebo 2026-10-20T09:05:00).</summary>
    public static string? TextIso(DateTime? hodnota, bool sCasom) =>
        hodnota is null
            ? null
            : sCasom
                ? hodnota.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
                : hodnota.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// ---------------------------------------------------------------- úplná štruktúra (JSON a XML)

/// <summary>Úplný export EviTA: číselníky, položky a voliteľne história zmien.</summary>
public class BalikEviTA
{
    [JsonPropertyOrder(-10)] public string Format { get; set; } = "EviTA";
    [JsonPropertyOrder(-9)] public int VerziaFormatu { get; set; } = 1;
    [JsonPropertyOrder(-8)] public string? Program { get; set; }
    [JsonPropertyOrder(-7)] public string? Vytvorene { get; set; }
    [JsonPropertyOrder(-6)] public string? Pocitac { get; set; }
    [JsonPropertyOrder(-5)] public int? PoslednePrirastkoveCislo { get; set; }
    [JsonPropertyOrder(1)] public List<KategoriaPrenos> Kategorie { get; set; } = new();
    [JsonPropertyOrder(2)] public List<MiestnostPrenos> Miestnosti { get; set; } = new();
    [JsonPropertyOrder(3)] public List<OsobaPrenos> Osoby { get; set; } = new();
    [JsonPropertyOrder(5)] public List<ZmenaPrenos>? Historia { get; set; }
    [JsonPropertyOrder(6)] public List<TypUdalostiPrenos>? TypyUdalosti { get; set; }
    [JsonPropertyOrder(7)] public List<UdalostPrenos>? Udalosti { get; set; }
    [JsonPropertyOrder(8)] public List<UlohaPrenos>? Ulohy { get; set; }
    [JsonPropertyOrder(9)] public List<PoznamkaPrenos>? Poznamky { get; set; }
}

/// <summary>Úloha zo zoznamu úloh pri prenose.</summary>
public sealed class UlohaPrenos
{
    public string? Guid { get; set; }
    public string Text { get; set; } = "";
    public string? Popis { get; set; }
    public string? Termin { get; set; }
    public string Priorita { get; set; } = "Normalna";
    public bool Hotovo { get; set; }
    public string? DatumHotovo { get; set; }
    public string? Vytvorene { get; set; }
    public string? PolozkaGuid { get; set; }
    public int? PolozkaCislo { get; set; }
}

/// <summary>Poznámka pri prenose. Väzba na udalosť je podľa jej identifikátora.</summary>
public sealed class PoznamkaPrenos
{
    public string? Guid { get; set; }
    public string Nazov { get; set; } = "";
    public string Text { get; set; } = "";
    public string Farba { get; set; } = "#FFF59D";
    public bool Pripnuta { get; set; }
    public string? Vytvorene { get; set; }
    public string? Zmenene { get; set; }
    public string? Datum { get; set; }
    public string? PolozkaGuid { get; set; }
    public int? PolozkaCislo { get; set; }
    public string? UdalostGuid { get; set; }
}

/// <summary>Typ udalosti pracovného kalendára pri prenose.</summary>
public sealed class TypUdalostiPrenos
{
    public string Nazov { get; set; } = "";
    public string Farba { get; set; } = "#7F8C8D";
    public bool Aktivny { get; set; } = true;
}

/// <summary>Udalosť pracovného kalendára pri prenose. Väzby sú podľa identifikátora položky a názvov.</summary>
public sealed class UdalostPrenos
{
    public string? Guid { get; set; }
    public string Nazov { get; set; } = "";
    public string? Popis { get; set; }
    public string Typ { get; set; } = "";
    public string Druh { get; set; } = "Udalost";
    public string Zaciatok { get; set; } = "";
    public string? Koniec { get; set; }
    public bool CelyDen { get; set; }
    public string Opakovanie { get; set; } = "Ziadne";
    public string? OpakovanieDo { get; set; }
    public string Pripomienka { get; set; } = "Ziadna";
    public string? PolozkaGuid { get; set; }
    public int? PolozkaCislo { get; set; }
    public string? Miestnost { get; set; }
    public string? Osoba { get; set; }
    public string Stav { get; set; } = "Naplanovane";
}

/// <summary>Export do JSON – navyše obsahuje položky (pri čítaní sa položky spracujú zvlášť, aby chyba v jednej neprekazila).</summary>
public sealed class BalikEviTAPolozky : BalikEviTA
{
    [JsonPropertyOrder(4)] public List<PolozkaPrenos> Polozky { get; set; } = new();
}

public sealed class KategoriaPrenos
{
    public string Nazov { get; set; } = "";
    public bool Aktivna { get; set; } = true;
}

public sealed class MiestnostPrenos
{
    public string Nazov { get; set; } = "";
    public string? Budova { get; set; }
    public string? Poznamka { get; set; }
    public bool Aktivna { get; set; } = true;
}

public sealed class OsobaPrenos
{
    public string Meno { get; set; } = "";
    public string Priezvisko { get; set; } = "";
    public string? Funkcia { get; set; }
    public bool Aktivna { get; set; } = true;

    [JsonIgnore] public string CeleMeno => $"{Meno} {Priezvisko}".Trim();
}

public sealed class PolozkaPrenos
{
    public string? Guid { get; set; }
    public int? PrirastkoveCislo { get; set; }
    public string Nazov { get; set; } = "";
    public string? Kategoria { get; set; }
    public string? Miestnost { get; set; }
    public string? Pridelene { get; set; }
    public string? Vyrobca { get; set; }
    public string? Model { get; set; }
    public string? SerioveCislo { get; set; }
    public string? InventarneCislo { get; set; }
    public string? Stav { get; set; }
    public string? DatumNakupu { get; set; }
    public decimal? Cena { get; set; }
    public string? ZarukaDo { get; set; }
    public string? DalsiaRevizia { get; set; }
    public string? DatumPridania { get; set; }
    public string? DatumZmeny { get; set; }
    public string? DatumVyradenia { get; set; }
    public string? DovodVyradenia { get; set; }
    public string? Poznamka { get; set; }

    public static PolozkaPrenos Z(Polozka p) => new()
    {
        Guid = p.Guid.ToString(),
        PrirastkoveCislo = p.PrirastkoveCislo,
        Nazov = p.Nazov,
        Kategoria = p.Kategoria?.Nazov,
        Miestnost = p.Miestnost?.Nazov,
        Pridelene = p.Osoba?.CeleMeno,
        Vyrobca = p.Vyrobca,
        Model = p.Model,
        SerioveCislo = p.SerioveCislo,
        InventarneCislo = p.InventarneCislo,
        Stav = StavyPoloziek.Nazov(p.Stav),
        DatumNakupu = Prevod.TextIso(p.DatumNakupu, false),
        Cena = p.Cena,
        ZarukaDo = Prevod.TextIso(p.ZarukaDo, false),
        DalsiaRevizia = Prevod.TextIso(p.DalsiaRevizia, false),
        DatumPridania = Prevod.TextIso(p.DatumPridania, true),
        DatumZmeny = Prevod.TextIso(p.DatumZmeny, true),
        DatumVyradenia = Prevod.TextIso(p.DatumVyradenia, false),
        DovodVyradenia = p.DovodVyradenia,
        Poznamka = p.Poznamka,
    };

    /// <summary>Hodnoty v poradí <see cref="StlpcePrenosu.Vsetky"/> (na import).</summary>
    public string?[] NaRiadok() => new[]
    {
        PrirastkoveCislo?.ToString(CultureInfo.InvariantCulture), Nazov, Kategoria, Miestnost, Pridelene, Vyrobca, Model,
        SerioveCislo, InventarneCislo, Stav, DatumNakupu, Cena?.ToString(CultureInfo.InvariantCulture), ZarukaDo, DalsiaRevizia,
        DatumPridania, DatumZmeny, DatumVyradenia, DovodVyradenia, Poznamka, Guid,
    };
}

public sealed class ZmenaPrenos
{
    public string? GuidPolozky { get; set; }
    public int PrirastkoveCislo { get; set; }
    public string NazovPolozky { get; set; } = "";
    public string Cas { get; set; } = "";
    public string Pouzivatel { get; set; } = "";
    public string Akcia { get; set; } = "";
    public string? Pole { get; set; }
    public string? StaraHodnota { get; set; }
    public string? NovaHodnota { get; set; }
}

/// <summary>Popis formátu XML (schéma XSD), ukladá sa vedľa exportu XML a medzi vzorové súbory.</summary>
public static class XsdEviTA
{
    public const string NazovSuboru = "evita.xsd";

    public const string Text = """
        <?xml version="1.0" encoding="utf-8"?>
        <!-- Popis formátu exportu a importu programu EviTA (verzia formátu 1). -->
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" elementFormDefault="qualified">

          <xs:simpleType name="Stav">
            <xs:annotation><xs:documentation>Stav položky.</xs:documentation></xs:annotation>
            <xs:restriction base="xs:string">
              <xs:enumeration value="v prevádzke" />
              <xs:enumeration value="v sklade" />
              <xs:enumeration value="v oprave" />
              <xs:enumeration value="vyradené" />
            </xs:restriction>
          </xs:simpleType>

          <xs:element name="EviTA">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="Kategorie" minOccurs="0">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="Kategoria" minOccurs="0" maxOccurs="unbounded">
                        <xs:complexType>
                          <xs:attribute name="nazov" type="xs:string" use="required" />
                          <xs:attribute name="aktivna" type="xs:boolean" default="true" />
                        </xs:complexType>
                      </xs:element>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>

                <xs:element name="Miestnosti" minOccurs="0">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="Miestnost" minOccurs="0" maxOccurs="unbounded">
                        <xs:complexType>
                          <xs:attribute name="nazov" type="xs:string" use="required" />
                          <xs:attribute name="budova" type="xs:string" />
                          <xs:attribute name="poznamka" type="xs:string" />
                          <xs:attribute name="aktivna" type="xs:boolean" default="true" />
                        </xs:complexType>
                      </xs:element>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>

                <xs:element name="Osoby" minOccurs="0">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="Osoba" minOccurs="0" maxOccurs="unbounded">
                        <xs:complexType>
                          <xs:attribute name="meno" type="xs:string" />
                          <xs:attribute name="priezvisko" type="xs:string" use="required" />
                          <xs:attribute name="funkcia" type="xs:string" />
                          <xs:attribute name="aktivna" type="xs:boolean" default="true" />
                        </xs:complexType>
                      </xs:element>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>

                <xs:element name="Polozky">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="Polozka" minOccurs="0" maxOccurs="unbounded">
                        <xs:complexType>
                          <xs:all>
                            <xs:element name="Nazov" type="xs:string" />
                            <xs:element name="Kategoria" type="xs:string" minOccurs="0" />
                            <xs:element name="Miestnost" type="xs:string" minOccurs="0" />
                            <xs:element name="Pridelene" type="xs:string" minOccurs="0" />
                            <xs:element name="Vyrobca" type="xs:string" minOccurs="0" />
                            <xs:element name="Model" type="xs:string" minOccurs="0" />
                            <xs:element name="SerioveCislo" type="xs:string" minOccurs="0" />
                            <xs:element name="InventarneCislo" type="xs:string" minOccurs="0" />
                            <xs:element name="Stav" type="Stav" minOccurs="0" />
                            <xs:element name="DatumNakupu" type="xs:date" minOccurs="0" />
                            <xs:element name="Cena" type="xs:decimal" minOccurs="0" />
                            <xs:element name="ZarukaDo" type="xs:date" minOccurs="0" />
                            <xs:element name="DalsiaRevizia" type="xs:date" minOccurs="0" />
                            <xs:element name="DatumPridania" type="xs:dateTime" minOccurs="0" />
                            <xs:element name="DatumZmeny" type="xs:dateTime" minOccurs="0" />
                            <xs:element name="DatumVyradenia" type="xs:date" minOccurs="0" />
                            <xs:element name="DovodVyradenia" type="xs:string" minOccurs="0" />
                            <xs:element name="Poznamka" type="xs:string" minOccurs="0" />
                          </xs:all>
                          <xs:attribute name="guid" type="xs:string" />
                          <xs:attribute name="prirastkoveCislo" type="xs:positiveInteger" />
                        </xs:complexType>
                      </xs:element>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>

                <xs:element name="Historia" minOccurs="0">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="Zmena" minOccurs="0" maxOccurs="unbounded">
                        <xs:complexType>
                          <xs:attribute name="guidPolozky" type="xs:string" />
                          <xs:attribute name="prirastkoveCislo" type="xs:int" use="required" />
                          <xs:attribute name="nazovPolozky" type="xs:string" />
                          <xs:attribute name="cas" type="xs:dateTime" use="required" />
                          <xs:attribute name="pouzivatel" type="xs:string" />
                          <xs:attribute name="akcia" type="xs:string" use="required" />
                          <xs:attribute name="pole" type="xs:string" />
                          <xs:attribute name="staraHodnota" type="xs:string" />
                          <xs:attribute name="novaHodnota" type="xs:string" />
                        </xs:complexType>
                      </xs:element>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>

                <xs:element name="TypyUdalosti" minOccurs="0">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="TypUdalosti" minOccurs="0" maxOccurs="unbounded">
                        <xs:complexType>
                          <xs:attribute name="nazov" type="xs:string" use="required" />
                          <xs:attribute name="farba" type="xs:string" />
                          <xs:attribute name="aktivny" type="xs:boolean" default="true" />
                        </xs:complexType>
                      </xs:element>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>

                <xs:element name="Udalosti" minOccurs="0">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="Udalost" minOccurs="0" maxOccurs="unbounded">
                        <xs:complexType>
                          <xs:sequence>
                            <xs:element name="Popis" type="xs:string" minOccurs="0" />
                          </xs:sequence>
                          <xs:attribute name="guid" type="xs:string" />
                          <xs:attribute name="nazov" type="xs:string" use="required" />
                          <xs:attribute name="typ" type="xs:string" />
                          <xs:attribute name="druh" type="xs:string" />
                          <xs:attribute name="zaciatok" type="xs:dateTime" use="required" />
                          <xs:attribute name="koniec" type="xs:dateTime" />
                          <xs:attribute name="celyDen" type="xs:boolean" default="false" />
                          <xs:attribute name="opakovanie" type="xs:string" />
                          <xs:attribute name="opakovanieDo" type="xs:date" />
                          <xs:attribute name="pripomienka" type="xs:string" />
                          <xs:attribute name="polozkaGuid" type="xs:string" />
                          <xs:attribute name="polozkaCislo" type="xs:int" />
                          <xs:attribute name="miestnost" type="xs:string" />
                          <xs:attribute name="osoba" type="xs:string" />
                          <xs:attribute name="stav" type="xs:string" />
                        </xs:complexType>
                      </xs:element>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>

                <xs:element name="Ulohy" minOccurs="0">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="Uloha" minOccurs="0" maxOccurs="unbounded">
                        <xs:complexType>
                          <xs:sequence>
                            <xs:element name="Popis" type="xs:string" minOccurs="0" />
                          </xs:sequence>
                          <xs:attribute name="guid" type="xs:string" />
                          <xs:attribute name="text" type="xs:string" use="required" />
                          <xs:attribute name="termin" type="xs:date" />
                          <xs:attribute name="priorita" type="xs:string" />
                          <xs:attribute name="hotovo" type="xs:boolean" default="false" />
                          <xs:attribute name="datumHotovo" type="xs:dateTime" />
                          <xs:attribute name="vytvorene" type="xs:dateTime" />
                          <xs:attribute name="polozkaGuid" type="xs:string" />
                          <xs:attribute name="polozkaCislo" type="xs:int" />
                        </xs:complexType>
                      </xs:element>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>

                <xs:element name="Poznamky" minOccurs="0">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="Poznamka" minOccurs="0" maxOccurs="unbounded">
                        <xs:complexType>
                          <xs:sequence>
                            <xs:element name="Text" type="xs:string" minOccurs="0" />
                          </xs:sequence>
                          <xs:attribute name="guid" type="xs:string" />
                          <xs:attribute name="nazov" type="xs:string" use="required" />
                          <xs:attribute name="farba" type="xs:string" />
                          <xs:attribute name="pripnuta" type="xs:boolean" default="false" />
                          <xs:attribute name="vytvorene" type="xs:dateTime" />
                          <xs:attribute name="zmenene" type="xs:dateTime" />
                          <xs:attribute name="datum" type="xs:date" />
                          <xs:attribute name="polozkaGuid" type="xs:string" />
                          <xs:attribute name="polozkaCislo" type="xs:int" />
                          <xs:attribute name="udalostGuid" type="xs:string" />
                        </xs:complexType>
                      </xs:element>
                    </xs:sequence>
                  </xs:complexType>
                </xs:element>
              </xs:sequence>
              <xs:attribute name="verziaFormatu" type="xs:int" />
              <xs:attribute name="program" type="xs:string" />
              <xs:attribute name="vytvorene" type="xs:dateTime" />
              <xs:attribute name="pocitac" type="xs:string" />
              <xs:attribute name="poslednePrirastkoveCislo" type="xs:int" />
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;
}
