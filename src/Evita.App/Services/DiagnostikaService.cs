using System.IO;
using Evita.Data;
using Evita.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

/// <summary>Výsledok jedného nástroja: či je všetko v poriadku a riadky protokolu.</summary>
public sealed record VysledokKontroly(string Nastroj, bool Ok, List<string> Riadky);

/// <summary>Nástroje na kontrolu, údržbu a opravu databázy.</summary>
public sealed class DiagnostikaService
{
    private static string Upozornenie => Preklad.T("UPOZORNENIE: ");

    /// <summary>Kľúč v nastaveniach: čas, nástroj a výsledok poslednej kontroly integrity.</summary>
    public const string KlucPoslednaKontrola = "Kontrola.Posledna";

    /// <summary>Tabuľky v poradí, v akom sa kopírujú pri záchrane (najprv tie, na ktoré sa odkazuje).</summary>
    private static readonly string[] Tabulky =
    {
        "Nastavenia", "Kategorie", "Miestnosti", "Osoby", "Polozky", "HistoriaZmien", "ZaznamyZaloh", "TypyUdalosti", "Udalosti", "Ulohy", "Poznamky",
    };

    public VysledokKontroly RychlaKontrola() => Kontrola("PRAGMA quick_check;", Preklad.T("Rýchla kontrola"));

    public VysledokKontroly UplnaKontrola() => Kontrola("PRAGMA integrity_check;", Preklad.T("Úplná kontrola integrity"));

    public VysledokKontroly KontrolaVazieb()
    {
        var riadky = new List<string>();
        using var spojenie = Databaza.OtvorSpojenie();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "PRAGMA foreign_key_check;";
        using var citac = prikaz.ExecuteReader();
        while (citac.Read())
        {
            var tabulka = citac.GetString(0);
            var riadok = citac.IsDBNull(1) ? "?" : citac.GetInt64(1).ToString();
            var rodic = citac.GetString(2);
            riadky.Add(Preklad.F($"{Upozornenie}tabuľka {tabulka}, riadok {riadok} odkazuje na neexistujúci záznam v tabuľke {rodic}."));
        }

        var ok = riadky.Count == 0;
        if (ok)
        {
            riadky.Add(Preklad.T("Všetky odkazy na kategórie, miestnosti a osoby sú v poriadku."));
        }

        return new VysledokKontroly(Preklad.T("Kontrola väzieb"), ok, riadky);
    }

    /// <summary>Logická kontrola údajov: duplicity, chýbajúce čísla, nezmyselné dátumy…</summary>
    public VysledokKontroly KontrolaDat()
    {
        var riadky = new List<string>();
        var problem = false;
        using var db = Databaza.Otvor();
        var polozky = db.Polozky.AsNoTracking().ToList();
        var teraz = DateTime.Now;

        // duplicitné sériové čísla
        var duplicity = polozky
            .Where(p => !string.IsNullOrWhiteSpace(p.SerioveCislo))
            .GroupBy(p => p.SerioveCislo!.Trim().ToUpperInvariant())
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var skupina in duplicity)
        {
            problem = true;
            riadky.Add(Preklad.F($"{Upozornenie}sériové číslo {skupina.Key} má viac položiek: {string.Join(", ", skupina.Select(p => p.PrirastkoveCislo))}."));
        }

        // chýbajúce prírastkové čísla
        if (polozky.Count > 0)
        {
            var existujuce = polozky.Select(p => p.PrirastkoveCislo).ToHashSet();
            var vymazane = db.Historia.AsNoTracking()
                .Where(h => h.Akcia == PolozkaService.AkciaVymazanie)
                .Select(h => h.PrirastkoveCislo).ToHashSet();
            var chybajuce = Enumerable.Range(1, existujuce.Max()).Where(c => !existujuce.Contains(c)).ToList();
            var nevysvetlene = chybajuce.Where(c => !vymazane.Contains(c)).ToList();
            riadky.Add(Preklad.F($"Prírastkové čísla: 1 až {existujuce.Max()}, chýba {chybajuce.Count} (vymazané v programe: {chybajuce.Count - nevysvetlene.Count})."));
            if (nevysvetlene.Count > 0)
            {
                problem = true;
                riadky.Add(Preklad.F($"{Upozornenie}čísla chýbajú bez záznamu o vymazaní: {Rozsahy(nevysvetlene)}."));
            }
        }

        var aktivne = polozky.Where(p => p.Stav != StavPolozky.Vyradene).ToList();
        Pridaj(riadky, aktivne.Where(p => p.MiestnostId is null), Preklad.T("aktívne položky bez miestnosti"), ref problem, jeProblem: false);
        Pridaj(riadky, polozky.Where(p => p.DatumPridania > teraz.AddMinutes(5)), Preklad.T("dátum pridania je v budúcnosti"), ref problem);
        Pridaj(riadky, polozky.Where(p => p.DatumNakupu > teraz.Date), Preklad.T("dátum nákupu je v budúcnosti"), ref problem);
        Pridaj(riadky, polozky.Where(p => p.ZarukaDo is not null && p.DatumNakupu is not null && p.ZarukaDo < p.DatumNakupu),
            Preklad.T("záruka končí skôr, ako bol nákup"), ref problem);
        Pridaj(riadky, polozky.Where(p => p.Stav == StavPolozky.Vyradene && p.DatumVyradenia is null),
            Preklad.T("vyradené bez dátumu vyradenia"), ref problem);
        Pridaj(riadky, polozky.Where(p => string.IsNullOrWhiteSpace(p.Nazov)), Preklad.T("položka bez názvu"), ref problem);

        // nepoužívané položky číselníkov (len informácia)
        var pouziteKategorie = polozky.Select(p => p.KategoriaId).ToHashSet();
        var pouziteMiestnosti = polozky.Where(p => p.MiestnostId is not null).Select(p => p.MiestnostId!.Value).ToHashSet();
        var pouziteOsoby = polozky.Where(p => p.OsobaId is not null).Select(p => p.OsobaId!.Value).ToHashSet();
        riadky.Add(Preklad.F($"Nepoužívané kategórie: {db.Kategorie.AsNoTracking().AsEnumerable().Count(k => !pouziteKategorie.Contains(k.Id))}, miestnosti: {db.Miestnosti.AsNoTracking().AsEnumerable().Count(m => !pouziteMiestnosti.Contains(m.Id))}, osoby: {db.Osoby.AsNoTracking().AsEnumerable().Count(o => !pouziteOsoby.Contains(o.Id))} (len informácia)."));

        if (!problem)
        {
            riadky.Add(Preklad.T("Údaje sú v poriadku."));
        }

        return new VysledokKontroly(Preklad.T("Kontrola dát"), !problem, riadky);
    }

    public VysledokKontroly Optimalizuj()
    {
        Vykonaj("PRAGMA optimize; ANALYZE;");
        return new VysledokKontroly(Preklad.T("Optimalizácia"), true, new List<string> { Preklad.T("Štatistiky pre rýchle vyhľadávanie sú aktualizované.") });
    }

    public VysledokKontroly ObnovIndexy()
    {
        Vykonaj("REINDEX;");
        return new VysledokKontroly(Preklad.T("Obnova indexov"), true, new List<string> { Preklad.T("Všetky indexy sú znovu vytvorené.") });
    }

    public VysledokKontroly Kompaktuj()
    {
        var pred = ServisService.VelkostDatabazy();
        Vykonaj("PRAGMA wal_checkpoint(TRUNCATE);");
        Vykonaj("VACUUM;");
        Vykonaj("PRAGMA wal_checkpoint(TRUNCATE);");
        var po = ServisService.VelkostDatabazy();
        return new VysledokKontroly(Preklad.T("Kompaktovanie"), true, new List<string>
        {
            Preklad.F($"Veľkosť pred: {Velkost(pred)}, po: {Velkost(po)}."),
        });
    }

    /// <summary>
    /// Záchrana dát z poškodenej databázy: prenesie všetky čitateľné záznamy do novej databázy a tou
    /// nahradí pôvodnú. Pôvodný súbor sa predtým skopíruje do priečinka záloh.
    /// </summary>
    public VysledokKontroly Zachran()
    {
        var riadky = new List<string>();
        var cas = DateTime.Now;
        var priecinokZaloh = new ZalohaService().NacitajNastavenia().Priecinok;
        Directory.CreateDirectory(priecinokZaloh);

        // 1. surová kópia pôvodného súboru (môže byť nečitateľný, preto len kopírujeme súbor)
        SqliteConnection.ClearAllPools();
        var kopia = ZalohaService.CislovanaCesta(priecinokZaloh, $"EviTA_pred_zachranou_{cas:yyyy-MM-dd_HHmmss}", ".db");
        File.Copy(Databaza.Cesta, kopia, overwrite: true);
        if (File.Exists(Databaza.Cesta + "-wal"))
        {
            File.Copy(Databaza.Cesta + "-wal", kopia + "-wal", overwrite: true);
        }

        riadky.Add(Preklad.F($"Pôvodný súbor je uložený: {kopia}"));

        // 2. nová prázdna databáza s aktuálnou štruktúrou
        var nova = Path.Combine(Databaza.Priecinok, "evita_zachrana.db");
        foreach (var pripona in new[] { "", "-wal", "-shm" })
        {
            if (File.Exists(nova + pripona))
            {
                File.Delete(nova + pripona);
            }
        }

        using (var db = new EvitaDbContext(nova))
        {
            db.Database.EnsureCreated();
        }

        // 3. prenos tabuliek
        var prenesene = 0;
        var strateneSpolu = 0;
        using (var spojenie = new SqliteConnection($"Data Source={nova};Pooling=False"))
        {
            spojenie.Open();
            Vykonaj(spojenie, "PRAGMA foreign_keys = OFF;");
            using (var pripoj = spojenie.CreateCommand())
            {
                pripoj.CommandText = "ATTACH DATABASE $cesta AS stara;";
                pripoj.Parameters.AddWithValue("$cesta", Databaza.Cesta);
                pripoj.ExecuteNonQuery();
            }

            foreach (var tabulka in Tabulky)
            {
                var (pocet, stratene, sprava) = PrenesTabulku(spojenie, tabulka);
                prenesene += pocet;
                strateneSpolu += stratene;
                riadky.Add(sprava);
            }

            Vykonaj(spojenie, "DETACH DATABASE stara;");
        }

        // 4. nahradenie pôvodnej databázy
        SqliteConnection.ClearAllPools();
        foreach (var pripona in new[] { "", "-wal", "-shm" })
        {
            if (File.Exists(Databaza.Cesta + pripona))
            {
                File.Delete(Databaza.Cesta + pripona);
            }
        }

        File.Move(nova, Databaza.Cesta);
        Databaza.Inicializuj();

        var vazby = KontrolaVazieb();
        riadky.Add(Preklad.F($"Prenesených záznamov: {prenesene}, nečitateľných: {strateneSpolu}."));
        if (!vazby.Ok)
        {
            riadky.Add(Preklad.F($"{Upozornenie}niektoré položky odkazujú na stratené kategórie, miestnosti alebo osoby – pozrite kontrolu väzieb."));
            riadky.AddRange(vazby.Riadky);
        }

        return new VysledokKontroly(Preklad.T("Záchrana dát"), strateneSpolu == 0 && vazby.Ok, riadky);
    }

    // ------------------------------------------------------------------ pomocné

    private static VysledokKontroly Kontrola(string sql, string nastroj)
    {
        var riadky = new List<string>();
        using (var spojenie = Databaza.OtvorSpojenie())
        using (var prikaz = spojenie.CreateCommand())
        {
            prikaz.CommandText = sql;
            using var citac = prikaz.ExecuteReader();
            while (citac.Read())
            {
                riadky.Add(citac.GetString(0));
            }
        }

        var ok = riadky.Count == 1 && riadky[0] == "ok";
        ZapisKontrolu(nastroj, ok);
        return new VysledokKontroly(nastroj, ok, ok
            ? new List<string> { Preklad.T("Databáza je v poriadku.") }
            : riadky.Select(r => Upozornenie + r).Prepend(Preklad.T("Databáza môže byť poškodená. Odporúča sa obnova zo zálohy alebo záchrana dát.")).ToList());
    }

    private static void ZapisKontrolu(string nastroj, bool ok)
    {
        try
        {
            using var db = Databaza.Otvor();
            Databaza.Zapis(db, KlucPoslednaKontrola, $"{DateTime.Now:yyyy-MM-ddTHH:mm:ss}|{nastroj}|{(ok ? "ok" : "chyba")}");
            db.SaveChanges();
        }
        catch (Exception)
        {
            // záznam o kontrole nie je dôležitý
        }
    }

    private static (int Pocet, int Stratene, string Sprava) PrenesTabulku(SqliteConnection spojenie, string tabulka)
    {
        var stlpceNove = Stlpce(spojenie, "main", tabulka);
        List<string> stlpceStare;
        try
        {
            stlpceStare = Stlpce(spojenie, "stara", tabulka);
        }
        catch (SqliteException)
        {
            return (0, 0, Preklad.F($"Tabuľka {tabulka}: nedá sa prečítať, preskočená."));
        }

        if (stlpceStare.Count == 0)
        {
            return (0, 0, Preklad.F($"Tabuľka {tabulka}: v pôvodnej databáze neexistuje."));
        }

        var spolocne = stlpceNove.Where(stlpceStare.Contains).Select(s => $"\"{s}\"").ToList();
        var zoznam = string.Join(", ", spolocne);
        var hromadne = $"INSERT OR IGNORE INTO main.\"{tabulka}\" ({zoznam}) SELECT {zoznam} FROM stara.\"{tabulka}\"";

        try
        {
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = hromadne + ";";
            var pocet = prikaz.ExecuteNonQuery();
            return (pocet, 0, Preklad.F($"Tabuľka {tabulka}: prenesených {pocet} záznamov."));
        }
        catch (SqliteException)
        {
            // tabuľka je čiastočne poškodená – skúsime záznam po zázname
        }

        long najvacsi;
        try
        {
            using var prikaz = spojenie.CreateCommand();
            prikaz.CommandText = $"SELECT MAX(rowid) FROM stara.\"{tabulka}\";";
            najvacsi = prikaz.ExecuteScalar() is long l ? l : 0;
        }
        catch (SqliteException)
        {
            return (0, 0, Preklad.F($"Tabuľka {tabulka}: nedá sa prečítať, preskočená."));
        }

        var prenesene = 0;
        var stratene = 0;
        for (long rowid = 1; rowid <= najvacsi; rowid++)
        {
            try
            {
                using var prikaz = spojenie.CreateCommand();
                prikaz.CommandText = hromadne + " WHERE rowid = $rowid;";
                prikaz.Parameters.AddWithValue("$rowid", rowid);
                prenesene += prikaz.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                stratene++;
            }
        }

        return (prenesene, stratene, Preklad.F($"Tabuľka {tabulka}: prenesených {prenesene} záznamov, nečitateľných {stratene}."));
    }

    private static List<string> Stlpce(SqliteConnection spojenie, string schema, string tabulka)
    {
        var stlpce = new List<string>();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = $"PRAGMA {schema}.table_info(\"{tabulka}\");";
        using var citac = prikaz.ExecuteReader();
        while (citac.Read())
        {
            stlpce.Add(citac.GetString(1));
        }

        return stlpce;
    }

    private static void Vykonaj(string sql)
    {
        using var spojenie = Databaza.OtvorSpojenie();
        Vykonaj(spojenie, sql);
    }

    private static void Vykonaj(SqliteConnection spojenie, string sql)
    {
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = sql;
        prikaz.ExecuteNonQuery();
    }

    private static void Pridaj(List<string> riadky, IEnumerable<Polozka> polozky, string popis, ref bool problem, bool jeProblem = true)
    {
        var cisla = polozky.Select(p => p.PrirastkoveCislo).OrderBy(c => c).ToList();
        if (cisla.Count == 0)
        {
            return;
        }

        if (jeProblem)
        {
            problem = true;
        }

        var ukazka = string.Join(", ", cisla.Take(20)) + (cisla.Count > 20 ? " …" : "");
        riadky.Add($"{(jeProblem ? Upozornenie : "")}{popis}: {cisla.Count} ({ukazka}).");
    }

    /// <summary>[1,2,3,7,9,10] → „1–3, 7, 9–10“</summary>
    private static string Rozsahy(List<int> cisla)
    {
        var casti = new List<string>();
        var i = 0;
        while (i < cisla.Count && casti.Count < 30)
        {
            var od = cisla[i];
            var po = od;
            while (i + 1 < cisla.Count && cisla[i + 1] == po + 1)
            {
                po = cisla[++i];
            }

            casti.Add(od == po ? od.ToString() : $"{od}–{po}");
            i++;
        }

        return string.Join(", ", casti) + (i < cisla.Count ? " …" : "");
    }

    private static string Velkost(long bajty) =>
        bajty < 1024 * 1024 ? $"{bajty / 1024.0:0.0} kB" : $"{bajty / 1024.0 / 1024.0:0.0} MB";
}
