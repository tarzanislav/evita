using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using ClosedXML.Excel;
using Evita.Models;

namespace Evita.Services;

/// <summary>Obsah súboru na import: hlavičky stĺpcov, riadky a pri exporte z EviTA aj číselníky a história.</summary>
public sealed class ZdrojImportu
{
    public string Cesta { get; init; } = "";
    public string Format { get; init; } = "";
    public List<string> Hlavicky { get; } = new();
    public List<string?[]> Riadky { get; } = new();

    /// <summary>Číslo riadku v pôvodnom súbore pre každý riadok (na hlásenia „riadok 12: …“).</summary>
    public List<int> CislaRiadkov { get; } = new();

    /// <summary>Úplný export z EviTA (JSON alebo XML) – číselníky a história. Pri tabuľkách null.</summary>
    public BalikEviTA? Balik { get; set; }

    public List<string> Upozornenia { get; } = new();

    public string NazovSuboru => Path.GetFileName(Cesta);

    /// <summary>Prvá neprázdna hodnota v stĺpci – ukážka v sprievodcovi.</summary>
    public string Ukazka(int stlpec) =>
        Riadky.Select(r => stlpec < r.Length ? r[stlpec] : null).FirstOrDefault(h => !string.IsNullOrWhiteSpace(h)) ?? "";
}

/// <summary>Načíta súbor Excel, CSV, JSON alebo XML do jednotnej podoby tabuľky.</summary>
public static class CitacSuborov
{
    public static string FilterSuborov =>
        Preklad.T("Všetky podporované (*.xlsx;*.csv;*.txt;*.json;*.xml)|*.xlsx;*.csv;*.txt;*.json;*.xml|Excel (*.xlsx)|*.xlsx|CSV (*.csv;*.txt)|*.csv;*.txt|JSON (*.json)|*.json|XML (*.xml)|*.xml");

    static CitacSuborov()
    {
        // kódovanie Windows-1250 pre staršie CSV súbory zo slovenských programov
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Načíta súbor; pri importe číselníka (kategórie, miestnosti, osoby) sa v Exceli hľadá hárok
    /// s názvom číselníka a zo súboru EviTA (JSON, XML) sa vezme zoznam číselníka.</summary>
    public static ZdrojImportu Nacitaj(string cesta, TypCiselnika? ciselnik = null)
    {
        _ciselnik = ciselnik;
        try
        {
            var zdroj = NacitajSubor(cesta);
            if (ciselnik is { } typ && zdroj.Balik is { } balik)
            {
                PolaCiselnika.NaplnZBaliku(zdroj, balik, typ);
            }

            return zdroj;
        }
        finally
        {
            _ciselnik = null;
        }
    }

    [ThreadStatic] private static TypCiselnika? _ciselnik;

    private static bool JeHlavicka(string? text) =>
        _ciselnik is { } typ ? PolaCiselnika.Rozpoznaj(typ, text) is not null : StlpcePrenosu.Rozpoznaj(text) is not null;

    private static ZdrojImportu NacitajSubor(string cesta)
    {
        if (!File.Exists(cesta))
        {
            throw new EvitaException(Preklad.F($"Súbor neexistuje:\n{cesta}"));
        }

        var pripona = Path.GetExtension(cesta).ToLowerInvariant();
        try
        {
            var zdroj = pripona switch
            {
                ".xlsx" or ".xlsm" => Excel(cesta),
                ".csv" or ".txt" => Csv(cesta),
                ".json" => Json(cesta),
                ".xml" => Xml(cesta),
                ".xls" => throw new EvitaException(Preklad.T("Starý formát Excelu .xls sa nedá načítať. Otvorte súbor v Exceli a uložte ho ako .xlsx (Súbor → Uložiť ako → Zošit programu Excel).")),
                ".zip" => throw new EvitaException(Preklad.T("Zálohu EviTA (ZIP) obnovíte v časti Servis → Obnova.")),
                _ => throw new EvitaException(Preklad.F($"Formát súboru {pripona} sa nedá importovať. Použite Excel (.xlsx), CSV, JSON alebo XML.")),
            };

            OrezPrazdne(zdroj);
            return zdroj;
        }
        catch (EvitaException)
        {
            throw;
        }
        catch (IOException ex)
        {
            throw new EvitaException(Preklad.F($"Súbor sa nedá otvoriť. Ak je otvorený v inom programe, zatvorte ho a skúste znova.\n\n{ex.Message}"));
        }
        catch (Exception ex)
        {
            throw new EvitaException(Preklad.F($"Súbor sa nepodarilo prečítať: {ex.GetBaseException().Message}"));
        }
    }

    // ------------------------------------------------------------------ Excel

    private static ZdrojImportu Excel(string cesta)
    {
        using var prud = new FileStream(cesta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zosit = new XLWorkbook(prud);
        var harky = zosit.Worksheets.Where(h => h.Visibility == XLWorksheetVisibility.Visible).ToList();
        if (harky.Count == 0)
        {
            throw new EvitaException(Preklad.T("Zošit neobsahuje žiadny viditeľný hárok."));
        }

        var harok = (_ciselnik is { } typ
                        ? harky.FirstOrDefault(h => PolaCiselnika.JeHarok(typ, h.Name))
                        : harky.FirstOrDefault(h => StlpcePrenosu.Normalizuj(h.Name) == "polozky"))
                    ?? harky[0];
        var zdroj = new ZdrojImportu { Cesta = cesta, Format = "Excel" };
        if (harky.Count > 1)
        {
            zdroj.Upozornenia.Add(Preklad.F($"Zošit má {harky.Count} hárkov, načítal sa hárok „{harok.Name}“."));
        }

        var poslednyRiadok = harok.LastRowUsed()?.RowNumber() ?? 0;
        var poslednyStlpec = harok.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (poslednyRiadok == 0 || poslednyStlpec == 0)
        {
            return zdroj;
        }

        // hlavička = prvý riadok (z prvých desiatich), v ktorom je rozpoznaný niektorý stĺpec; inak prvý neprázdny
        var riadokHlavicky = 0;
        for (var r = 1; r <= Math.Min(10, poslednyRiadok); r++)
        {
            var hodnoty = Enumerable.Range(1, poslednyStlpec).Select(c => TextBunky(harok.Cell(r, c))).ToList();
            if (riadokHlavicky == 0 && hodnoty.Any(h => !string.IsNullOrWhiteSpace(h)))
            {
                riadokHlavicky = r;
            }

            if (hodnoty.Any(JeHlavicka))
            {
                riadokHlavicky = r;
                break;
            }
        }

        if (riadokHlavicky == 0)
        {
            return zdroj;
        }

        for (var c = 1; c <= poslednyStlpec; c++)
        {
            zdroj.Hlavicky.Add(TextBunky(harok.Cell(riadokHlavicky, c))?.Trim() is { Length: > 0 } h ? h : Preklad.F($"Stĺpec {c}"));
        }

        for (var r = riadokHlavicky + 1; r <= poslednyRiadok; r++)
        {
            var riadok = new string?[poslednyStlpec];
            for (var c = 1; c <= poslednyStlpec; c++)
            {
                riadok[c - 1] = TextBunky(harok.Cell(r, c));
            }

            zdroj.Riadky.Add(riadok);
            zdroj.CislaRiadkov.Add(r);
        }

        return zdroj;
    }

    private static string? TextBunky(IXLCell bunka)
    {
        var hodnota = bunka.Value;
        if (hodnota.IsBlank)
        {
            return null;
        }

        if (hodnota.IsDateTime)
        {
            var d = hodnota.GetDateTime();
            return d.TimeOfDay == TimeSpan.Zero
                ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (hodnota.IsNumber)
        {
            return hodnota.GetNumber().ToString("0.############", CultureInfo.InvariantCulture);
        }

        if (hodnota.IsBoolean)
        {
            return hodnota.GetBoolean() ? Preklad.T("áno") : Preklad.T("nie");
        }

        if (hodnota.IsText)
        {
            return hodnota.GetText();
        }

        return bunka.GetFormattedString();
    }

    // ------------------------------------------------------------------ CSV

    private static ZdrojImportu Csv(string cesta)
    {
        var bajty = File.ReadAllBytes(cesta);
        var text = Dekoduj(bajty, out var kodovanie);
        var zdroj = new ZdrojImportu { Cesta = cesta, Format = "CSV" };
        if (kodovanie != "UTF-8")
        {
            zdroj.Upozornenia.Add(Preklad.F($"Súbor nie je v kódovaní UTF-8, načítal sa v kódovaní {kodovanie}."));
        }

        var oddelovac = UrciOddelovac(text);
        var zaznamy = RozdelCsv(text, oddelovac);
        var hlavicka = zaznamy.FindIndex(z => z.Any(h => !string.IsNullOrWhiteSpace(h)));
        if (hlavicka < 0)
        {
            return zdroj;
        }

        var pocet = zaznamy.Skip(hlavicka).Max(z => z.Count);
        for (var c = 0; c < pocet; c++)
        {
            zdroj.Hlavicky.Add(c < zaznamy[hlavicka].Count && zaznamy[hlavicka][c].Trim() is { Length: > 0 } h ? h : Preklad.F($"Stĺpec {c + 1}"));
        }

        for (var z = hlavicka + 1; z < zaznamy.Count; z++)
        {
            var zaznam = zaznamy[z];
            var riadok = new string?[pocet];
            for (var c = 0; c < pocet; c++)
            {
                riadok[c] = c < zaznam.Count ? zaznam[c] : null;
            }

            zdroj.Riadky.Add(riadok);
            zdroj.CislaRiadkov.Add(z + 1);
        }

        return zdroj;
    }

    private static string Dekoduj(byte[] bajty, out string kodovanie)
    {
        if (bajty.Length >= 3 && bajty[0] == 0xEF && bajty[1] == 0xBB && bajty[2] == 0xBF)
        {
            kodovanie = "UTF-8";
            return Encoding.UTF8.GetString(bajty, 3, bajty.Length - 3);
        }

        if (bajty.Length >= 2 && bajty[0] == 0xFF && bajty[1] == 0xFE)
        {
            kodovanie = "UTF-16";
            return Encoding.Unicode.GetString(bajty, 2, bajty.Length - 2);
        }

        try
        {
            kodovanie = "UTF-8";
            return new UTF8Encoding(false, true).GetString(bajty);
        }
        catch (DecoderFallbackException)
        {
            kodovanie = "Windows-1250";
            return Encoding.GetEncoding(1250).GetString(bajty);
        }
    }

    private static char UrciOddelovac(string text)
    {
        var koniec = text.IndexOf('\n');
        var prvy = koniec < 0 ? text : text[..koniec];
        var kandidati = new[] { ';', ',', '\t', '|' };
        var vonku = true;
        var pocty = new int[kandidati.Length];
        foreach (var znak in prvy)
        {
            if (znak == '"')
            {
                vonku = !vonku;
            }
            else if (vonku)
            {
                var i = Array.IndexOf(kandidati, znak);
                if (i >= 0)
                {
                    pocty[i]++;
                }
            }
        }

        var najviac = Array.IndexOf(pocty, pocty.Max());
        return pocty[najviac] == 0 ? ';' : kandidati[najviac];
    }

    /// <summary>Rozdelí CSV na záznamy (úvodzovky, zdvojené úvodzovky aj zalomenie riadku vo vnútri hodnoty).</summary>
    internal static List<List<string>> RozdelCsv(string text, char oddelovac)
    {
        var zaznamy = new List<List<string>>();
        var zaznam = new List<string>();
        var hodnota = new StringBuilder();
        var vUvodzovkach = false;

        for (var i = 0; i < text.Length; i++)
        {
            var znak = text[i];
            if (vUvodzovkach)
            {
                if (znak == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        hodnota.Append('"');
                        i++;
                    }
                    else
                    {
                        vUvodzovkach = false;
                    }
                }
                else
                {
                    hodnota.Append(znak);
                }

                continue;
            }

            if (znak == '"' && hodnota.ToString().Trim().Length == 0)
            {
                hodnota.Clear();
                vUvodzovkach = true;
            }
            else if (znak == oddelovac)
            {
                zaznam.Add(hodnota.ToString());
                hodnota.Clear();
            }
            else if (znak is '\r' or '\n')
            {
                if (znak == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                zaznam.Add(hodnota.ToString());
                hodnota.Clear();
                zaznamy.Add(zaznam);
                zaznam = new List<string>();
            }
            else
            {
                hodnota.Append(znak);
            }
        }

        if (hodnota.Length > 0 || zaznam.Count > 0)
        {
            zaznam.Add(hodnota.ToString());
            zaznamy.Add(zaznam);
        }

        return zaznamy;
    }

    // ------------------------------------------------------------------ JSON

    private static readonly JsonSerializerOptions MoznostiJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static ZdrojImportu Json(string cesta)
    {
        var text = File.ReadAllText(cesta);
        using var dokument = JsonDocument.Parse(text, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var zdroj = new ZdrojImportu { Cesta = cesta, Format = "JSON" };
        var koren = dokument.RootElement;
        JsonElement? zoznam = null;

        if (koren.ValueKind == JsonValueKind.Array)
        {
            zoznam = koren;
        }
        else if (koren.ValueKind == JsonValueKind.Object)
        {
            if (NajdiVlastnost(koren, "polozky") is { ValueKind: JsonValueKind.Array } polozky)
            {
                zoznam = polozky;
                try
                {
                    zdroj.Balik = JsonSerializer.Deserialize<BalikEviTA>(text, MoznostiJson);
                }
                catch (JsonException ex)
                {
                    zdroj.Upozornenia.Add(Preklad.F($"Číselníky alebo história v súbore sa nedali prečítať ({ex.Message}). Načítajú sa len položky."));
                }
            }
            else
            {
                // jediný zoznam objektov v koreni (napr. {"zariadenia": [ ... ]})
                zoznam = koren.EnumerateObject().Select(v => v.Value)
                    .FirstOrDefault(v => v.ValueKind == JsonValueKind.Array);
            }
        }

        if (zoznam is not { } pole || pole.ValueKind != JsonValueKind.Array)
        {
            throw new EvitaException(Preklad.T("V súbore JSON sa nenašiel zoznam položiek. Pozrite si vzorový súbor EviTA_vzor_JSON.json."));
        }

        var objekty = pole.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.Object)
            .Select(o => o.EnumerateObject().Select(v => (v.Name, Hodnota: TextJson(v.Value))).ToList())
            .ToList();
        NaplnZDvojic(zdroj, objekty);
        return zdroj;
    }

    private static JsonElement? NajdiVlastnost(JsonElement objekt, string nazov)
    {
        foreach (var vlastnost in objekt.EnumerateObject())
        {
            if (string.Equals(vlastnost.Name, nazov, StringComparison.OrdinalIgnoreCase))
            {
                return vlastnost.Value;
            }
        }

        return null;
    }

    private static string? TextJson(JsonElement hodnota) => hodnota.ValueKind switch
    {
        JsonValueKind.String => hodnota.GetString(),
        JsonValueKind.Number => hodnota.GetRawText(),
        JsonValueKind.True => Preklad.T("áno"),
        JsonValueKind.False => Preklad.T("nie"),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => hodnota.GetRawText(),
    };

    // ------------------------------------------------------------------ XML

    private static ZdrojImportu Xml(string cesta)
    {
        var dokument = XDocument.Load(cesta);
        var koren = dokument.Root ?? throw new EvitaException(Preklad.T("Súbor XML je prázdny."));
        var zdroj = new ZdrojImportu { Cesta = cesta, Format = "XML" };

        List<XElement> prvky;
        if (koren.Name.LocalName == "EviTA")
        {
            Over(dokument, zdroj);
            zdroj.Balik = BalikZXml(koren);
            prvky = koren.Element("Polozky")?.Elements("Polozka").ToList() ?? new List<XElement>();
        }
        else
        {
            // všeobecné XML: zoznam rovnakých prvkov v koreni alebo o úroveň nižšie
            prvky = NajdiOpakovane(koren);
            if (prvky.Count == 0)
            {
                throw new EvitaException(Preklad.T("V súbore XML sa nenašiel zoznam položiek. Pozrite si vzorový súbor EviTA_vzor_XML.xml."));
            }
        }

        var objekty = prvky.Select(p =>
                p.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => (a.Name.LocalName, Hodnota: (string?)a.Value))
                    .Concat(p.Elements().Select(e => (e.Name.LocalName, Hodnota: (string?)e.Value)))
                    .ToList())
            .ToList();
        NaplnZDvojic(zdroj, objekty);
        return zdroj;
    }

    private static List<XElement> NajdiOpakovane(XElement koren)
    {
        var deti = koren.Elements().ToList();
        if (deti.Count > 0 && deti.All(d => d.Name == deti[0].Name) && (deti.Count > 1 || deti[0].HasElements || deti[0].HasAttributes))
        {
            if (deti.Count == 1 && deti[0].Elements().Count() > 1 && deti[0].Elements().All(e => e.HasElements))
            {
                return deti[0].Elements().ToList();
            }

            return deti;
        }

        return deti.Select(d => d.Elements().ToList())
            .Where(z => z.Count > 0 && z.All(e => e.Name == z[0].Name))
            .OrderByDescending(z => z.Count)
            .FirstOrDefault() ?? new List<XElement>();
    }

    /// <summary>Overí súbor podľa popisu formátu (evita.xsd). Odchýlky sú len upozornenia – import ich zvládne.</summary>
    private static void Over(XDocument dokument, ZdrojImportu zdroj)
    {
        try
        {
            var schemy = new XmlSchemaSet();
            using (var citac = XmlReader.Create(new StringReader(XsdEviTA.Text)))
            {
                schemy.Add(null, citac);
            }

            var pocet = 0;
            dokument.Validate(schemy, (_, e) =>
            {
                pocet++;
                if (pocet <= 3)
                {
                    zdroj.Upozornenia.Add(Preklad.F($"Súbor sa odchyľuje od popisu formátu evita.xsd: {e.Message}"));
                }
            });
            if (pocet > 3)
            {
                zdroj.Upozornenia.Add(Preklad.F($"… a ďalších {pocet - 3} odchýlok od popisu formátu."));
            }
        }
        catch (Exception ex)
        {
            zdroj.Upozornenia.Add(Preklad.F($"Kontrola podľa evita.xsd sa nedala vykonať: {ex.Message}"));
        }
    }

    private static BalikEviTA BalikZXml(XElement koren)
    {
        static string? A(XElement e, string nazov) => e.Attribute(nazov)?.Value;
        static bool Aktivna(XElement e) => A(e, "aktivna") is not { } a || a is "true" or "1";

        var balik = new BalikEviTA
        {
            Program = A(koren, "program"),
            Vytvorene = A(koren, "vytvorene"),
            Pocitac = A(koren, "pocitac"),
            PoslednePrirastkoveCislo = int.TryParse(A(koren, "poslednePrirastkoveCislo"), out var p) ? p : null,
        };

        balik.Kategorie = koren.Element("Kategorie")?.Elements("Kategoria")
            .Select(e => new KategoriaPrenos { Nazov = A(e, "nazov") ?? "", Aktivna = Aktivna(e) }).ToList() ?? new();
        balik.Miestnosti = koren.Element("Miestnosti")?.Elements("Miestnost")
            .Select(e => new MiestnostPrenos { Nazov = A(e, "nazov") ?? "", Budova = A(e, "budova"), Poznamka = A(e, "poznamka"), Aktivna = Aktivna(e) })
            .ToList() ?? new();
        balik.Osoby = koren.Element("Osoby")?.Elements("Osoba")
            .Select(e => new OsobaPrenos { Meno = A(e, "meno") ?? "", Priezvisko = A(e, "priezvisko") ?? "", Funkcia = A(e, "funkcia"), Aktivna = Aktivna(e) })
            .ToList() ?? new();
        balik.Historia = koren.Element("Historia")?.Elements("Zmena")
            .Select(e => new ZmenaPrenos
            {
                GuidPolozky = A(e, "guidPolozky"),
                PrirastkoveCislo = int.TryParse(A(e, "prirastkoveCislo"), out var c) ? c : 0,
                NazovPolozky = A(e, "nazovPolozky") ?? "",
                Cas = A(e, "cas") ?? "",
                Pouzivatel = A(e, "pouzivatel") ?? "",
                Akcia = A(e, "akcia") ?? "",
                Pole = A(e, "pole"),
                StaraHodnota = A(e, "staraHodnota"),
                NovaHodnota = A(e, "novaHodnota"),
            })
            .ToList();
        balik.TypyUdalosti = koren.Element("TypyUdalosti")?.Elements("TypUdalosti")
            .Select(e => new TypUdalostiPrenos { Nazov = A(e, "nazov") ?? "", Farba = A(e, "farba") ?? "#7F8C8D", Aktivny = A(e, "aktivny") is not "false" and not "0" })
            .ToList();
        balik.Udalosti = koren.Element("Udalosti")?.Elements("Udalost")
            .Select(e => new UdalostPrenos
            {
                Guid = A(e, "guid"),
                Nazov = A(e, "nazov") ?? "",
                Popis = e.Element("Popis")?.Value,
                Typ = A(e, "typ") ?? "",
                Druh = A(e, "druh") ?? "Udalost",
                Zaciatok = A(e, "zaciatok") ?? "",
                Koniec = A(e, "koniec"),
                CelyDen = A(e, "celyDen") is "true" or "1",
                Opakovanie = A(e, "opakovanie") ?? "Ziadne",
                OpakovanieDo = A(e, "opakovanieDo"),
                Pripomienka = A(e, "pripomienka") ?? "Ziadna",
                PolozkaGuid = A(e, "polozkaGuid"),
                PolozkaCislo = int.TryParse(A(e, "polozkaCislo"), out var pc) ? pc : null,
                Miestnost = A(e, "miestnost"),
                Osoba = A(e, "osoba"),
                Stav = A(e, "stav") ?? "Naplanovane",
            })
            .ToList();
        balik.Ulohy = koren.Element("Ulohy")?.Elements("Uloha")
            .Select(e => new UlohaPrenos
            {
                Guid = A(e, "guid"),
                Text = A(e, "text") ?? "",
                Popis = e.Element("Popis")?.Value,
                Termin = A(e, "termin"),
                Priorita = A(e, "priorita") ?? "Normalna",
                Hotovo = A(e, "hotovo") is "true" or "1",
                DatumHotovo = A(e, "datumHotovo"),
                Vytvorene = A(e, "vytvorene"),
                PolozkaGuid = A(e, "polozkaGuid"),
                PolozkaCislo = int.TryParse(A(e, "polozkaCislo"), out var uc) ? uc : null,
            })
            .ToList();
        balik.Poznamky = koren.Element("Poznamky")?.Elements("Poznamka")
            .Select(e => new PoznamkaPrenos
            {
                Guid = A(e, "guid"),
                Nazov = A(e, "nazov") ?? "",
                Text = e.Element("Text")?.Value ?? "",
                Farba = A(e, "farba") ?? "#FFF59D",
                Pripnuta = A(e, "pripnuta") is "true" or "1",
                Vytvorene = A(e, "vytvorene"),
                Zmenene = A(e, "zmenene"),
                Datum = A(e, "datum"),
                PolozkaGuid = A(e, "polozkaGuid"),
                PolozkaCislo = int.TryParse(A(e, "polozkaCislo"), out var nc) ? nc : null,
                UdalostGuid = A(e, "udalostGuid"),
            })
            .ToList();
        return balik;
    }

    // ------------------------------------------------------------------ spoločné

    /// <summary>Zo zoznamu objektov (názov → hodnota) urobí tabuľku; stĺpce v poradí, v akom sa prvýkrát objavia.</summary>
    private static void NaplnZDvojic(ZdrojImportu zdroj, List<List<(string Nazov, string? Hodnota)>> objekty)
    {
        var indexy = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var objekt in objekty)
        {
            foreach (var (nazov, _) in objekt)
            {
                if (!indexy.ContainsKey(nazov))
                {
                    indexy[nazov] = zdroj.Hlavicky.Count;
                    zdroj.Hlavicky.Add(nazov);
                }
            }
        }

        for (var i = 0; i < objekty.Count; i++)
        {
            var riadok = new string?[zdroj.Hlavicky.Count];
            foreach (var (nazov, hodnota) in objekty[i])
            {
                riadok[indexy[nazov]] = hodnota;
            }

            zdroj.Riadky.Add(riadok);
            zdroj.CislaRiadkov.Add(i + 1);
        }
    }

    /// <summary>Oreže medzery a vynechá úplne prázdne riadky.</summary>
    private static void OrezPrazdne(ZdrojImportu zdroj)
    {
        for (var i = zdroj.Riadky.Count - 1; i >= 0; i--)
        {
            var riadok = zdroj.Riadky[i];
            for (var c = 0; c < riadok.Length; c++)
            {
                riadok[c] = string.IsNullOrWhiteSpace(riadok[c]) ? null : riadok[c]!.Trim();
            }

            if (riadok.All(h => h is null))
            {
                zdroj.Riadky.RemoveAt(i);
                zdroj.CislaRiadkov.RemoveAt(i);
            }
        }
    }
}
