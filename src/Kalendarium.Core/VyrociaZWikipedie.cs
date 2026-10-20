using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Kalendarium;

/// <summary>
/// Výročia dňa zo stránok Wikipédie „20. október“, „October 20“ … (udalosti, narodenia, úmrtia).
/// Číta zdrojový text stránky (wikitext), takže nepotrebuje žiadny kľúč ani registráciu.
/// </summary>
public static partial class VyrociaZWikipedie
{
    /// <summary>Jazyky, pre ktoré vieme nájsť a prečítať stránku dňa.</summary>
    public static readonly IReadOnlyList<string> Jazyky = new[] { "sk", "cs", "pl", "hu", "en", "ru", "zh" };

    private static readonly Dictionary<string, string[]> Mesiace = new()
    {
        ["sk"] = new[] { "január", "február", "marec", "apríl", "máj", "jún", "júl", "august", "september", "október", "november", "december" },
        ["cs"] = new[] { "leden", "únor", "březen", "duben", "květen", "červen", "červenec", "srpen", "září", "říjen", "listopad", "prosinec" },
        ["pl"] = new[] { "stycznia", "lutego", "marca", "kwietnia", "maja", "czerwca", "lipca", "sierpnia", "września", "października", "listopada", "grudnia" },
        ["hu"] = new[] { "Január", "Február", "Március", "Április", "Május", "Június", "Július", "Augusztus", "Szeptember", "Október", "November", "December" },
        ["en"] = new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" },
        ["ru"] = new[] { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" },
    };

    /// <summary>Názov stránky dňa vo Wikipédii daného jazyka.</summary>
    public static string NazovStranky(string jazyk, int mesiac, int den) => jazyk switch
    {
        "sk" or "cs" => $"{den}. {Mesiace[jazyk][mesiac - 1]}",
        "pl" or "ru" => $"{den} {Mesiace[jazyk][mesiac - 1]}",
        "hu" => $"{Mesiace[jazyk][mesiac - 1]} {den}.",
        "en" => $"{Mesiace[jazyk][mesiac - 1]} {den}",
        "zh" => $"{mesiac}月{den}日",
        _ => throw new ArgumentException($"Jazyk {jazyk} nie je podporovaný.", nameof(jazyk)),
    };

    /// <summary>Adresa zdrojového textu stránky.</summary>
    public static string Adresa(string jazyk, string nazovStranky) =>
        $"https://{jazyk}.wikipedia.org/w/index.php?title={Uri.EscapeDataString(nazovStranky.Replace(' ', '_'))}&action=raw";

    /// <summary>Adresa stránky na čítanie v prehliadači.</summary>
    public static string AdresaStranky(string jazyk, int mesiac, int den) =>
        $"https://{jazyk}.wikipedia.org/wiki/{Uri.EscapeDataString(NazovStranky(jazyk, mesiac, den).Replace(' ', '_'))}";

    /// <summary>Stiahne a prečíta výročia dňa. Pri presmerovaní ide raz na cieľovú stránku.</summary>
    public static async Task<List<NoveVyrocie>> StiahniAsync(HttpClient http, string jazyk, int mesiac, int den, CancellationToken zrusenie = default)
    {
        var nazov = NazovStranky(jazyk, mesiac, den);
        var text = await http.GetStringAsync(Adresa(jazyk, nazov), zrusenie).ConfigureAwait(false);
        if (Presmerovanie().Match(text) is { Success: true } presmerovanie)
        {
            text = await http.GetStringAsync(Adresa(jazyk, presmerovanie.Groups[1].Value), zrusenie).ConfigureAwait(false);
        }

        return Precitaj(text, jazyk);
    }

    /// <summary>HTTP klient s identifikáciou programu (Wikipédia ju vyžaduje).</summary>
    public static HttpClient VytvorKlienta(string program)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{program} (https://github.com/tarzanislav/evita)");
        return http;
    }

    // ------------------------------------------------------------------ čítanie wikitextu

    private static readonly (string Kluc, DruhVyrocia Druh)[] Sekcie =
    {
        ("udalost", DruhVyrocia.Udalost), ("událost", DruhVyrocia.Udalost), ("wydarzenia", DruhVyrocia.Udalost),
        ("esemény", DruhVyrocia.Udalost), ("event", DruhVyrocia.Udalost), ("событи", DruhVyrocia.Udalost),
        ("大事", DruhVyrocia.Udalost),
        ("narodeni", DruhVyrocia.Narodenie), ("narodili", DruhVyrocia.Narodenie), ("narození", DruhVyrocia.Narodenie),
        ("urodzili", DruhVyrocia.Narodenie), ("születés", DruhVyrocia.Narodenie), ("birth", DruhVyrocia.Narodenie),
        ("родились", DruhVyrocia.Narodenie), ("出生", DruhVyrocia.Narodenie),
        ("úmrti", DruhVyrocia.Umrtie), ("úmrtí", DruhVyrocia.Umrtie), ("zomreli", DruhVyrocia.Umrtie), ("zmarli", DruhVyrocia.Umrtie),
        ("halálozás", DruhVyrocia.Umrtie), ("death", DruhVyrocia.Umrtie), ("скончались", DruhVyrocia.Umrtie),
        ("умерли", DruhVyrocia.Umrtie), ("逝世", DruhVyrocia.Umrtie), ("去世", DruhVyrocia.Umrtie),
    };

    /// <summary>Slová v nadpise alebo texte, podľa ktorých je výročie „domáce“ (týka sa krajiny jazyka).</summary>
    private static readonly Dictionary<string, string[]> DomaceSlova = new()
    {
        ["sk"] = new[] { "sloven", "Sloven" },
        ["cs"] = new[] { "česk", "Česk", "česko", "Česko" },
        ["pl"] = new[] { "polsk", "Polsk", "w Polsce" },
        ["hu"] = new[] { "magyar", "Magyar" },
    };

    /// <summary>Prečíta výročia zo zdrojového textu stránky dňa.</summary>
    public static List<NoveVyrocie> Precitaj(string wikitext, string jazyk)
    {
        var vysledok = new List<NoveVyrocie>();
        var domace = DomaceSlova.GetValueOrDefault(jazyk) ?? Array.Empty<string>();
        DruhVyrocia? druh = null;
        var domacaSekcia = false;
        int? rokNadradeny = null;

        foreach (var surovy in wikitext.Replace("\r", "").Split('\n'))
        {
            var riadok = surovy.Trim();
            if (Nadpis().Match(riadok) is { Success: true } nadpis)
            {
                var uroven = nadpis.Groups[1].Value.Length;
                var text = Vycisti(nadpis.Groups[2].Value).ToLowerInvariant();
                if (uroven <= 2)
                {
                    druh = Sekcie.Where(s => text.Contains(s.Kluc, StringComparison.OrdinalIgnoreCase)).Select(s => (DruhVyrocia?)s.Druh).FirstOrDefault();
                    domacaSekcia = domace.Any(d => text.Contains(d, StringComparison.OrdinalIgnoreCase));
                }
                else if (druh is not null)
                {
                    domacaSekcia = domace.Any(d => text.Contains(d, StringComparison.OrdinalIgnoreCase))
                                   || (domacaSekcia && uroven > 3);
                }

                rokNadradeny = null;
                continue;
            }

            if (druh is null || !riadok.StartsWith('*'))
            {
                continue;
            }

            var hviezdicky = riadok.TakeWhile(z => z == '*').Count();
            var obsah = Vycisti(riadok[hviezdicky..]);
            if (obsah.Length == 0)
            {
                continue;
            }

            int? rok;
            string popis;
            var zhoda = RokNaZaciatku().Match(obsah);
            if (zhoda.Success)
            {
                rok = int.Parse(zhoda.Groups[1].Value);
                popis = zhoda.Groups[2].Value.Trim();
                if (popis.Length == 0)
                {
                    // „* 1822“ a pod ním „** udalosť“
                    rokNadradeny = rok;
                    continue;
                }

                if (hviezdicky == 1)
                {
                    rokNadradeny = null;
                }
            }
            else if (hviezdicky > 1 && rokNadradeny is not null)
            {
                rok = rokNadradeny;
                popis = obsah;
            }
            else
            {
                continue;
            }

            popis = popis.TrimStart('–', '—', '-', ':', '：', ' ').Trim();
            if (popis.Length < 3 || rok is < 1 or > 2200)
            {
                continue;
            }

            if (popis.Length > 600)
            {
                popis = popis[..597].TrimEnd() + "…";
            }

            var jeDomace = domacaSekcia || domace.Any(d => popis.Contains(d, StringComparison.Ordinal));
            vysledok.Add(new NoveVyrocie(rok, druh.Value, popis, jeDomace));
        }

        return vysledok;
    }

    /// <summary>Wikitext → obyčajný text (odkazy, šablóny, poznámky, formátovanie).</summary>
    public static string Vycisti(string text)
    {
        text = Poznamky().Replace(text, "");
        text = Komentare().Replace(text, "");

        // šablóny od najvnútornejších
        for (var i = 0; i < 6 && text.Contains("{{"); i++)
        {
            text = Sablona().Replace(text, m => TextSablony(m.Groups[1].Value));
        }

        text = Varianty().Replace(text, m => VariantTextu(m.Groups[1].Value));
        text = SuborovyOdkaz().Replace(text, "");
        text = Odkaz().Replace(text, m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value);
        text = VonkajsiOdkaz().Replace(text, m => m.Groups[1].Success ? m.Groups[1].Value : "");
        text = text.Replace("'''", "").Replace("''", "");
        text = Znacky().Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
        text = Medzery().Replace(text, " ").Trim();
        return text;
    }

    /// <summary>Čínske varianty „-{zh-hans:…; zh-hant:…}-“ → zjednodušená čínština.</summary>
    private static string VariantTextu(string obsah)
    {
        if (!obsah.Contains(':'))
        {
            return obsah;
        }

        var varianty = obsah.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => v.Split(':', 2))
            .Where(v => v.Length == 2)
            .GroupBy(v => v[0].Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First()[1].Trim());
        return varianty.GetValueOrDefault("zh-hans") ?? varianty.GetValueOrDefault("zh-cn") ?? varianty.Values.FirstOrDefault() ?? "";
    }

    private static string TextSablony(string obsah)
    {
        var casti = obsah.Split('|');
        var nazov = casti[0].Trim().ToLowerInvariant();
        var argumenty = casti.Skip(1).Where(a => !a.Contains('=')).Select(a => a.Trim()).ToList();
        return nazov switch
        {
            "nowrap" or "год" or "le" or "ill" or "lang" when argumenty.Count > 0 => nazov == "lang" ? argumenty[^1] : argumenty[0],
            _ when nazov.StartsWith("lang-") && argumenty.Count > 0 => argumenty[^1],
            "sortname" when argumenty.Count >= 2 => $"{argumenty[0]} {argumenty[1]}",
            "ndash" => "–",
            "mdash" => "—",
            _ => "",
        };
    }

    [GeneratedRegex(@"^(={2,6})\s*(.*?)\s*\1\s*$")]
    private static partial Regex Nadpis();

    // rok na začiatku: „1918 – …“, „1918年：…“, „1822 год — …“; bez oddeľovača = rok pre vnorené riadky
    [GeneratedRegex(@"^(\d{1,4})\s*(?:年|год|г\.)?\s*(?:(?:[–—:：]|-\s|&ndash;)\s*(.*))?$")]
    private static partial Regex RokNaZaciatku();

    [GeneratedRegex(@"<ref[^>]*/>|<ref[^>]*>.*?</ref>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Poznamky();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Komentare();

    [GeneratedRegex(@"-\{(.*?)\}-")]
    private static partial Regex Varianty();

    [GeneratedRegex(@"\{\{([^{}]*)\}\}")]
    private static partial Regex Sablona();

    [GeneratedRegex(@"\[\[(?:Súbor|Subor|Soubor|Plik|Fájl|File|Image|Файл|Изображение|文件|File):[^\]]*(?:\[\[[^\]]*\]\][^\]]*)*\]\]", RegexOptions.IgnoreCase)]
    private static partial Regex SuborovyOdkaz();

    [GeneratedRegex(@"\[\[([^\]|]*)(?:\|([^\]]*))?\]\]")]
    private static partial Regex Odkaz();

    [GeneratedRegex(@"\[https?://\S+(?:\s+([^\]]+))?\]")]
    private static partial Regex VonkajsiOdkaz();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Znacky();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Medzery();

    [GeneratedRegex(@"^#(?:REDIRECT|PRESMERUJ|PŘESMĚRUJ|PATRZ|ÁTIRÁNYÍTÁS|ПЕРЕНАПРАВЛЕНИЕ|重定向)\s*\[\[([^\]|#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Presmerovanie();
}
