using System.Globalization;
using Evita.Models;
using Kalendarium;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

namespace Evita.Services;

/// <summary>Tlačová zostava kalendára: mesačná mriežka (mesiac na strane) alebo zoznam udalostí a úloh.</summary>
internal static class ZostavaKalendara
{
    private static string[] NazvyDni => new[] { Preklad.T("Po"), Preklad.T("Ut"), Preklad.T("St"), Preklad.T("Št"), Preklad.T("Pi"), Preklad.T("So"), Preklad.T("Ne") };

    public static Document Vytvor(MoznostiZostavy m)
    {
        var dnes = DateTime.Today;
        var od = (m.Od ?? new DateTime(dnes.Year, dnes.Month, 1)).Date;
        var po = (m.Do ?? od.AddMonths(1).AddDays(-1)).Date;
        if (po < od)
        {
            throw new EvitaException(Preklad.T("Dátum „do“ je skôr ako dátum „od“."));
        }

        if ((po - od).TotalDays > 750)
        {
            throw new EvitaException(Preklad.T("Kalendár sa dá vytlačiť najviac za dva roky naraz."));
        }

        var sluzba = new KalendarService();
        var vyskyty = sluzba.Vyskyty(od.AddDays(-7), po.AddDays(7), automaticke: m.AutomatickeTerminy);
        var obdobie = $"{Sk.Datum(od)} – {Sk.Datum(po)}";
        var vObdobi = vyskyty.Where(v => v.Zaciatok.Date <= po && v.PoslednyDen >= od).ToList();
        var podtitulok = Preklad.F($"obdobie {obdobie} · {vObdobi.Count} udalostí a termínov") +
                         (m.AutomatickeTerminy ? Preklad.T(" (vrátane automatických termínov)") : "") + Preklad.F($" · vytlačené {Sk.DatumCas(DateTime.Now)}");

        return m.KalendarZoznam
            ? Zoznam(vObdobi, podtitulok, m.NaSirku)
            : Mriezka(vyskyty, od, po, podtitulok, m.NaSirku);
    }

    private static Document Zoznam(List<VyskytUdalosti> vyskyty, string podtitulok, bool naSirku)
    {
        var zostava = new PdfZostava(Preklad.T("Kalendár – zoznam udalostí a úloh"), podtitulok, naSirku);
        if (vyskyty.Count == 0)
        {
            zostava.Text(Preklad.T("V zvolenom období nie sú žiadne udalosti."), sedy: true);
            return zostava.Dokument;
        }

        var hlavicky = new[] { Preklad.T("Dátum"), Preklad.T("Čas"), Preklad.T("Udalosť"), Preklad.T("Typ"), Preklad.T("Druh"), Preklad.T("Naviazanie"), Preklad.T("Stav") };
        var riadky = vyskyty.Select(v => new[]
        {
            $"{v.Zaciatok.ToString("ddd", Sk.Kultura)} {Sk.Datum(v.Zaciatok)}",
            v.ViacDni ? Preklad.F($"do {Sk.KratkyDatum(v.PoslednyDen)}") : v.CelyDen ? Preklad.T("celý deň") : v.CasText,
            v.Nazov,
            v.Typ,
            v.DruhText,
            v.Naviazanie ?? "",
            v.Automaticky ? Preklad.T("automaticky") : v.StavText,
        }).ToList();
        zostava.Tabulka(hlavicky, new bool[hlavicky.Length], riadky, zostava.Sirky(hlavicky, riadky),
            vyskyty.Select(v => v.Stav != StavUdalosti.Naplanovane).ToList());
        return zostava.Dokument;
    }

    private static Document Mriezka(List<VyskytUdalosti> vyskyty, DateTime od, DateTime po, string podtitulok, bool naSirku)
    {
        var zostava = new PdfZostava(Preklad.T("Kalendár"), podtitulok, naSirku);

        // sviatky a meniny z Kalendária (ak nie je dostupné, kalendár sa vytlačí bez nich)
        var sviatky = new Dictionary<DateOnly, List<SviatokDna>>();
        try
        {
            for (var rok = od.Year; rok <= po.Year; rok++)
            {
                foreach (var s in KalendariumDb.Spolocna.Sviatky(rok))
                {
                    if (!sviatky.TryGetValue(s.Datum, out var zoznam))
                    {
                        sviatky[s.Datum] = zoznam = new List<SviatokDna>();
                    }

                    zoznam.Add(s);
                }
            }
        }
        catch (Exception)
        {
            // bez sviatkov
        }

        string Meniny(DateTime d)
        {
            try
            {
                return string.Join(", ", KalendariumDb.Spolocna.Meniny(DateOnly.FromDateTime(d)));
            }
            catch (Exception)
            {
                return "";
            }
        }

        var sirkaTyzdna = 0.8;
        var sirkaDna = (zostava.SirkaTabulky - sirkaTyzdna) / 7;
        var prvaStrana = true;
        for (var mesiac = new DateTime(od.Year, od.Month, 1); mesiac <= po; mesiac = mesiac.AddMonths(1))
        {
            if (!prvaStrana)
            {
                zostava.NovaStrana();
            }

            prvaStrana = false;
            var koniecMesiaca = mesiac.AddMonths(1).AddDays(-1);
            var pocet = vyskyty.Count(v => v.Zaciatok.Date <= koniecMesiaca && v.PoslednyDen >= mesiac);
            zostava.NadpisSkupiny(Sk.MesiacRok(mesiac), Preklad.F($"{pocet} udalostí a termínov"));

            var tabulka = zostava.Sekcia.AddTable();
            tabulka.Borders.Width = 0.4;
            tabulka.Borders.Color = new Color(190, 190, 190);
            tabulka.TopPadding = Unit.FromPoint(1.5);
            tabulka.BottomPadding = Unit.FromPoint(1);
            tabulka.AddColumn(Unit.FromCentimeter(sirkaTyzdna));
            for (var i = 0; i < 7; i++)
            {
                tabulka.AddColumn(Unit.FromCentimeter(sirkaDna));
            }

            var hlavicka = tabulka.AddRow();
            hlavicka.HeadingFormat = true;
            hlavicka.Format.Font.Bold = true;
            hlavicka.Shading.Color = new Color(220, 230, 241);
            hlavicka.Cells[0].AddParagraph(Preklad.T("týž.")).Format.Font.Size = 6.5;
            for (var i = 0; i < 7; i++)
            {
                hlavicka.Cells[i + 1].AddParagraph(NazvyDni[i]);
            }

            var zaciatok = mesiac.AddDays(-(((int)mesiac.DayOfWeek + 6) % 7));
            var tyzdne = (int)Math.Ceiling((koniecMesiaca - zaciatok).TotalDays / 7.0 + 0.01);
            var vyskaRiadku = (naSirku ? 13.6 : 22.5) / Math.Max(4, tyzdne);
            var najviac = Math.Max(2, (int)(vyskaRiadku / 0.36) - 2);

            for (var t = 0; t < tyzdne; t++)
            {
                var riadok = tabulka.AddRow();
                riadok.Height = Unit.FromCentimeter(vyskaRiadku);
                riadok.HeightRule = RowHeightRule.AtLeast;
                riadok.VerticalAlignment = VerticalAlignment.Top;
                var pondelok = zaciatok.AddDays(t * 7);
                var cisloTyzdna = riadok.Cells[0].AddParagraph(KalendarService.CisloTyzdna(pondelok).ToString(CultureInfo.InvariantCulture));
                cisloTyzdna.Format.Font.Size = 6.5;
                cisloTyzdna.Format.Font.Color = Colors.Gray;

                for (var d = 0; d < 7; d++)
                {
                    var den = pondelok.AddDays(d);
                    var bunka = riadok.Cells[d + 1];
                    var mimo = den.Month != mesiac.Month;
                    var sviatkyDna = sviatky.TryGetValue(DateOnly.FromDateTime(den), out var s) ? s : new List<SviatokDna>();
                    var volno = sviatkyDna.Any(x => x.DenPracovnehoPokoja);
                    if (volno)
                    {
                        bunka.Shading.Color = new Color(232, 245, 233);
                    }
                    else if (d >= 5)
                    {
                        bunka.Shading.Color = new Color(245, 245, 245);
                    }

                    var hlavickaDna = bunka.AddParagraph();
                    var cislo = hlavickaDna.AddFormattedText(den.Day.ToString(CultureInfo.InvariantCulture));
                    cislo.Bold = true;
                    cislo.Size = 9;
                    if (mimo)
                    {
                        cislo.Color = Colors.Gray;
                    }
                    else if (volno)
                    {
                        cislo.Color = new Color(46, 125, 50);
                    }

                    if (den == DateTime.Today)
                    {
                        cislo.Underline = Underline.Single;
                    }

                    var meniny = Meniny(den);
                    if (meniny.Length > 0 && !mimo)
                    {
                        var m = hlavickaDna.AddFormattedText("  " + meniny);
                        m.Size = 6;
                        m.Color = Colors.Gray;
                    }

                    if (sviatkyDna.Count > 0 && !mimo)
                    {
                        var odsek = bunka.AddParagraph(string.Join(", ", sviatkyDna.Select(NazvySviatkov.Nazov)));
                        odsek.Format.Font.Size = 6;
                        odsek.Format.Font.Italic = true;
                        odsek.Format.Font.Color = new Color(46, 125, 50);
                    }

                    if (mimo)
                    {
                        continue;
                    }

                    var dna = vyskyty.Where(v => v.Zaciatok.Date <= den && v.PoslednyDen >= den).ToList();
                    foreach (var v in dna.Take(najviac))
                    {
                        var odsek = bunka.AddParagraph();
                        odsek.Format.Font.Size = 6.5;
                        odsek.Format.SpaceBefore = Unit.FromPoint(0.5);
                        var znacka = odsek.AddFormattedText("■ ");
                        znacka.Color = Farba(v.Farba);
                        var text = odsek.AddFormattedText(v.KratkyText);
                        if (v.Stav != StavUdalosti.Naplanovane)
                        {
                            text.Color = Colors.Gray;
                        }
                    }

                    if (dna.Count > najviac)
                    {
                        var dalsie = bunka.AddParagraph(Preklad.F($"+ {dna.Count - najviac} ďalšie"));
                        dalsie.Format.Font.Size = 6;
                        dalsie.Format.Font.Color = Colors.Gray;
                    }
                }
            }
        }

        return zostava.Dokument;
    }

    /// <summary>Farba #RRGGBB pre MigraDoc.</summary>
    private static Color Farba(string hex)
    {
        try
        {
            var h = hex.TrimStart('#');
            if (h.Length == 6)
            {
                return new Color(
                    byte.Parse(h[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(h[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(h[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }
        catch (FormatException)
        {
            // náhradná farba nižšie
        }

        return Colors.Gray;
    }
}
