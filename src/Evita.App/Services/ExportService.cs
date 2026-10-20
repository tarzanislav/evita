using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using ClosedXML.Excel;
using Evita.Data;
using Evita.Models;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

public enum FormatExportu
{
    Excel,
    Csv,
    Json,
    Xml,
    Html,
    Pdf,
}

public sealed record PopisFormatu(FormatExportu Format, string Nazov, string Popis);

/// <summary>Čo a kam exportovať. IdPoloziek null = celá evidencia.</summary>
public sealed record MoznostiExportu(
    FormatExportu Format,
    string Cesta,
    IReadOnlyCollection<int>? IdPoloziek,
    IReadOnlyList<string> Stlpce,
    bool SHistoriou = false,
    bool HarokPreKategoriu = false,
    string? Popis = null);

/// <summary>Export evidencie do Excelu, CSV, JSON, XML, HTML a PDF.</summary>
public sealed class ExportService
{
    public const string KlucStlpce = "Export.Stlpce";
    public static string Titulok => Preklad.T("Evidencia IT techniky");

    public static IReadOnlyList<PopisFormatu> Formaty => new PopisFormatu[]
    {
        new(FormatExportu.Excel, "Excel (.xlsx)", Preklad.T("Ďalšia práca v Exceli – formátovaná hlavička, zapnutý filter, ukotvený prvý riadok, dátumy a sumy ako čísla.")),
        new(FormatExportu.Csv, "CSV (.csv)", Preklad.T("Prenos do iných programov – UTF-8 s diakritikou, oddeľovač bodkočiarka, slovenský Excel ho otvorí správne.")),
        new(FormatExportu.Json, "JSON (.json)", Preklad.T("Výmena dát a prenos do inej EviTA – úplná štruktúra vrátane číselníkov, voliteľne aj s históriou zmien.")),
        new(FormatExportu.Xml, "XML (.xml)", Preklad.T("Výmena dát s inými systémami – rovnaká štruktúra ako JSON, vedľa sa uloží popis formátu evita.xsd.")),
        new(FormatExportu.Html, "HTML (.html)", Preklad.T("Prezeranie v prehliadači alebo zverejnenie – jeden súbor s formátovanou tabuľkou, dá sa aj vytlačiť.")),
        new(FormatExportu.Pdf, "PDF (.pdf)", Preklad.T("Archivácia a odoslanie – tabuľka na stránkach A4 s číslami strán.")),
    };

    public static string Pripona(FormatExportu format) => format switch
    {
        FormatExportu.Excel => ".xlsx",
        FormatExportu.Csv => ".csv",
        FormatExportu.Json => ".json",
        FormatExportu.Xml => ".xml",
        FormatExportu.Html => ".html",
        _ => ".pdf",
    };

    public static string Filter(FormatExportu format) => format switch
    {
        FormatExportu.Excel => "Excel (*.xlsx)|*.xlsx",
        FormatExportu.Csv => "CSV (*.csv)|*.csv",
        FormatExportu.Json => "JSON (*.json)|*.json",
        FormatExportu.Xml => "XML (*.xml)|*.xml",
        FormatExportu.Html => "HTML (*.html)|*.html",
        _ => "PDF (*.pdf)|*.pdf",
    };

    /// <summary>Tabuľkové formáty majú voľbu stĺpcov; JSON a XML obsahujú vždy úplnú štruktúru.</summary>
    public static bool MaVolbuStlpcov(FormatExportu format) => format is not (FormatExportu.Json or FormatExportu.Xml);

    // ------------------------------------------------------------------ uložené stĺpce

    public IReadOnlyList<string> NacitajStlpce()
    {
        using var db = Databaza.Otvor();
        var ulozene = Databaza.Citaj(db, KlucStlpce);
        if (string.IsNullOrWhiteSpace(ulozene))
        {
            return StlpcePrenosu.Vsetky.Where(s => s.PredvolenyExport).Select(s => s.Kluc).ToList();
        }

        var znamy = StlpcePrenosu.Vsetky.Select(s => s.Kluc).ToHashSet();
        return ulozene.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(znamy.Contains).ToList();
    }

    public void UlozStlpce(IEnumerable<string> stlpce)
    {
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucStlpce, string.Join(',', stlpce));
        db.SaveChanges();
    }

    // ------------------------------------------------------------------ export

    /// <summary>Vyexportuje položky do súboru. Vráti počet vyexportovaných položiek.</summary>
    public int Exportuj(MoznostiExportu moznosti)
    {
        var stlpce = StlpcePrenosu.Vsetky.Where(s => moznosti.Stlpce.Contains(s.Kluc)).ToList();
        if (MaVolbuStlpcov(moznosti.Format) && stlpce.Count == 0)
        {
            throw new EvitaException(Preklad.T("Vyberte aspoň jeden stĺpec."));
        }

        var priecinok = Path.GetDirectoryName(Path.GetFullPath(moznosti.Cesta));
        if (!string.IsNullOrEmpty(priecinok))
        {
            Directory.CreateDirectory(priecinok);
        }

        using var db = Databaza.Otvor();
        var polozky = NacitajPolozky(db, moznosti.IdPoloziek);
        var popis = PopisExportu(moznosti.Popis, polozky.Count);

        try
        {
            switch (moznosti.Format)
            {
                case FormatExportu.Excel:
                {
                    using var zosit = new XLWorkbook();
                    PridajHarok(zosit, Preklad.T("Položky"), stlpce, Riadky(polozky, stlpce));
                    if (moznosti.HarokPreKategoriu)
                    {
                        foreach (var skupina in polozky.GroupBy(p => p.Kategoria?.Nazov ?? Preklad.T("nezaradené"))
                                     .OrderBy(g => g.Key, Sk.Porovnanie))
                        {
                            PridajHarok(zosit, skupina.Key, stlpce, Riadky(skupina.ToList(), stlpce));
                        }
                    }

                    zosit.SaveAs(moznosti.Cesta);
                    break;
                }

                case FormatExportu.Csv:
                    ZapisCsv(moznosti.Cesta, stlpce, Riadky(polozky, stlpce));
                    break;
                case FormatExportu.Json:
                    ZapisJson(moznosti.Cesta, VytvorBalik(db, polozky, moznosti.SHistoriou, moznosti.IdPoloziek is null));
                    break;
                case FormatExportu.Xml:
                    ZapisXml(moznosti.Cesta, VytvorBalik(db, polozky, moznosti.SHistoriou, moznosti.IdPoloziek is null));
                    break;
                case FormatExportu.Html:
                    ZapisHtml(moznosti.Cesta, Titulok, popis, stlpce, polozky);
                    break;
                default:
                    PdfExport.Zapis(moznosti.Cesta, Titulok, popis, stlpce, polozky);
                    break;
            }
        }
        catch (IOException ex)
        {
            throw new EvitaException(Preklad.F($"Súbor sa nedá uložiť. Ak je otvorený v inom programe (napr. v Exceli), zatvorte ho a skúste znova.\n\n{ex.Message}"));
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new EvitaException(Preklad.F($"Do zvoleného priečinka nemáte právo zapisovať. Vyberte iný priečinok.\n\n{ex.Message}"));
        }

        return polozky.Count;
    }

    internal static List<Polozka> NacitajPolozky(EvitaDbContext db, IReadOnlyCollection<int>? idPoloziek)
    {
        var dotaz = db.Polozky.AsNoTracking()
            .Include(p => p.Kategoria)
            .Include(p => p.Miestnost)
            .Include(p => p.Osoba)
            .AsQueryable();

        if (idPoloziek is not null)
        {
            var zoznam = idPoloziek.Distinct().ToList();
            dotaz = dotaz.Where(p => zoznam.Contains(p.Id));
        }

        return dotaz.OrderBy(p => p.PrirastkoveCislo).ToList();
    }

    private static string PopisExportu(string? popis, int pocet)
    {
        var casti = new List<string>();
        if (!string.IsNullOrWhiteSpace(popis))
        {
            casti.Add(popis.Trim());
        }

        casti.Add(PocetPoloziek(pocet));
        casti.Add(Preklad.F($"vytvorené {Sk.DatumCas(DateTime.Now)}"));
        return string.Join(" · ", casti);
    }

    public static string PocetPoloziek(int pocet) => Preklad.Pocet(pocet, "{0} položka", "{0} položky", "{0} položiek");

    internal static List<object?[]> Riadky(IEnumerable<Polozka> polozky, IReadOnlyList<StlpecPrenosu> stlpce) =>
        polozky.Select(p => stlpce.Select(s => StlpcePrenosu.Hodnota(p, s.Kluc)).ToArray()).ToList();

    // ------------------------------------------------------------------ Excel

    /// <summary>Hárok s tabuľkou: tučná hlavička s farbou, filter, ukotvený prvý riadok, dátumy a sumy ako čísla.</summary>
    internal static IXLWorksheet PridajHarok(XLWorkbook zosit, string nazov, IReadOnlyList<StlpecPrenosu> stlpce, IReadOnlyList<object?[]> riadky)
    {
        var harok = zosit.Worksheets.Add(NazovHarku(zosit, nazov));
        for (var c = 0; c < stlpce.Count; c++)
        {
            harok.Cell(1, c + 1).Value = stlpce[c].Nazov;
        }

        for (var r = 0; r < riadky.Count; r++)
        {
            for (var c = 0; c < stlpce.Count; c++)
            {
                var bunka = harok.Cell(r + 2, c + 1);
                switch (riadky[r][c])
                {
                    case null:
                        break;
                    case int i:
                        bunka.Value = (double)i;
                        break;
                    case decimal d:
                        bunka.Value = (double)d;
                        break;
                    case DateTime dt:
                        bunka.Value = dt;
                        break;
                    case string s:
                        bunka.Value = s;
                        break;
                    case { } ine:
                        bunka.Value = ine.ToString() ?? "";
                        break;
                }
            }
        }

        var posledny = Math.Max(2, riadky.Count + 1);
        for (var c = 0; c < stlpce.Count; c++)
        {
            var stlpec = harok.Column(c + 1);
            stlpec.Width = Math.Clamp(Math.Max(stlpce[c].Nazov.Length + 3, stlpce[c].Sirka * 6.5), 10, 60);
            var format = stlpce[c].Typ switch
            {
                TypStlpca.Datum => "d.m.yyyy",
                TypStlpca.DatumCas => "d.m.yyyy h:mm",
                TypStlpca.Suma => "#,##0.00 \"€\"",
                TypStlpca.CeleCislo => "0",
                _ => null,
            };
            if (format is not null)
            {
                harok.Range(2, c + 1, posledny, c + 1).Style.NumberFormat.Format = format;
            }
        }

        var hlavicka = harok.Range(1, 1, 1, stlpce.Count);
        hlavicka.Style.Font.Bold = true;
        hlavicka.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
        hlavicka.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        harok.Range(1, 1, riadky.Count + 1, stlpce.Count).SetAutoFilter();
        harok.SheetView.FreezeRows(1);
        return harok;
    }

    private static string NazovHarku(XLWorkbook zosit, string nazov)
    {
        var sb = new StringBuilder();
        foreach (var znak in nazov)
        {
            sb.Append(znak is '[' or ']' or ':' or '*' or '?' or '/' or '\\' ? '_' : znak);
        }

        var zaklad = sb.ToString().Trim('\'', ' ');
        if (zaklad.Length == 0)
        {
            zaklad = Preklad.T("Hárok");
        }

        zaklad = zaklad.Length > 31 ? zaklad[..31] : zaklad;
        var vysledok = zaklad;
        var poradie = 2;
        while (zosit.Worksheets.Any(h => string.Equals(h.Name, vysledok, StringComparison.OrdinalIgnoreCase)))
        {
            var pripona = $" ({poradie++})";
            vysledok = (zaklad.Length + pripona.Length > 31 ? zaklad[..(31 - pripona.Length)] : zaklad) + pripona;
        }

        return vysledok;
    }

    // ------------------------------------------------------------------ CSV

    internal static void ZapisCsv(string cesta, IReadOnlyList<StlpecPrenosu> stlpce, IReadOnlyList<object?[]> riadky)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(';', stlpce.Select(s => PoleCsv(s.Nazov)))).Append("\r\n");
        foreach (var riadok in riadky)
        {
            sb.Append(string.Join(';', riadok.Select(h => PoleCsv(Prevod.TextCsv(h))))).Append("\r\n");
        }

        File.WriteAllText(cesta, sb.ToString(), new UTF8Encoding(true));
    }

    private static string PoleCsv(string hodnota)
    {
        if (hodnota.Length == 0)
        {
            return "";
        }

        var treba = hodnota.IndexOfAny(new[] { ';', '"', '\r', '\n' }) >= 0 || hodnota[0] == ' ' || hodnota[^1] == ' ';
        return treba ? "\"" + hodnota.Replace("\"", "\"\"") + "\"" : hodnota;
    }

    // ------------------------------------------------------------------ JSON a XML

    internal static BalikEviTAPolozky VytvorBalik(EvitaDbContext db, List<Polozka> polozky, bool sHistoriou, bool celaEvidencia)
    {
        var balik = new BalikEviTAPolozky
        {
            Program = $"EviTA {AktualizaciaService.AktualnaVerzia}",
            Vytvorene = Prevod.TextIso(DateTime.Now, true),
            Pocitac = Environment.MachineName,
            PoslednePrirastkoveCislo = celaEvidencia ? Databaza.DalsiePrirastkoveCislo(db) - 1 : null,
            Kategorie = db.Kategorie.AsNoTracking().OrderBy(k => k.Id).ToList()
                .Select(k => new KategoriaPrenos { Nazov = k.Nazov, Aktivna = k.Aktivna }).ToList(),
            Miestnosti = db.Miestnosti.AsNoTracking().OrderBy(m => m.Id).ToList()
                .Select(m => new MiestnostPrenos { Nazov = m.Nazov, Budova = m.Budova, Poznamka = m.Poznamka, Aktivna = m.Aktivna }).ToList(),
            Osoby = db.Osoby.AsNoTracking().OrderBy(o => o.Id).ToList()
                .Select(o => new OsobaPrenos { Meno = o.Meno, Priezvisko = o.Priezvisko, Funkcia = o.Funkcia, Aktivna = o.Aktivna }).ToList(),
            Polozky = polozky.Select(PolozkaPrenos.Z).ToList(),
        };

        // pracovný kalendár ide len s celou evidenciou (prenos na iný počítač)
        if (celaEvidencia)
        {
            var typy = db.TypyUdalosti.AsNoTracking().OrderBy(t => t.Poradie).ThenBy(t => t.Id).ToList();
            var nazvyTypov = typy.ToDictionary(t => t.Id, t => t.Nazov);
            balik.TypyUdalosti = typy.Select(t => new TypUdalostiPrenos { Nazov = t.Nazov, Farba = t.Farba, Aktivny = t.Aktivny }).ToList();
            balik.Udalosti = db.Udalosti.AsNoTracking()
                .Include(u => u.Polozka).Include(u => u.Miestnost).Include(u => u.Osoba)
                .OrderBy(u => u.Zaciatok).ToList()
                .Select(u => new UdalostPrenos
                {
                    Guid = u.Guid.ToString(),
                    Nazov = u.Nazov,
                    Popis = u.Popis,
                    Typ = nazvyTypov.TryGetValue(u.TypUdalostiId, out var typ) ? typ : "",
                    Druh = u.Druh.ToString(),
                    Zaciatok = Prevod.TextIso(u.Zaciatok, true)!,
                    Koniec = Prevod.TextIso(u.Koniec, true),
                    CelyDen = u.CelyDen,
                    Opakovanie = u.Opakovanie.ToString(),
                    OpakovanieDo = Prevod.TextIso(u.OpakovanieDo, false),
                    Pripomienka = u.Pripomienka.ToString(),
                    PolozkaGuid = u.Polozka?.Guid.ToString(),
                    PolozkaCislo = u.Polozka?.PrirastkoveCislo,
                    Miestnost = u.Miestnost?.Nazov,
                    Osoba = u.Osoba?.CeleMeno,
                    Stav = u.Stav.ToString(),
                })
                .ToList();
            balik.Ulohy = db.Ulohy.AsNoTracking().Include(u => u.Polozka)
                .OrderBy(u => u.Hotovo).ThenBy(u => u.Poradie).ThenBy(u => u.Id).ToList()
                .Select(u => new UlohaPrenos
                {
                    Guid = u.Guid.ToString(),
                    Text = u.Text,
                    Popis = u.Popis,
                    Termin = Prevod.TextIso(u.Termin, false),
                    Priorita = u.Priorita.ToString(),
                    Hotovo = u.Hotovo,
                    DatumHotovo = Prevod.TextIso(u.DatumHotovo, true),
                    Vytvorene = Prevod.TextIso(u.Vytvorene, true),
                    PolozkaGuid = u.Polozka?.Guid.ToString(),
                    PolozkaCislo = u.Polozka?.PrirastkoveCislo,
                })
                .ToList();
            balik.Poznamky = db.Poznamky.AsNoTracking().Include(p => p.Polozka).Include(p => p.Udalost)
                .OrderBy(p => p.Vytvorene).ToList()
                .Select(p => new PoznamkaPrenos
                {
                    Guid = p.Guid.ToString(),
                    Nazov = p.Nazov,
                    Text = p.Text,
                    Farba = p.Farba,
                    Pripnuta = p.Pripnuta,
                    Vytvorene = Prevod.TextIso(p.Vytvorene, true),
                    Zmenene = Prevod.TextIso(p.Zmenene, true),
                    Datum = Prevod.TextIso(p.Datum, false),
                    PolozkaGuid = p.Polozka?.Guid.ToString(),
                    PolozkaCislo = p.Polozka?.PrirastkoveCislo,
                    UdalostGuid = p.Udalost?.Guid.ToString(),
                })
                .ToList();
        }

        if (sHistoriou)
        {
            var guidy = db.Polozky.AsNoTracking().Select(p => new { p.Id, p.Guid }).ToDictionary(x => x.Id, x => x.Guid);
            var dotaz = db.Historia.AsNoTracking().AsQueryable();
            if (!celaEvidencia)
            {
                var id = polozky.Select(p => p.Id).ToList();
                dotaz = dotaz.Where(h => id.Contains(h.PolozkaId));
            }

            balik.Historia = dotaz.OrderBy(h => h.Cas).ThenBy(h => h.Id).ToList()
                .Select(h => new ZmenaPrenos
                {
                    GuidPolozky = guidy.TryGetValue(h.PolozkaId, out var g) ? g.ToString() : null,
                    PrirastkoveCislo = h.PrirastkoveCislo,
                    NazovPolozky = h.NazovPolozky,
                    Cas = Prevod.TextIso(h.Cas, true)!,
                    Pouzivatel = h.Pouzivatel,
                    Akcia = h.Akcia,
                    Pole = h.Pole,
                    StaraHodnota = h.StaraHodnota,
                    NovaHodnota = h.NovaHodnota,
                })
                .ToList();
        }

        return balik;
    }

    internal static readonly JsonSerializerOptions MoznostiJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static void ZapisJson(string cesta, BalikEviTAPolozky balik) =>
        File.WriteAllText(cesta, JsonSerializer.Serialize(balik, MoznostiJson), new UTF8Encoding(false));

    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>Zapíše XML a vedľa neho popis formátu evita.xsd.</summary>
    internal static void ZapisXml(string cesta, BalikEviTAPolozky balik)
    {
        var nastavenia = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.Entitize };
        using (var w = XmlWriter.Create(cesta, nastavenia))
        {
            w.WriteStartDocument();
            w.WriteStartElement("EviTA");
            w.WriteAttributeString("xsi", "noNamespaceSchemaLocation", XsiNamespace, XsdEviTA.NazovSuboru);
            w.WriteAttributeString("verziaFormatu", balik.VerziaFormatu.ToString(CultureInfo.InvariantCulture));
            Atribut(w, "program", balik.Program);
            Atribut(w, "vytvorene", balik.Vytvorene);
            Atribut(w, "pocitac", balik.Pocitac);
            Atribut(w, "poslednePrirastkoveCislo", balik.PoslednePrirastkoveCislo?.ToString(CultureInfo.InvariantCulture));

            w.WriteStartElement("Kategorie");
            foreach (var k in balik.Kategorie)
            {
                w.WriteStartElement("Kategoria");
                w.WriteAttributeString("nazov", k.Nazov);
                w.WriteAttributeString("aktivna", k.Aktivna ? "true" : "false");
                w.WriteEndElement();
            }

            w.WriteEndElement();

            w.WriteStartElement("Miestnosti");
            foreach (var m in balik.Miestnosti)
            {
                w.WriteStartElement("Miestnost");
                w.WriteAttributeString("nazov", m.Nazov);
                Atribut(w, "budova", m.Budova);
                Atribut(w, "poznamka", m.Poznamka);
                w.WriteAttributeString("aktivna", m.Aktivna ? "true" : "false");
                w.WriteEndElement();
            }

            w.WriteEndElement();

            w.WriteStartElement("Osoby");
            foreach (var o in balik.Osoby)
            {
                w.WriteStartElement("Osoba");
                Atribut(w, "meno", o.Meno);
                w.WriteAttributeString("priezvisko", o.Priezvisko);
                Atribut(w, "funkcia", o.Funkcia);
                w.WriteAttributeString("aktivna", o.Aktivna ? "true" : "false");
                w.WriteEndElement();
            }

            w.WriteEndElement();

            w.WriteStartElement("Polozky");
            foreach (var p in balik.Polozky)
            {
                w.WriteStartElement("Polozka");
                Atribut(w, "guid", p.Guid);
                Atribut(w, "prirastkoveCislo", p.PrirastkoveCislo?.ToString(CultureInfo.InvariantCulture));
                w.WriteElementString("Nazov", p.Nazov);
                Prvok(w, "Kategoria", p.Kategoria);
                Prvok(w, "Miestnost", p.Miestnost);
                Prvok(w, "Pridelene", p.Pridelene);
                Prvok(w, "Vyrobca", p.Vyrobca);
                Prvok(w, "Model", p.Model);
                Prvok(w, "SerioveCislo", p.SerioveCislo);
                Prvok(w, "InventarneCislo", p.InventarneCislo);
                Prvok(w, "Ean", p.Ean);
                Prvok(w, "Stav", p.Stav);
                Prvok(w, "DatumNakupu", p.DatumNakupu);
                Prvok(w, "Cena", p.Cena?.ToString(CultureInfo.InvariantCulture));
                Prvok(w, "ZarukaDo", p.ZarukaDo);
                Prvok(w, "DalsiaRevizia", p.DalsiaRevizia);
                Prvok(w, "DatumPridania", p.DatumPridania);
                Prvok(w, "DatumZmeny", p.DatumZmeny);
                Prvok(w, "DatumVyradenia", p.DatumVyradenia);
                Prvok(w, "DovodVyradenia", p.DovodVyradenia);
                Prvok(w, "Poznamka", p.Poznamka);
                w.WriteEndElement();
            }

            w.WriteEndElement();

            if (balik.Historia is not null)
            {
                w.WriteStartElement("Historia");
                foreach (var h in balik.Historia)
                {
                    w.WriteStartElement("Zmena");
                    Atribut(w, "guidPolozky", h.GuidPolozky);
                    w.WriteAttributeString("prirastkoveCislo", h.PrirastkoveCislo.ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("nazovPolozky", h.NazovPolozky);
                    w.WriteAttributeString("cas", h.Cas);
                    w.WriteAttributeString("pouzivatel", h.Pouzivatel);
                    w.WriteAttributeString("akcia", h.Akcia);
                    Atribut(w, "pole", h.Pole);
                    Atribut(w, "staraHodnota", h.StaraHodnota);
                    Atribut(w, "novaHodnota", h.NovaHodnota);
                    w.WriteEndElement();
                }

                w.WriteEndElement();
            }

            if (balik.TypyUdalosti is not null)
            {
                w.WriteStartElement("TypyUdalosti");
                foreach (var t in balik.TypyUdalosti)
                {
                    w.WriteStartElement("TypUdalosti");
                    w.WriteAttributeString("nazov", t.Nazov);
                    w.WriteAttributeString("farba", t.Farba);
                    w.WriteAttributeString("aktivny", t.Aktivny ? "true" : "false");
                    w.WriteEndElement();
                }

                w.WriteEndElement();
            }

            if (balik.Udalosti is not null)
            {
                w.WriteStartElement("Udalosti");
                foreach (var u in balik.Udalosti)
                {
                    w.WriteStartElement("Udalost");
                    Atribut(w, "guid", u.Guid);
                    w.WriteAttributeString("nazov", u.Nazov);
                    Atribut(w, "typ", u.Typ);
                    Atribut(w, "druh", u.Druh);
                    w.WriteAttributeString("zaciatok", u.Zaciatok);
                    Atribut(w, "koniec", u.Koniec);
                    w.WriteAttributeString("celyDen", u.CelyDen ? "true" : "false");
                    Atribut(w, "opakovanie", u.Opakovanie);
                    Atribut(w, "opakovanieDo", u.OpakovanieDo);
                    Atribut(w, "pripomienka", u.Pripomienka);
                    Atribut(w, "polozkaGuid", u.PolozkaGuid);
                    Atribut(w, "polozkaCislo", u.PolozkaCislo?.ToString(CultureInfo.InvariantCulture));
                    Atribut(w, "miestnost", u.Miestnost);
                    Atribut(w, "osoba", u.Osoba);
                    Atribut(w, "stav", u.Stav);
                    Prvok(w, "Popis", u.Popis);
                    w.WriteEndElement();
                }

                w.WriteEndElement();
            }

            if (balik.Ulohy is not null)
            {
                w.WriteStartElement("Ulohy");
                foreach (var u in balik.Ulohy)
                {
                    w.WriteStartElement("Uloha");
                    Atribut(w, "guid", u.Guid);
                    w.WriteAttributeString("text", u.Text);
                    Atribut(w, "termin", u.Termin);
                    Atribut(w, "priorita", u.Priorita);
                    w.WriteAttributeString("hotovo", u.Hotovo ? "true" : "false");
                    Atribut(w, "datumHotovo", u.DatumHotovo);
                    Atribut(w, "vytvorene", u.Vytvorene);
                    Atribut(w, "polozkaGuid", u.PolozkaGuid);
                    Atribut(w, "polozkaCislo", u.PolozkaCislo?.ToString(CultureInfo.InvariantCulture));
                    Prvok(w, "Popis", u.Popis);
                    w.WriteEndElement();
                }

                w.WriteEndElement();
            }

            if (balik.Poznamky is not null)
            {
                w.WriteStartElement("Poznamky");
                foreach (var p in balik.Poznamky)
                {
                    w.WriteStartElement("Poznamka");
                    Atribut(w, "guid", p.Guid);
                    w.WriteAttributeString("nazov", p.Nazov);
                    Atribut(w, "farba", p.Farba);
                    w.WriteAttributeString("pripnuta", p.Pripnuta ? "true" : "false");
                    Atribut(w, "vytvorene", p.Vytvorene);
                    Atribut(w, "zmenene", p.Zmenene);
                    Atribut(w, "datum", p.Datum);
                    Atribut(w, "polozkaGuid", p.PolozkaGuid);
                    Atribut(w, "polozkaCislo", p.PolozkaCislo?.ToString(CultureInfo.InvariantCulture));
                    Atribut(w, "udalostGuid", p.UdalostGuid);
                    Prvok(w, "Text", p.Text);
                    w.WriteEndElement();
                }

                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndDocument();
        }

        var priecinok = Path.GetDirectoryName(Path.GetFullPath(cesta)) ?? ".";
        File.WriteAllText(Path.Combine(priecinok, XsdEviTA.NazovSuboru), XsdEviTA.Text, new UTF8Encoding(false));
    }

    private static void Atribut(XmlWriter w, string nazov, string? hodnota)
    {
        if (!string.IsNullOrEmpty(hodnota))
        {
            w.WriteAttributeString(nazov, hodnota);
        }
    }

    private static void Prvok(XmlWriter w, string nazov, string? hodnota)
    {
        if (!string.IsNullOrEmpty(hodnota))
        {
            w.WriteElementString(nazov, hodnota);
        }
    }

    // ------------------------------------------------------------------ HTML

    internal static void ZapisHtml(string cesta, string titulok, string popis, IReadOnlyList<StlpecPrenosu> stlpce, IReadOnlyList<Polozka> polozky)
    {
        static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

        var sb = new StringBuilder();
        sb.Append($"<!DOCTYPE html>\n<html lang=\"{Preklad.Kod}\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(E(titulok)).Append("</title>\n");
        sb.Append("""
            <style>
            body { font-family: "Segoe UI", Arial, sans-serif; margin: 24px; color: #1b1b1b; background: #fff; }
            h1 { font-size: 20px; margin: 0 0 4px; }
            p.popis { color: #555; margin: 0 0 16px; font-size: 13px; }
            table { border-collapse: collapse; width: 100%; font-size: 12px; }
            th { background: #dce6f1; text-align: left; padding: 6px 8px; border-bottom: 2px solid #9bb3d1; position: sticky; top: 0; }
            td { padding: 5px 8px; border-bottom: 1px solid #e3e3e3; vertical-align: top; }
            tr:nth-child(even) td { background: #f7f9fc; }
            td.cislo { text-align: right; white-space: nowrap; }
            td.datum { white-space: nowrap; }
            tr.vyradene td { color: #8a8a8a; }
            footer { margin-top: 12px; color: #777; font-size: 11px; }
            @media print { body { margin: 0; } th { position: static; } tr { page-break-inside: avoid; } }
            </style>

            """);
        sb.Append("</head>\n<body>\n");
        sb.Append("<h1>").Append(E(titulok)).Append("</h1>\n");
        sb.Append("<p class=\"popis\">").Append(E(popis)).Append("</p>\n");
        sb.Append("<table>\n<thead><tr>");
        foreach (var s in stlpce)
        {
            sb.Append("<th>").Append(E(s.Nazov)).Append("</th>");
        }

        sb.Append("</tr></thead>\n<tbody>\n");
        foreach (var p in polozky)
        {
            sb.Append(p.Stav == StavPolozky.Vyradene ? "<tr class=\"vyradene\">" : "<tr>");
            foreach (var s in stlpce)
            {
                var trieda = s.Typ switch
                {
                    TypStlpca.CeleCislo or TypStlpca.Suma => " class=\"cislo\"",
                    TypStlpca.Datum or TypStlpca.DatumCas => " class=\"datum\"",
                    _ => "",
                };
                var text = E(Prevod.TextZobrazenia(StlpcePrenosu.Hodnota(p, s.Kluc), s.Typ)).Replace("\n", "<br>");
                sb.Append("<td").Append(trieda).Append('>').Append(text).Append("</td>");
            }

            sb.Append("</tr>\n");
        }

        sb.Append("</tbody>\n</table>\n");
        sb.Append("<footer>")
            .Append(E(Preklad.F($"Vytvorené programom EviTA {AktualizaciaService.AktualnaVerzia.ToString(3)}, {Sk.DatumCas(DateTime.Now)}.")))
            .Append("</footer>\n");
        sb.Append("</body>\n</html>\n");
        File.WriteAllText(cesta, sb.ToString(), new UTF8Encoding(false));
    }
}
