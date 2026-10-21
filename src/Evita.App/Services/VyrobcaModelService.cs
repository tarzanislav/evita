using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Evita.Data;
using Evita.Models;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

/// <summary>Návrh výrobcu a modelu podľa názvu položky (Model môže chýbať).</summary>
public sealed record NavrhVyrobcu(string Vyrobca, string? Model, string Zdroj);

/// <summary>Výsledok hľadania na internete. Hotovo = false: limit, sieť alebo časový limit – skúsi sa to znova neskôr.</summary>
public sealed record VysledokInternetu(NavrhVyrobcu? Navrh, bool Hotovo);

/// <summary>Výrobca a model, ktoré už sú v evidencii (na rýchle doplnenie bez internetu).</summary>
public sealed record ZnamyVyrobok(string Vyrobca, string? Model);

/// <summary>
/// Rýchle doplnenie výrobcu a modelu podľa názvu technológie („Router TP-Link Archer AX55“ → TP-Link, Archer AX55).
/// Najprv bez internetu: modely a výrobcovia z vlastnej evidencie a zoznam známych značiek IT techniky. Až keď sa model
/// takto nenájde, krátko (najviac pár sekúnd) sa pýta databáza výrobkov UPCitemdb – odosiela sa len názov položky.
/// </summary>
public static class VyrobcaModelService
{
    /// <summary>Najdlhšie čakanie na internet – program nesmie pôsobiť zamrznuto.</summary>
    public static readonly TimeSpan LimitInternetu = TimeSpan.FromSeconds(4);

    /// <summary>Známe značky IT techniky a príslušenstva (pravopis výrobcu).</summary>
    public static readonly string[] ZnameZnacky =
    {
        "Acer", "ADATA", "AMD", "Anker", "AOC", "APC", "Apple", "Aruba", "Asus", "Axagon", "Axis", "Baseus", "Belkin", "BenQ",
        "Bose", "Brother", "Buffalo", "Canon", "Cisco", "Connect IT", "Corsair", "Crucial", "CyberPower", "Dahua", "Datalogic",
        "Dell", "D-Link", "Eaton", "Eizo", "Epson", "Evolveo", "Fortinet", "Fujitsu", "Gembird", "Genius", "Gigabyte", "Google",
        "Grandstream", "Hama", "Hikvision", "Honeywell", "HP", "HPE", "Huawei", "HyperX", "iiyama", "Intel", "i-tec", "Jabra",
        "JBL", "Juniper", "Kingston", "Konica Minolta", "Kyocera", "Lenovo", "Lexmark", "LG", "Logitech", "Microsoft", "MikroTik",
        "Motorola", "MSI", "NEC", "Netgear", "Nokia", "Nvidia", "OKI", "Optoma", "Panasonic", "Patriot", "Philips", "Poly",
        "QNAP", "Raspberry Pi", "Razer", "Ricoh", "Samsung", "SanDisk", "Seagate", "Sencor", "Sennheiser", "Sharp", "Sony",
        "SteelSeries", "Synology", "Toshiba", "TP-Link", "Transcend", "Trust", "Ubiquiti", "Ugreen", "Verbatim", "ViewSonic",
        "Vivitek", "Western Digital", "Xerox", "Xiaomi", "Yealink", "Zebra", "Zyxel",
    };

    /// <summary>Všeobecné slová v názvoch (druh techniky) – do hľadania na internete sa neposielajú.</summary>
    private static readonly HashSet<string> VseobecneSlova = new(StringComparer.OrdinalIgnoreCase)
    {
        "notebook", "laptop", "počítač", "pocitac", "pc", "stolový", "tlačiareň", "tlaciaren", "multifunkcia", "multifunkčná",
        "monitor", "router", "smerovač", "switch", "prepínač", "myš", "mys", "klávesnica", "klavesnica", "projektor", "telefón",
        "telefon", "mobil", "mobilný", "tablet", "server", "ups", "záložný", "zdroj", "disk", "kábel", "kabel", "adaptér",
        "adapter", "dokovacia", "dokovanie", "stanica", "slúchadlá", "sluchadla", "headset", "reproduktor", "reproduktory",
        "kamera", "webkamera", "skener", "čítačka", "citacka", "nabíjačka", "nabijacka", "batéria", "pamäť", "karta", "hub",
        "rozbočovač", "televízor", "tv", "access", "point", "prístupový", "bod", "nas", "úložisko", "externý", "interný", "wifi",
        "wi-fi", "bezdrôtová", "bezdrôtový", "bezdrôtové", "herná", "herný", "kancelárska", "kancelársky", "nový", "nová",
    };

    private static readonly HttpClient Klient = VytvorKlienta();

    /// <summary>Výsledky z internetu podľa hľadaného textu (aby sa rovnaký názov nehľadal dvakrát).</summary>
    private static readonly Dictionary<string, NavrhVyrobcu?> Pamat = new(StringComparer.OrdinalIgnoreCase);

    public const string KlucZInternetu = "Vyrobca.ZInternetu";
    public const string KlucLimitDen = "Vyrobca.LimitDen";
    public const string KlucCakajuce = "Vyrobca.Cakajuce";

    /// <summary>Deň, keď sa vyčerpal denný bezplatný limit UPCitemdb (v ten deň sa už na internet nepýta).</summary>
    private static DateTime? _limitDen;
    private static bool? _zInternetu;

    /// <summary>Hľadanie výrobcu a modelu na internete je zapnuté (voľba v Evidencii; predvolene áno).</summary>
    public static bool ZInternetuZapnute
    {
        get
        {
            if (_zInternetu is null)
            {
                try
                {
                    using var db = Databaza.Otvor();
                    _zInternetu = Databaza.Citaj(db, KlucZInternetu) != "0";
                    _limitDen = DateTime.TryParse(Databaza.Citaj(db, KlucLimitDen), System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var den) ? den.Date : null;
                }
                catch (Exception)
                {
                    _zInternetu = true;
                }
            }

            return _zInternetu.Value;
        }
        set
        {
            _zInternetu = value;
            using var db = Databaza.Otvor();
            Databaza.Zapis(db, KlucZInternetu, value ? "1" : "0");
            db.SaveChanges();
        }
    }

    /// <summary>Dnes je denný limit UPCitemdb vyčerpaný (zajtra sa pokračuje).</summary>
    public static bool LimitDnesVycerpany => _limitDen == DateTime.Today;

    /// <summary>Zapamätá vyčerpanie denného limitu (aj po reštarte programu v ten istý deň). Volá aj hľadanie podľa EAN.</summary>
    public static void OznacVycerpanyLimit()
    {
        _limitDen = DateTime.Today;
        try
        {
            using var db = Databaza.Otvor();
            Databaza.Zapis(db, KlucLimitDen, DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            db.SaveChanges();
        }
        catch (Exception)
        {
            // stačí v pamäti
        }
    }

    /// <summary>Len pre samotest: zrušenie zapamätaného limitu.</summary>
    internal static void ZrusLimit()
    {
        _limitDen = null;
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucLimitDen, null);
        db.SaveChanges();
    }

    // ------------------------------------------------------------------ položky čakajúce na doplnenie

    /// <summary>Položky, ktorým sa výrobca/model nedal hľadať (limit, bez internetu) – doplnia sa neskôr, ak sú polia prázdne.</summary>
    public static List<int> Cakajuce()
    {
        try
        {
            using var db = Databaza.Otvor();
            return (Databaza.Citaj(db, KlucCakajuce) ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();
        }
        catch (Exception)
        {
            return new List<int>();
        }
    }

    private static void UlozCakajuce(IEnumerable<int> idcka)
    {
        using var db = Databaza.Otvor();
        var zoznam = idcka.Distinct().TakeLast(2000).ToList();
        Databaza.Zapis(db, KlucCakajuce, zoznam.Count == 0 ? null : string.Join(',', zoznam));
        db.SaveChanges();
    }

    public static void PridajCakajuce(IEnumerable<int> idcka)
    {
        lock (Pamat)
        {
            UlozCakajuce(Cakajuce().Concat(idcka));
        }
    }

    private static void OdoberCakajuce(int id)
    {
        lock (Pamat)
        {
            UlozCakajuce(Cakajuce().Where(x => x != id));
        }
    }

    /// <summary>
    /// Doplní výrobcu/model čakajúcim položkám (pri spustení programu a potom raz za čas): len prázdne polia, zmena sa zapíše
    /// do histórie. Skončí pri vyčerpaní limitu alebo výpadku internetu – zvyšok počká. Vráti počet doplnených položiek.
    /// </summary>
    public static async Task<int> DoplnCakajuceAsync(CancellationToken zrusenie = default, int najviac = 40)
    {
        if (!ZInternetuZapnute)
        {
            return 0;
        }

        var sluzba = new PolozkaService();

        // položky, ktorým výrobcu a model medzičasom zadal používateľ (alebo sú zmazané), už nečakajú – aj pri vyčerpanom limite
        foreach (var id in Cakajuce())
        {
            if (sluzba.Nacitaj(id) is not { } p || (!string.IsNullOrWhiteSpace(p.Vyrobca) && !string.IsNullOrWhiteSpace(p.Model)))
            {
                OdoberCakajuce(id);
            }
        }

        if (LimitDnesVycerpany)
        {
            return 0;
        }

        var zname = await Task.Run(NacitajZEvidencie, zrusenie).ConfigureAwait(false);
        var kategorie = new CiselnikService().Volby(TypCiselnika.Kategorie, null, null).Select(k => k.Nazov).ToList();
        var doplnene = 0;
        foreach (var id in Cakajuce().Take(najviac))
        {
            zrusenie.ThrowIfCancellationRequested();
            var polozka = sluzba.Nacitaj(id);
            if (polozka is null || (!string.IsNullOrWhiteSpace(polozka.Vyrobca) && !string.IsNullOrWhiteSpace(polozka.Model)))
            {
                OdoberCakajuce(id);
                continue;
            }

            var navrh = ZNazvu(polozka.Nazov, zname);
            var hotovo = true;
            if (navrh?.Model is null)
            {
                var zInternetu = await ZInternetuAsync(polozka.Nazov, kategorie, zrusenie).ConfigureAwait(false);
                hotovo = zInternetu.Hotovo;
                if (zInternetu.Navrh is { } n && (navrh is null || n.Vyrobca.Equals(navrh.Vyrobca, StringComparison.OrdinalIgnoreCase)))
                {
                    navrh = n with { Vyrobca = navrh?.Vyrobca ?? n.Vyrobca };
                }
            }

            if (navrh is not null)
            {
                var zmena = false;
                if (string.IsNullOrWhiteSpace(polozka.Vyrobca))
                {
                    polozka.Vyrobca = navrh.Vyrobca;
                    zmena = true;
                }

                if (string.IsNullOrWhiteSpace(polozka.Model) && navrh.Model is not null)
                {
                    polozka.Model = navrh.Model;
                    zmena = true;
                }

                if (zmena)
                {
                    sluzba.Uloz(polozka);
                    doplnene++;
                }
            }

            if (!hotovo)
            {
                break; // limit alebo bez internetu – pokračuje sa nabudúce
            }

            OdoberCakajuce(id);
            await Task.Delay(1500, zrusenie).ConfigureAwait(false);
        }

        return doplnene;
    }

    private static HttpClient VytvorKlienta()
    {
        var klient = new HttpClient { Timeout = LimitInternetu };
        klient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EviTA", AktualizaciaService.AktualnaVerzia.ToString()));
        klient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return klient;
    }

    /// <summary>Výrobcovia a modely z evidencie (rýchly dopyt, volá sa raz pri otvorení karty mimo vlákna rozhrania).</summary>
    public static List<ZnamyVyrobok> NacitajZEvidencie()
    {
        using var db = Databaza.Otvor();
        return db.Polozky.AsNoTracking()
            .Where(p => p.Vyrobca != null && p.Vyrobca != "")
            .Select(p => new { p.Vyrobca, p.Model })
            .Distinct()
            .AsEnumerable()
            .Select(p => new ZnamyVyrobok(p.Vyrobca!.Trim(), Sk.Prazdne(p.Model)))
            .ToList();
    }

    /// <summary>Návrh bez internetu: model z evidencie obsiahnutý v názve, inak známa značka v názve a text za ňou ako model.</summary>
    public static NavrhVyrobcu? ZNazvu(string nazov, IReadOnlyCollection<ZnamyVyrobok> evidencia)
    {
        nazov = nazov.Trim();
        if (nazov.Length < 2)
        {
            return null;
        }

        // 1. model, ktorý už je v evidencii (najdlhší, aby „Archer AX55 Pro“ vyhral nad „Archer AX55“)
        var normalizovany = Normalizuj(nazov);
        var zEvidencie = evidencia
            .Where(v => v.Model is { Length: >= 4 } && Normalizuj(v.Model).Length >= 4 && normalizovany.Contains(Normalizuj(v.Model)))
            .OrderByDescending(v => Normalizuj(v.Model!).Length)
            .FirstOrDefault();
        if (zEvidencie is not null)
        {
            return new NavrhVyrobcu(zEvidencie.Vyrobca, zEvidencie.Model, "evidencia");
        }

        // 2. značka v názve – pravopis z evidencie má prednosť pred zabudovaným zoznamom
        var znacky = evidencia.Select(v => v.Vyrobca)
            .Concat(ZnameZnacky)
            .Where(z => z.Length >= 2)
            .DistinctBy(z => z.ToLowerInvariant())
            .ToList();
        Match? najlepsia = null;
        string? znacka = null;
        foreach (var z in znacky)
        {
            var zhoda = VzorZnacky(z).Match(nazov);
            if (zhoda.Success && (najlepsia is null || zhoda.Index < najlepsia.Index
                                  || (zhoda.Index == najlepsia.Index && zhoda.Length > najlepsia.Length)))
            {
                najlepsia = zhoda;
                znacka = z;
            }
        }

        if (najlepsia is null || znacka is null)
        {
            return null;
        }

        return new NavrhVyrobcu(znacka, ModelZaZnackou(nazov, najlepsia.Index + najlepsia.Length), "názov");
    }

    /// <summary>
    /// Návrh z internetu (UPCitemdb, hľadanie podľa textu). Výrobca = najčastejšia značka v nájdených výrobkoch;
    /// model sa doplní len vtedy, keď ho názov naozaj obsahuje (nevymýšľa sa). Pri chybe, limite alebo pomalej sieti vráti null.
    /// </summary>
    public static async Task<VysledokInternetu> ZInternetuAsync(string nazov, IEnumerable<string>? dalsieVseobecneSlova, CancellationToken zrusenie)
    {
        var slova = Regex.Split(nazov.Trim(), @"\s+").Where(s => s.Length > 0).ToList();
        var vseobecne = new HashSet<string>(VseobecneSlova, StringComparer.OrdinalIgnoreCase);
        foreach (var s in dalsieVseobecneSlova ?? Enumerable.Empty<string>())
        {
            foreach (var slovo in Regex.Split(s, @"\s+").Where(x => x.Length > 0))
            {
                vseobecne.Add(slovo);
            }
        }

        var hladat = string.Join(' ', slova.Where(s => !vseobecne.Contains(s.Trim(',', '.', '(', ')'))));
        if (Normalizuj(hladat).Length < 3)
        {
            return new VysledokInternetu(null, true);
        }

        lock (Pamat)
        {
            if (Pamat.TryGetValue(hladat, out var ulozeny))
            {
                return new VysledokInternetu(ulozeny, true);
            }
        }

        if (!ZInternetuZapnute || LimitDnesVycerpany)
        {
            return new VysledokInternetu(null, false);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(zrusenie);
        limit.CancelAfter(LimitInternetu);
        NavrhVyrobcu? navrh = null;
        try
        {
            using var odpoved = await Klient.GetAsync(
                $"https://api.upcitemdb.com/prod/trial/search?s={Uri.EscapeDataString(hladat)}&match_mode=0&type=product", limit.Token).ConfigureAwait(false);
            if (odpoved.StatusCode == HttpStatusCode.TooManyRequests)
            {
                OznacVycerpanyLimit();
                return new VysledokInternetu(null, false);
            }

            if (!odpoved.IsSuccessStatusCode)
            {
                return new VysledokInternetu(null, false);
            }

            using var json = JsonDocument.Parse(await odpoved.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false));
            navrh = ZVysledkov(hladat, json.RootElement);
        }
        catch (Exception) when (!zrusenie.IsCancellationRequested)
        {
            // bez internetu, pomalá sieť, neplatná odpoveď – nič sa nevyplní, skúsi sa neskôr
            return new VysledokInternetu(null, false);
        }

        lock (Pamat)
        {
            Pamat[hladat] = navrh;
        }

        return new VysledokInternetu(navrh, true);
    }

    /// <summary>Výber výrobcu a modelu z odpovede UPCitemdb (verejné kvôli samotestu).</summary>
    public static NavrhVyrobcu? ZVysledkov(string hladat, JsonElement odpoved)
    {
        if (!odpoved.TryGetProperty("items", out var polozky) || polozky.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var najdene = polozky.EnumerateArray()
            .Take(10)
            .Select(p => (Znacka: Text(p, "brand"), Model: Text(p, "model")))
            .Where(p => p.Znacka is { Length: >= 2 and <= 40 })
            .ToList();
        if (najdene.Count == 0)
        {
            return null;
        }

        var znacka = najdene.GroupBy(p => p.Znacka!.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .First();
        // pravopis značky: zo zoznamu známych, inak ako ho uvádza väčšina výsledkov
        var vyrobca = ZnameZnacky.FirstOrDefault(z => z.Equals(znacka.First().Znacka, StringComparison.OrdinalIgnoreCase))
                      ?? znacka.GroupBy(p => p.Znacka!).OrderByDescending(g => g.Count()).First().Key;

        // model len vtedy, keď ho hľadaný text naozaj obsahuje – použije sa používateľov zápis
        string? model = null;
        var vNazve = VzorZnacky(vyrobca).Match(hladat);
        var bezZnacky = vNazve.Success ? ModelZaZnackou(hladat, vNazve.Index + vNazve.Length) ?? "" : hladat;
        var normalizovany = Normalizuj(bezZnacky);
        if (normalizovany.Length >= 3 && znacka.Any(p => p.Model is not null && Normalizuj(p.Model).Length >= 3
                                                         && normalizovany.Contains(Normalizuj(p.Model))))
        {
            model = bezZnacky.Trim();
        }

        return new NavrhVyrobcu(vyrobca, model, "UPCitemdb");
    }

    /// <summary>Text za značkou ako model („Archer AX55“); poznámka v zátvorke alebo za čiarkou sa vynechá.</summary>
    private static string? ModelZaZnackou(string nazov, int od)
    {
        var zvysok = nazov[od..];
        var koniec = zvysok.IndexOfAny(new[] { '(', ',', ';', '/' });
        if (koniec >= 0)
        {
            zvysok = zvysok[..koniec];
        }

        var pomlcka = zvysok.IndexOf(" – ", StringComparison.Ordinal);
        if (pomlcka >= 0)
        {
            zvysok = zvysok[..pomlcka];
        }

        zvysok = zvysok.Trim(' ', '-', '–', ':', '.');
        return zvysok.Length is >= 2 and <= 60 ? zvysok : null;
    }

    /// <summary>Značka ako celé slovo, bez ohľadu na veľkosť písmen a na medzeru/pomlčku vnútri („TP Link“ = „TP-Link“).</summary>
    private static Regex VzorZnacky(string znacka)
    {
        var vzor = string.Join(@"[\s\-]?", Regex.Split(znacka.Trim(), @"[\s\-]+").Select(Regex.Escape));
        return new Regex(@"(?<![\p{L}\p{N}])" + vzor + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Len písmená a číslice malými písmenami („Archer AX-55“ → „archerax55“).</summary>
    private static string Normalizuj(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var z in text)
        {
            if (char.IsLetterOrDigit(z))
            {
                sb.Append(char.ToLowerInvariant(z));
            }
        }

        return sb.ToString();
    }

    private static string? Text(JsonElement prvok, string vlastnost) =>
        prvok.ValueKind == JsonValueKind.Object && prvok.TryGetProperty(vlastnost, out var hodnota) && hodnota.ValueKind == JsonValueKind.String
            ? Sk.Prazdne(hodnota.GetString())
            : null;
}
