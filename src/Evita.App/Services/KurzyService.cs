using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
using Evita.Data;

namespace Evita.Services;

/// <summary>Kurz jednej meny voči euru s historiou (na malý graf).</summary>
public sealed record KurzMeny(string Kod, decimal Kurz, decimal? Predchadzajuci, IReadOnlyList<decimal> Historia)
{
    public string Nazov => KurzyService.NazovMeny(Kod);

    public decimal? ZmenaPercent => Predchadzajuci is > 0 and var p ? Math.Round((Kurz - p) / p * 100m, 2) : null;

    /// <summary>Kurz rastie = euro posilňuje voči tejto mene.</summary>
    public bool Rastie => ZmenaPercent > 0;

    public bool Klesa => ZmenaPercent < 0;

    public string KurzText => Kurz.ToString(Kurz >= 100 ? "N2" : "N4", Sk.Kultura);

    public string ZmenaText => ZmenaPercent is not { } z
        ? ""
        : (z > 0 ? "▲ +" : z < 0 ? "▼ " : "■ ") + z.ToString("0.00", Sk.Kultura) + " %";
}

/// <summary>Kurzový lístok jedného dňa.</summary>
public sealed record KurzovyListok(DateTime Datum, DateTime Stiahnute, List<KurzMeny> Meny);

/// <summary>Referenčné výmenné kurzy Európskej centrálnej banky (euro voči ostatným menám), s vyrovnávacou pamäťou v databáze.</summary>
public sealed class KurzyService
{
    public const string KlucMeny = "Kurzy.Meny";
    private const string KlucData = "Kurzy.Data";
    public const string Adresa = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-hist-90d.xml";
    public const string PredvoleneMeny = "USD,CZK,PLN,HUF,GBP,CHF,CNY,JPY";

    /// <summary>Koľko dní histórie sa uchováva (malý graf vývoja).</summary>
    private const int DniHistorie = 30;

    private static readonly HttpClient Klient = VytvorKlienta();
    private static DateTime _poslednyPokus = DateTime.MinValue;

    /// <summary>Meny, ktoré ECB zverejňuje.</summary>
    public static readonly string[] VsetkyMeny =
    {
        "USD", "JPY", "CZK", "DKK", "GBP", "HUF", "PLN", "RON", "SEK", "CHF", "ISK", "NOK", "TRY", "AUD", "BRL",
        "CAD", "CNY", "HKD", "IDR", "ILS", "INR", "KRW", "MXN", "MYR", "NZD", "PHP", "SGD", "THB", "ZAR",
    };

    public static string NazovMeny(string kod) => kod switch
    {
        "USD" => Preklad.T("americký dolár"),
        "JPY" => Preklad.T("japonský jen"),
        "CZK" => Preklad.T("česká koruna"),
        "DKK" => Preklad.T("dánska koruna"),
        "GBP" => Preklad.T("britská libra"),
        "HUF" => Preklad.T("maďarský forint"),
        "PLN" => Preklad.T("poľský zlotý"),
        "RON" => Preklad.T("rumunský lei"),
        "SEK" => Preklad.T("švédska koruna"),
        "CHF" => Preklad.T("švajčiarsky frank"),
        "ISK" => Preklad.T("islandská koruna"),
        "NOK" => Preklad.T("nórska koruna"),
        "TRY" => Preklad.T("turecká líra"),
        "AUD" => Preklad.T("austrálsky dolár"),
        "BRL" => Preklad.T("brazílsky real"),
        "CAD" => Preklad.T("kanadský dolár"),
        "CNY" => Preklad.T("čínsky jüan"),
        "HKD" => Preklad.T("hongkonský dolár"),
        "IDR" => Preklad.T("indonézska rupia"),
        "ILS" => Preklad.T("izraelský šekel"),
        "INR" => Preklad.T("indická rupia"),
        "KRW" => Preklad.T("juhokórejský won"),
        "MXN" => Preklad.T("mexické peso"),
        "MYR" => Preklad.T("malajzijský ringgit"),
        "NZD" => Preklad.T("novozélandský dolár"),
        "PHP" => Preklad.T("filipínske peso"),
        "SGD" => Preklad.T("singapurský dolár"),
        "THB" => Preklad.T("thajský baht"),
        "ZAR" => Preklad.T("juhoafrický rand"),
        "EUR" => Preklad.T("euro"),
        _ => kod,
    };

    /// <summary>Meny zvolené na dashboarde (v poradí, v akom ich používateľ zvolil).</summary>
    public List<string> ZvoleneMeny()
    {
        using var db = Databaza.Otvor();
        var text = Databaza.Citaj(db, KlucMeny) ?? PredvoleneMeny;
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => m.ToUpperInvariant())
            .Where(m => VsetkyMeny.Contains(m))
            .Distinct()
            .ToList();
    }

    public void UlozMeny(IEnumerable<string> meny)
    {
        var zoznam = meny.Select(m => m.ToUpperInvariant()).Where(m => VsetkyMeny.Contains(m)).Distinct().ToList();
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucMeny, string.Join(',', zoznam));
        db.SaveChanges();
    }

    /// <summary>Posledný stiahnutý kurzový lístok (bez internetu), alebo null.</summary>
    public KurzovyListok? ZVyrovnavacejPamate()
    {
        try
        {
            using var db = Databaza.Otvor();
            var json = Databaza.Citaj(db, KlucData);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var data = JsonSerializer.Deserialize<UlozeneKurzy>(json);
            return data is null || data.Dni.Count == 0 ? null : Listok(data, ZvoleneMeny());
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Treba stiahnuť nové kurzy? (ECB ich zverejňuje v pracovné dni okolo 16:00.)</summary>
    public bool TrebaObnovit(KurzovyListok? listok)
    {
        if (DateTime.Now - _poslednyPokus < TimeSpan.FromMinutes(15))
        {
            return false;
        }

        return listok is null || DateTime.Now - listok.Stiahnute > TimeSpan.FromHours(3);
    }

    /// <summary>Stiahne kurzy z ECB, uloží ich a vráti kurzový lístok zvolených mien.</summary>
    public async Task<KurzovyListok> StiahniAsync(CancellationToken zrusenie = default)
    {
        _poslednyPokus = DateTime.Now;
        string xml;
        try
        {
            xml = await Klient.GetStringAsync(Adresa, zrusenie).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new EvitaException(Preklad.T("Kurzy sa nepodarilo stiahnuť – skontrolujte pripojenie k internetu."), ex);
        }

        var dni = Precitaj(xml);
        if (dni.Count == 0)
        {
            throw new EvitaException(Preklad.T("Odpoveď Európskej centrálnej banky neobsahuje žiadne kurzy."));
        }

        var data = new UlozeneKurzy
        {
            Stiahnute = DateTime.Now,
            Dni = dni.Take(DniHistorie).Select(d => new UlozenyDen { Datum = d.Datum, Kurzy = d.Kurzy }).ToList(),
        };
        using (var db = Databaza.Otvor())
        {
            Databaza.Zapis(db, KlucData, JsonSerializer.Serialize(data));
            db.SaveChanges();
        }

        return Listok(data, ZvoleneMeny());
    }

    /// <summary>Kurzy z XML súboru ECB, najnovší deň prvý.</summary>
    public static List<(DateTime Datum, Dictionary<string, decimal> Kurzy)> Precitaj(string xml)
    {
        var dokument = XDocument.Parse(xml);
        var vysledok = new List<(DateTime, Dictionary<string, decimal>)>();
        foreach (var den in dokument.Descendants().Where(e => e.Name.LocalName == "Cube" && e.Attribute("time") is not null))
        {
            if (!DateTime.TryParseExact((string?)den.Attribute("time"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var datum))
            {
                continue;
            }

            var kurzy = new Dictionary<string, decimal>();
            foreach (var kurz in den.Elements().Where(e => e.Name.LocalName == "Cube"))
            {
                var mena = (string?)kurz.Attribute("currency");
                if (mena is { Length: 3 } && decimal.TryParse((string?)kurz.Attribute("rate"), NumberStyles.Float, CultureInfo.InvariantCulture, out var hodnota) && hodnota > 0)
                {
                    kurzy[mena] = hodnota;
                }
            }

            if (kurzy.Count > 0)
            {
                vysledok.Add((datum, kurzy));
            }
        }

        return vysledok.OrderByDescending(d => d.Item1).ToList();
    }

    private static KurzovyListok Listok(UlozeneKurzy data, List<string> meny)
    {
        var dni = data.Dni.OrderByDescending(d => d.Datum).ToList();
        var posledny = dni[0];
        var zoznam = new List<KurzMeny>();
        foreach (var mena in meny)
        {
            if (!posledny.Kurzy.TryGetValue(mena, out var kurz))
            {
                continue;
            }

            decimal? predchadzajuci = dni.Count > 1 && dni[1].Kurzy.TryGetValue(mena, out var p) ? p : null;
            var historia = dni.Select(d => d.Kurzy.TryGetValue(mena, out var h) ? h : (decimal?)null)
                .Where(h => h is not null).Select(h => h!.Value).Reverse().ToList();
            zoznam.Add(new KurzMeny(mena, kurz, predchadzajuci, historia));
        }

        return new KurzovyListok(posledny.Datum, data.Stiahnute, zoznam);
    }

    /// <summary>Prepočet sumy medzi menami cez euro (kurzy sú „1 EUR = x meny“).</summary>
    public static decimal? Prepocitaj(decimal suma, string z, string na, IReadOnlyDictionary<string, decimal> kurzy)
    {
        decimal? Kurz(string mena) => mena == "EUR" ? 1m : kurzy.TryGetValue(mena, out var k) ? k : null;
        if (Kurz(z) is not { } kz || Kurz(na) is not { } kn || kz == 0)
        {
            return null;
        }

        return Math.Round(suma / kz * kn, 4);
    }

    /// <summary>Všetky kurzy posledného dňa (na prepočet), alebo prázdny slovník.</summary>
    public IReadOnlyDictionary<string, decimal> PosledneKurzy()
    {
        try
        {
            using var db = Databaza.Otvor();
            var json = Databaza.Citaj(db, KlucData);
            var data = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<UlozeneKurzy>(json);
            return data?.Dni.OrderByDescending(d => d.Datum).FirstOrDefault()?.Kurzy ?? new Dictionary<string, decimal>();
        }
        catch (Exception)
        {
            return new Dictionary<string, decimal>();
        }
    }

    private static HttpClient VytvorKlienta()
    {
        var klient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        klient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EviTA", AktualizaciaService.AktualnaVerzia.ToString()));
        return klient;
    }

    private sealed class UlozeneKurzy
    {
        public DateTime Stiahnute { get; set; }
        public List<UlozenyDen> Dni { get; set; } = new();
    }

    private sealed class UlozenyDen
    {
        public DateTime Datum { get; set; }
        public Dictionary<string, decimal> Kurzy { get; set; } = new();
    }
}
