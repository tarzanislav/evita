using System.Text;
using System.Text.RegularExpressions;

namespace Evita.Services;

/// <summary>
/// Texty čiarových a QR kódov EviTA a rozpoznanie naskenovaného textu. Čítačka kódov sa správa ako klávesnica –
/// ak je nastavená na americké rozloženie a vo Windows je slovenské alebo české, namiesto číslic „píše“ znaky
/// +ľščťžýáíé. Preto sa každý naskenovaný text skúša aj v opravených podobách.
/// </summary>
public static partial class KodyEvita
{
    public const string Predpona = "EVITA";

    /// <summary>Text kódu na štítku položky, napr. EVITA-123.</summary>
    public static string KodPolozky(int prirastkoveCislo) => $"{Predpona}-{prirastkoveCislo}";

    /// <summary>Prírastkové číslo z kódu štítku EviTA (EVITA-123, evita 123, EVITA/0123 …), inak null.</summary>
    public static int? PrirastkoveZKodu(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var zhoda = KodStitku().Match(text.Trim());
        return zhoda.Success && int.TryParse(zhoda.Groups[1].Value, out var cislo) && cislo > 0 ? cislo : null;
    }

    [GeneratedRegex(@"^EVITA[^0-9A-Z]{0,2}0*([0-9]{1,9})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KodStitku();

    /// <summary>Znaky, ktoré čítačka v americkom režime napíše na slovenskej, českej, maďarskej alebo nemeckej klávesnici.</summary>
    private static readonly Dictionary<char, char> ZnakyKlavesnice = new()
    {
        ['+'] = '1', ['ľ'] = '2', ['ě'] = '2', ['š'] = '3', ['č'] = '4', ['ť'] = '5', ['ř'] = '5', ['ž'] = '6',
        ['ý'] = '7', ['á'] = '8', ['í'] = '9', ['é'] = '0', ['ö'] = '0',
        ['Ľ'] = '2', ['Ě'] = '2', ['Š'] = '3', ['Č'] = '4', ['Ť'] = '5', ['Ř'] = '5', ['Ž'] = '6', ['Ý'] = '7', ['Á'] = '8',
        ['Í'] = '9', ['É'] = '0', ['Ö'] = '0',
        ['='] = '-', ['ü'] = '-', ['ß'] = '-',
    };

    /// <summary>
    /// Podoby naskenovaného textu, ktoré sa skúšajú pri vyhľadávaní: pôvodný text, text s opravenými číslicami
    /// (čítačka v americkom režime na slovenskej klávesnici) a text s vymenenými Y a Z (klávesnica QWERTZ).
    /// </summary>
    public static List<string> Varianty(string? text, IEnumerable<string>? dalsie = null)
    {
        var vysledok = new List<string>();
        void Pridaj(string? t)
        {
            t = Ocisti(t);
            if (t.Length > 0 && !vysledok.Contains(t))
            {
                vysledok.Add(t);
            }
        }

        var zdroje = new List<string?> { text };
        if (dalsie is not null)
        {
            zdroje.AddRange(dalsie);
        }

        foreach (var zdroj in zdroje)
        {
            Pridaj(zdroj);
        }

        foreach (var zdroj in zdroje.ToList())
        {
            var cisla = OpravCislice(zdroj);
            Pridaj(cisla);
            Pridaj(VymenYZ(cisla));
            Pridaj(VymenYZ(zdroj));
        }

        // GTIN (EAN) ukrytý v kóde GS1: „(01)05901234123457(10)…“ alebo webová adresa „…/01/05901234123457/…“
        foreach (var kod in vysledok.ToList())
        {
            Pridaj(GtinZGs1(kod));
        }

        return vysledok;
    }

    /// <summary>Najdlhší spracovaný kód – dlhší text sa skráti (ochrana pred zneužitím čítačky alebo mobilu).</summary>
    public const int NajdlhsiKod = 512;

    /// <summary>Odstráni neviditeľné znaky (Tab, prefix GS1, koniec riadka) a medzery na začiatku a konci.</summary>
    public static string Ocisti(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        if (text.Length > NajdlhsiKod)
        {
            text = text[..NajdlhsiKod];
        }

        var sb = new StringBuilder(text.Length);
        foreach (var znak in text)
        {
            if (!char.IsControl(znak))
            {
                sb.Append(znak);
            }
        }

        return sb.ToString().Trim();
    }

    private static string OpravCislice(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var sb = new StringBuilder(text.Length);
        foreach (var znak in text)
        {
            sb.Append(ZnakyKlavesnice.TryGetValue(znak, out var cislica) ? cislica : znak);
        }

        return sb.ToString();
    }

    private static string VymenYZ(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOfAny(new[] { 'y', 'z', 'Y', 'Z' }) < 0)
        {
            return text ?? "";
        }

        var znaky = text.ToCharArray();
        for (var i = 0; i < znaky.Length; i++)
        {
            znaky[i] = znaky[i] switch
            {
                'y' => 'z',
                'z' => 'y',
                'Y' => 'Z',
                'Z' => 'Y',
                var z => z,
            };
        }

        return new string(znaky);
    }

    /// <summary>GTIN z kódu GS1-128, GS1 DataMatrix/QR alebo z webovej adresy GS1 Digital Link (ako EAN-13), inak null.</summary>
    public static string? GtinZGs1(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var zhoda = Gs1Gtin().Match(text);
        if (!zhoda.Success)
        {
            zhoda = Gs1Adresa().Match(text);
        }

        if (!zhoda.Success)
        {
            return null;
        }

        var gtin = zhoda.Groups[1].Value;
        if (gtin.Length == 14 && gtin[0] == '0')
        {
            gtin = gtin[1..];
        }

        return NormalizujEan(gtin);
    }

    [GeneratedRegex(@"^(?:\][A-Za-z][0-9])?\(?01\)?([0-9]{14})", RegexOptions.CultureInvariant)]
    private static partial Regex Gs1Gtin();

    [GeneratedRegex(@"^https?://[^ ]*?/01/([0-9]{8,14})(?:[/?#]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Gs1Adresa();

    /// <summary>Text je webová adresa (QR kód na krabici často obsahuje len odkaz na stránku výrobcu).</summary>
    public static bool JeAdresa(string? text) =>
        text is not null && (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                             || text.StartsWith("www.", StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ EAN

    /// <summary>
    /// EAN-13, EAN-8, UPC-A (12 číslic) alebo GTIN-14 s platnou kontrolnou číslicou – vráti len číslice, inak null.
    /// Medzery a pomlčky sa ignorujú.
    /// </summary>
    public static string? NormalizujEan(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cislice = new StringBuilder();
        foreach (var znak in text.Trim())
        {
            if (char.IsAsciiDigit(znak))
            {
                cislice.Append(znak);
            }
            else if (znak is not (' ' or '-'))
            {
                return null;
            }
        }

        var ean = cislice.ToString();
        return ean.Length is 8 or 12 or 13 or 14 && MaPlatnuKontrolnuCislicu(ean) ? ean : null;
    }

    public static bool JeEan(string? text) => NormalizujEan(text) is not null;

    /// <summary>Kontrolná číslica GTIN (váhy 3 a 1 sprava).</summary>
    public static bool MaPlatnuKontrolnuCislicu(string cislice)
    {
        if (cislice.Length < 2 || !cislice.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sucet = 0;
        var vaha = 3;
        for (var i = cislice.Length - 2; i >= 0; i--)
        {
            sucet += (cislice[i] - '0') * vaha;
            vaha = vaha == 3 ? 1 : 3;
        }

        var kontrolna = (10 - sucet % 10) % 10;
        return cislice[^1] - '0' == kontrolna;
    }
}
