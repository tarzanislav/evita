using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Evita.Data;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

/// <summary>Údaje o výrobku nájdené podľa EAN (z vlastnej evidencie alebo z internetu).</summary>
public sealed record UdajeProduktu(string Zdroj, string? Nazov, string? Vyrobca, string? Model, string? Kategoria, int? KategoriaId, string? Popis)
{
    public string Prehlad => string.Join(" · ", new[] { Vyrobca, Model, Kategoria }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

/// <summary>Výsledok hľadania podľa EAN: nájdené údaje a správy o zdrojoch, ktoré nič nenašli alebo zlyhali.</summary>
public sealed record VysledokEan(string Ean, List<UdajeProduktu> Najdene, List<string> Spravy);

/// <summary>
/// Doplnenie údajov o výrobku podľa čiarového kódu z krabice (EAN): najprv vlastná evidencia (iná položka s rovnakým
/// EAN), potom katalóg IT produktov Open Icecat a databáza UPCitemdb (obe bez registrácie, UPCitemdb najviac
/// 100 dopytov denne).
/// </summary>
public sealed class EanService
{
    public const string KlucIcecat = "Ean.IcecatPouzivatel";
    public const string ZdrojEvidencia = "evidencia";
    private const string PredvolenyIcecat = "openicecat-live";

    private static readonly HttpClient Klient = VytvorKlienta();

    private static HttpClient VytvorKlienta()
    {
        var klient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        klient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EviTA", AktualizaciaService.AktualnaVerzia.ToString()));
        klient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return klient;
    }

    /// <summary>Používateľské meno Icecat (nepovinné – bez neho sa použije otvorený katalóg Open Icecat).</summary>
    public static string? IcecatPouzivatel()
    {
        try
        {
            using var db = Databaza.Otvor();
            return Sk.Prazdne(Databaza.Citaj(db, KlucIcecat));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Údaje z inej položky evidencie s rovnakým EAN.</summary>
    public UdajeProduktu? ZEvidencie(string ean, int? okremPolozky = null)
    {
        var varianty = Varianty(ean);
        using var db = Databaza.Otvor();
        var polozka = db.Polozky.AsNoTracking()
            .Where(p => p.Ean != null && varianty.Contains(p.Ean) && p.Id != (okremPolozky ?? 0))
            .OrderByDescending(p => p.DatumZmeny)
            .Select(p => new { p.Nazov, p.Vyrobca, p.Model, p.KategoriaId, Kategoria = p.Kategoria!.Nazov, p.PrirastkoveCislo })
            .FirstOrDefault();
        return polozka is null
            ? null
            : new UdajeProduktu(Preklad.F($"vlastná evidencia (položka {polozka.PrirastkoveCislo})"), polozka.Nazov, polozka.Vyrobca, polozka.Model,
                polozka.Kategoria, polozka.KategoriaId, null);
    }

    public async Task<VysledokEan> HladajAsync(string text, int? okremPolozky = null, CancellationToken zrusenie = default)
    {
        var ean = KodyEvita.NormalizujEan(text) ?? throw new EvitaException(Preklad.T("EAN musí mať 8, 12, 13 alebo 14 číslic s platnou kontrolnou číslicou."));
        var najdene = new List<UdajeProduktu>();
        var spravy = new List<string>();

        if (ZEvidencie(ean, okremPolozky) is { } vlastne)
        {
            najdene.Add(vlastne);
        }

        try
        {
            if (await IcecatAsync(ean, zrusenie) is { } icecat)
            {
                najdene.Add(icecat);
            }
            else
            {
                spravy.Add(Preklad.T("Open Icecat: výrobok nie je v otvorenom katalógu."));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            spravy.Add(Preklad.F($"Open Icecat: {Chyba(ex)}"));
        }

        try
        {
            najdene.AddRange(await UpcItemDbAsync(ean, zrusenie));
            if (najdene.All(n => n.Zdroj != "UPCitemdb"))
            {
                spravy.Add(Preklad.T("UPCitemdb: výrobok sa nenašiel."));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            spravy.Add(ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
                ? Preklad.T("UPCitemdb: dnešný limit bezplatných dopytov je vyčerpaný, skúste to zajtra.")
                : Preklad.F($"UPCitemdb: {Chyba(ex)}"));
        }

        return new VysledokEan(ean, najdene, spravy);
    }

    private static string Chyba(Exception ex) => ex switch
    {
        TaskCanceledException => Preklad.T("služba neodpovedá"),
        HttpRequestException { StatusCode: { } kod } => Preklad.F($"chyba {(int)kod}"),
        HttpRequestException => Preklad.T("bez pripojenia k internetu"),
        _ => Preklad.T("nečitateľná odpoveď"),
    };

    private static List<string> Varianty(string ean)
    {
        var varianty = new List<string> { ean };
        if (ean.Length == 12)
        {
            varianty.Add("0" + ean);
        }
        else if (ean.Length == 13 && ean[0] == '0')
        {
            varianty.Add(ean[1..]);
        }

        return varianty;
    }

    /// <summary>Katalóg Open Icecat – údaje o IT výrobkoch v jazyku programu (ak je, inak v angličtine).</summary>
    private static async Task<UdajeProduktu?> IcecatAsync(string ean, CancellationToken zrusenie)
    {
        var pouzivatel = IcecatPouzivatel() ?? PredvolenyIcecat;
        var jazyk = Preklad.Aktualny.Kod switch
        {
            "sk" => "SK",
            "cs" => "CZ",
            "pl" => "PL",
            "hu" => "HU",
            "ru" => "RU",
            "zh" => "ZH",
            _ => "EN",
        };

        foreach (var lang in jazyk == "EN" ? new[] { "EN" } : new[] { jazyk, "EN" })
        {
            var adresa = $"https://live.icecat.biz/api?lang={lang}&shopname={Uri.EscapeDataString(pouzivatel)}&GTIN={ean}";
            using var odpoved = await Klient.GetAsync(adresa, zrusenie);
            var text = await odpoved.Content.ReadAsStringAsync(zrusenie);
            if (odpoved.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
            {
                // výrobok nie je v (otvorenom) katalógu alebo nie je v tomto jazyku
                continue;
            }

            odpoved.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(text);
            if (!json.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("GeneralInfo", out var info))
            {
                continue;
            }

            var titulok = Text(info, "Title");
            var znacka = Text(info, "Brand");
            var nazov = Text(info, "ProductName");
            var kod = Text(info, "BrandPartCode");
            string? kategoria = null;
            if (info.TryGetProperty("Category", out var kat) && kat.TryGetProperty("Name", out var meno))
            {
                kategoria = Text(meno, "Value");
            }

            string? popis = null;
            if (info.TryGetProperty("SummaryDescription", out var suhrn))
            {
                popis = Text(suhrn, "ShortSummaryDescription");
            }

            if (titulok is null && nazov is null)
            {
                continue;
            }

            var model = nazov ?? kod;
            return new UdajeProduktu("Open Icecat", NavrhniNazov(kategoria, znacka, model) ?? titulok, znacka, model, kategoria, null,
                string.Join(" · ", new[] { titulok, kod is not null && kod != model ? Preklad.F($"kód výrobcu {kod}") : null, popis }
                    .Where(x => !string.IsNullOrWhiteSpace(x))));
        }

        return null;
    }

    /// <summary>Databáza UPCitemdb (bezplatný prístup bez registrácie, obmedzený počet dopytov za deň).</summary>
    private static async Task<List<UdajeProduktu>> UpcItemDbAsync(string ean, CancellationToken zrusenie)
    {
        using var odpoved = await Klient.GetAsync($"https://api.upcitemdb.com/prod/trial/lookup?upc={ean}", zrusenie);
        if (odpoved.StatusCode == HttpStatusCode.NotFound || odpoved.StatusCode == HttpStatusCode.BadRequest)
        {
            return new List<UdajeProduktu>();
        }

        if (odpoved.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("429", null, HttpStatusCode.TooManyRequests);
        }

        odpoved.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await odpoved.Content.ReadAsStringAsync(zrusenie));
        var vysledok = new List<UdajeProduktu>();
        if (!json.RootElement.TryGetProperty("items", out var polozky) || polozky.ValueKind != JsonValueKind.Array)
        {
            return vysledok;
        }

        foreach (var p in polozky.EnumerateArray().Take(3))
        {
            var titulok = Text(p, "title");
            var znacka = Text(p, "brand");
            var model = Text(p, "model");
            var kategoria = Text(p, "category")?.Split('>').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0);
            if (titulok is null && model is null)
            {
                continue;
            }

            vysledok.Add(new UdajeProduktu("UPCitemdb", titulok, znacka, model, kategoria, null, Text(p, "description")));
        }

        return vysledok;
    }

    /// <summary>Názov ako v evidencii: „Notebook Dell Latitude 5540“ (kategória, značka, model).</summary>
    private static string? NavrhniNazov(string? kategoria, string? znacka, string? model)
    {
        if (model is null)
        {
            return null;
        }

        var casti = new List<string>();
        if (!string.IsNullOrWhiteSpace(kategoria))
        {
            casti.Add(Sk.VelkePrve(kategoria.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(znacka) && !model.StartsWith(znacka, StringComparison.OrdinalIgnoreCase))
        {
            casti.Add(znacka.Trim());
        }

        casti.Add(model.Trim());
        return string.Join(' ', casti);
    }

    private static string? Text(JsonElement prvok, string vlastnost) =>
        prvok.ValueKind == JsonValueKind.Object && prvok.TryGetProperty(vlastnost, out var hodnota) && hodnota.ValueKind == JsonValueKind.String
            ? Sk.Prazdne(hodnota.GetString())
            : null;
}
