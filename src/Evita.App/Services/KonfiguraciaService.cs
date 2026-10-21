using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Evita.Data;
using Evita.Models;

namespace Evita.Services;

/// <summary>Súbor s nastaveniami programu (bez evidencie).</summary>
public sealed class KonfiguraciaEviTA
{
    public const string OznacenieFormatu = "EviTA-konfiguracia";

    [JsonPropertyOrder(-10)] public string Format { get; set; } = OznacenieFormatu;
    [JsonPropertyOrder(-9)] public int VerziaFormatu { get; set; } = 1;
    [JsonPropertyOrder(-8)] public string? Program { get; set; }
    [JsonPropertyOrder(-7)] public string? Vytvorene { get; set; }
    [JsonPropertyOrder(-6)] public string? Pocitac { get; set; }
    [JsonPropertyOrder(1)] public SortedDictionary<string, string?> Nastavenia { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyOrder(2)] public List<TypUdalostiPrenos>? TypyUdalosti { get; set; }

    /// <summary>Kategórie s ikonou a farbou (od EviTA 0.7 s ikonami kategórií; staršie súbory ich nemajú).</summary>
    [JsonPropertyOrder(3)] public List<KategoriaKonfiguracie>? Kategorie { get; set; }
}

/// <summary>Kategória v konfigurácii: názov, ikona (null = podľa názvu) a farba (null = predvolená farba ikony).</summary>
public sealed class KategoriaKonfiguracie
{
    public string Nazov { get; set; } = "";
    public string? Ikona { get; set; }
    public string? Farba { get; set; }
    public bool Aktivna { get; set; } = true;
}

/// <summary>Výsledok načítania konfigurácie.</summary>
public sealed record VysledokKonfiguracie(int Nastavenia, int TypyUdalosti, bool ZmenaJazyka, List<string> Poznamky, int Kategorie = 0, int NoveKategorie = 0);

/// <summary>Export a import nastavení programu – jazyk, dashboard, počasie, kurzy, kalendár, zálohy, tlač … (bez dát evidencie).</summary>
public static class KonfiguraciaService
{
    /// <summary>Skupiny nastavení, ktoré patria do konfigurácie (podľa začiatku kľúča) a ich názvy pre používateľa.</summary>
    private static readonly (string Predpona, string Nazov)[] Skupiny =
    {
        ("Program.", Preklad.N("jazyk programu")),
        ("Dashboard.", Preklad.N("panely dashboardu")),
        ("Pocasie.Miesto", Preklad.N("miesto pre počasie")),
        ("Kurzy.Meny", Preklad.N("meny v kurzovom lístku")),
        ("Kalendar.", Preklad.N("nastavenia kalendára")),
        ("Zaloha.", Preklad.N("pravidelné zálohy")),
        ("Organizacia.", Preklad.N("názov a logo organizácie")),
        ("Tlac.", Preklad.N("tlač a zostavy")),
        ("Export.", Preklad.N("export (formát, stĺpce, priečinok)")),
        ("Aktualizacie.Kontrolovat", Preklad.N("kontrola aktualizácií")),
        ("Kalendarium.", Preklad.N("citáty a výročia (sťahovanie z Wikipédie)")),
        ("Skenovanie.", Preklad.N("skenovanie (zvuk, otvorenie karty, port pre mobil)")),
        ("Stitky.", Preklad.N("formát a obsah štítkov")),
        ("Ean.", Preklad.N("účet Icecat pre údaje podľa EAN")),
        ("Vyrobca.ZInternetu", Preklad.N("výrobca a model z internetu")),
    };

    /// <summary>Kľúče, ktoré sa neprenášajú: heslo je zašifrované len pre tento počítač, ostatné sú interné.</summary>
    private static readonly HashSet<string> Vynechane = new(StringComparer.Ordinal)
    {
        "Zaloha.HesloAutomatickych", "Zaloha.TypyOpravene", "Skenovanie.Kamera",
    };

    private static readonly JsonSerializerOptions Moznosti = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool PatriDoKonfiguracie(string kluc) =>
        !Vynechane.Contains(kluc) && Skupiny.Any(s => kluc.StartsWith(s.Predpona, StringComparison.Ordinal));

    public static string NazovSuboru() => $"EviTA_konfiguracia_{DateTime.Now:yyyy-MM-dd}.json";

    /// <summary>Uloží konfiguráciu do súboru. Vráti počet uložených nastavení.</summary>
    public static int Exportuj(string cesta)
    {
        using var db = Databaza.Otvor();
        var konfiguracia = new KonfiguraciaEviTA
        {
            Program = $"EviTA {AktualizaciaService.AktualnaVerzia}",
            Vytvorene = Prevod.TextIso(DateTime.Now, true),
            Pocitac = Environment.MachineName,
            TypyUdalosti = db.TypyUdalosti.OrderBy(t => t.Poradie).ThenBy(t => t.Id).ToList()
                .Select(t => new TypUdalostiPrenos { Nazov = t.Nazov, Farba = t.Farba, Aktivny = t.Aktivny }).ToList(),
            Kategorie = db.Kategorie.ToList().OrderBy(k => k.Nazov, Sk.Porovnanie)
                .Select(k => new KategoriaKonfiguracie { Nazov = k.Nazov, Ikona = k.Ikona, Farba = k.Farba, Aktivna = k.Aktivna }).ToList(),
        };

        foreach (var n in db.Nastavenia.ToList().Where(n => PatriDoKonfiguracie(n.Kluc)))
        {
            konfiguracia.Nastavenia[n.Kluc] = n.Hodnota;
        }

        // jazyk sa uloží vždy (aj keď sa ešte nemenil)
        konfiguracia.Nastavenia.TryAdd(Preklad.KlucJazyk, Preklad.Kod);
        File.WriteAllText(cesta, JsonSerializer.Serialize(konfiguracia, Moznosti), new UTF8Encoding(false));
        return konfiguracia.Nastavenia.Count;
    }

    /// <summary>Prečíta a skontroluje súbor s konfiguráciou.</summary>
    public static KonfiguraciaEviTA Precitaj(string cesta)
    {
        KonfiguraciaEviTA? konfiguracia;
        try
        {
            konfiguracia = JsonSerializer.Deserialize<KonfiguraciaEviTA>(File.ReadAllText(cesta), Moznosti);
        }
        catch (JsonException)
        {
            konfiguracia = null;
        }

        if (konfiguracia is null || konfiguracia.Format != KonfiguraciaEviTA.OznacenieFormatu)
        {
            throw new EvitaException(Preklad.T("Súbor neobsahuje konfiguráciu programu EviTA. Vyberte súbor uložený tlačidlom „Exportovať konfiguráciu“."));
        }

        if (konfiguracia.VerziaFormatu > 1)
        {
            throw new EvitaException(Preklad.T("Konfigurácia pochádza z novšej verzie programu EviTA. Nainštalujte najnovšiu verziu programu."));
        }

        konfiguracia.Nastavenia ??= new SortedDictionary<string, string?>(StringComparer.Ordinal);
        return konfiguracia;
    }

    /// <summary>Čo konfigurácia obsahuje – zoznam skupín pre potvrdenie pred načítaním.</summary>
    public static List<string> Obsah(KonfiguraciaEviTA k)
    {
        var obsah = Skupiny
            .Where(s => k.Nastavenia.Keys.Any(kluc => kluc.StartsWith(s.Predpona, StringComparison.Ordinal) && PatriDoKonfiguracie(kluc)))
            .Select(s => Preklad.T(s.Nazov))
            .ToList();
        if (k.TypyUdalosti is { Count: > 0 })
        {
            obsah.Add(Preklad.F($"typy udalostí kalendára ({k.TypyUdalosti.Count})"));
        }

        if (k.Kategorie is { Count: > 0 })
        {
            obsah.Add(Preklad.F($"kategórie s ikonami a farbami ({k.Kategorie.Count}; chýbajúce sa doplnia)"));
        }

        return obsah;
    }

    /// <summary>Načíta konfiguráciu do programu. Evidencia (položky, kalendár, úlohy, poznámky) sa nemení.</summary>
    public static VysledokKonfiguracie Importuj(KonfiguraciaEviTA k)
    {
        var poznamky = new List<string>();
        var povodnyJazyk = Preklad.JazykZDatabazy();
        var zalohy = new ZalohaService();
        var povodneZalohy = zalohy.NacitajNastavenia();
        var pocet = 0;
        var typy = 0;
        var kategorie = 0;
        var noveKategorie = 0;

        using (var db = Databaza.Otvor())
        {
            foreach (var (kluc, hodnota) in k.Nastavenia.Where(p => PatriDoKonfiguracie(p.Key)))
            {
                Databaza.Zapis(db, kluc, hodnota);
                pocet++;
            }

            if (k.TypyUdalosti is { Count: > 0 })
            {
                var existujuce = db.TypyUdalosti.ToList();
                var poradie = 0;
                foreach (var t in k.TypyUdalosti.Where(t => !string.IsNullOrWhiteSpace(t.Nazov)))
                {
                    var nazov = t.Nazov.Trim();
                    var farba = PoznamkyService.JePlatnaFarba(t.Farba) ? t.Farba.ToUpperInvariant() : "#7F8C8D";
                    var typ = existujuce.FirstOrDefault(x => Sk.Porovnanie.Equals(x.Nazov, nazov));
                    if (typ is null)
                    {
                        typ = new TypUdalosti { Nazov = nazov };
                        db.TypyUdalosti.Add(typ);
                        existujuce.Add(typ);
                    }

                    typ.Farba = farba;
                    typ.Aktivny = t.Aktivny;
                    typ.Poradie = poradie++;
                    typy++;
                }
            }

            // kategórie: ikona a farba podľa názvu; kategória, ktorá na tomto počítači chýba, sa doplní
            // (existujúce sa nevymazávajú ani nepremenúvajú – položky evidencie sa nemenia)
            if (k.Kategorie is { Count: > 0 })
            {
                var existujuce = db.Kategorie.ToList();
                foreach (var kat in k.Kategorie.Where(x => !string.IsNullOrWhiteSpace(x.Nazov)))
                {
                    var nazov = kat.Nazov.Trim();
                    var kategoria = existujuce.FirstOrDefault(x => Sk.Porovnanie.Equals(x.Nazov.Trim(), nazov));
                    if (kategoria is null)
                    {
                        kategoria = new Kategoria { Nazov = nazov, Aktivna = kat.Aktivna };
                        db.Kategorie.Add(kategoria);
                        existujuce.Add(kategoria);
                        noveKategorie++;
                    }

                    kategoria.Ikona = kat.Ikona is null ? null : IkonyKategorii.Podla(kat.Ikona).Kluc;
                    kategoria.Farba = IkonyKategorii.PlatnaFarba(kat.Farba);
                    kategorie++;
                }

                if (noveKategorie > 0)
                {
                    poznamky.Add(Preklad.Pocet(noveKategorie, "Doplnená {0} nová kategória.", "Doplnené {0} nové kategórie.", "Doplnených {0} nových kategórií."));
                }
            }

            db.SaveChanges();
        }

        // pravidelné zálohy: heslo sa neprenáša a priečinok musí na tomto počítači existovať
        try
        {
            var nove = zalohy.NacitajNastavenia();
            if (nove.AutomatickaSHeslom && !zalohy.JeHesloAutomatickychNastavene())
            {
                nove = nove with { AutomatickaSHeslom = false };
                poznamky.Add(Preklad.T("Automatické zálohy s heslom sú vypnuté – heslo sa s konfiguráciou neprenáša. Nastavte ho v časti Servis."));
            }

            try
            {
                Directory.CreateDirectory(nove.Priecinok);
            }
            catch (Exception)
            {
                poznamky.Add(Preklad.F($"Priečinok záloh „{nove.Priecinok}“ na tomto počítači nie je dostupný – použije sa predvolený."));
                nove = nove with { Priecinok = Databaza.PriecinokZaloh };
            }

            zalohy.UlozNastavenia(nove);
        }
        catch (Exception ex)
        {
            try
            {
                zalohy.UlozNastavenia(povodneZalohy);
            }
            catch (Exception)
            {
                // pôvodné nastavenia zostali v databáze aspoň čiastočne
            }

            poznamky.Add(Preklad.F($"Nastavenia záloh sa nepodarilo použiť ({ex.Message}), zostali pôvodné."));
        }

        var novyJazyk = Preklad.JazykZDatabazy();
        return new VysledokKonfiguracie(pocet, typy, novyJazyk != povodnyJazyk, poznamky, kategorie, noveKategorie);
    }
}
