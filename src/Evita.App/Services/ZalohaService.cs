using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Evita.Data;
using Evita.Models;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

public enum TypZalohy
{
    Rucna,
    Automaticka,
    PredOperaciou,
    PredAktualizaciou,
    PredObnovou,
    PredVymazanim,
    PredImportom,
}

public enum PeriodaZaloh
{
    Denne,
    Tyzdenne,
    Mesacne,
    Rocne,
}

public sealed record NastaveniaZaloh(
    bool Zapnuta,
    PeriodaZaloh Perioda,
    string Priecinok,
    int Ponechat,
    bool PlanovacWindows,
    string Cas,
    bool AutomatickaSHeslom = false);

public sealed record InfoOZalohe(
    string Subor,
    DateTime? Vytvorene,
    string? VerziaProgramu,
    int VerziaSchemy,
    int Polozky,
    bool UkazkoveData,
    bool Zasifrovana,
    string? Pocitac,
    string? Poznamka);

/// <summary>Záloha je zašifrovaná a treba zadať heslo.</summary>
public sealed class HesloPotrebneException : Exception
{
    public HesloPotrebneException() : base("Záloha je chránená heslom.")
    {
    }
}

/// <summary>Zálohy (ručné, pravidelné, pred rizikovými operáciami) a obnova zo zálohy.</summary>
public sealed class ZalohaService
{
    public const string NazovUlohyPlanovaca = "EviTA zaloha";

    private const string KlucZapnuta = "Zaloha.Zapnuta";
    private const string KlucPerioda = "Zaloha.Perioda";
    private const string KlucPriecinok = "Zaloha.Priecinok";
    private const string KlucPonechat = "Zaloha.Ponechat";
    private const string KlucPlanovac = "Zaloha.PlanovacWindows";
    private const string KlucCas = "Zaloha.Cas";
    private const string KlucSHeslom = "Zaloha.AutomatickaSHeslom";
    private const string KlucHeslo = "Zaloha.HesloAutomatickych";

    /// <summary>Doplnková hodnota pri šifrovaní hesla ochranou Windows (DPAPI).</summary>
    private static readonly byte[] Entropia = "EviTA.Zaloha.Heslo.v1"u8.ToArray();
    private const int PonechatPredOperaciou = 10;

    private static readonly object Zamok = new();

    public static string NazovPeriody(PeriodaZaloh perioda) => perioda switch
    {
        PeriodaZaloh.Denne => "denne",
        PeriodaZaloh.Tyzdenne => "týždenne",
        PeriodaZaloh.Mesacne => "mesačne",
        _ => "ročne",
    };

    public static string NazovTypu(TypZalohy typ) => typ switch
    {
        TypZalohy.Rucna => "ručná",
        TypZalohy.Automaticka => "automatická",
        TypZalohy.PredAktualizaciou => "pred aktualizáciou",
        TypZalohy.PredObnovou => "pred obnovou",
        TypZalohy.PredVymazanim => "pred vymazaním dát",
        TypZalohy.PredImportom => "pred importom",
        _ => "pred operáciou",
    };

    /// <summary>
    /// Celý popis typu pre záznam záloh: „ručná záloha“, „ručná záloha s heslom“, „automatická záloha s heslom“,
    /// „záloha pred aktualizáciou“, „obnova zo zálohy“ …
    /// </summary>
    public static string PopisTypu(ZaznamZalohy zaznam)
    {
        var popis = zaznam.Typ switch
        {
            "ručná" => "ručná záloha",
            "automatická" => "automatická záloha",
            "obnova" => "obnova zo zálohy",
            _ => "záloha " + zaznam.Typ,
        };
        return zaznam.Zasifrovana ? popis + " s heslom" : popis;
    }

    // ------------------------------------------------------------------ nastavenia

    public NastaveniaZaloh NacitajNastavenia()
    {
        using var db = Databaza.Otvor();
        var zapnuta = Databaza.Citaj(db, KlucZapnuta) != "0";
        var perioda = Enum.TryParse<PeriodaZaloh>(Databaza.Citaj(db, KlucPerioda), out var p) ? p : PeriodaZaloh.Tyzdenne;
        var priecinok = Databaza.Citaj(db, KlucPriecinok) is { Length: > 0 } pr ? pr : Databaza.PriecinokZaloh;
        var ponechat = int.TryParse(Databaza.Citaj(db, KlucPonechat), out var n) && n > 0 ? n : 10;
        var planovac = Databaza.Citaj(db, KlucPlanovac) == "1";
        var cas = Databaza.Citaj(db, KlucCas) is { Length: > 0 } c ? c : "20:00";
        var sHeslom = Databaza.Citaj(db, KlucSHeslom) == "1";
        return new NastaveniaZaloh(zapnuta, perioda, priecinok, ponechat, planovac, cas, sHeslom);
    }

    public void UlozNastavenia(NastaveniaZaloh nastavenia)
    {
        if (nastavenia.Ponechat is < 1 or > 999)
        {
            throw new EvitaException("Počet ponechaných záloh musí byť od 1 do 999.");
        }

        if (!TimeSpan.TryParseExact(nastavenia.Cas.Trim(), new[] { @"h\:mm", @"hh\:mm" }, null, out var cas) || cas.TotalHours >= 24)
        {
            throw new EvitaException("Čas zálohy zadajte v tvare hodiny:minúty, napríklad 20:00.");
        }

        if (string.IsNullOrWhiteSpace(nastavenia.Priecinok))
        {
            throw new EvitaException("Zadajte priečinok na zálohy.");
        }

        if (nastavenia.AutomatickaSHeslom && !JeHesloAutomatickychNastavene())
        {
            throw new EvitaException("Pre automatické zálohy s heslom najprv nastavte heslo (tlačidlo „Nastaviť heslo…“).");
        }

        try
        {
            Directory.CreateDirectory(nastavenia.Priecinok);
        }
        catch (Exception ex)
        {
            throw new EvitaException($"Do priečinka „{nastavenia.Priecinok}“ sa nedá zapisovať: {ex.Message}");
        }

        var casText = cas.ToString(@"hh\:mm");
        NastavPlanovac(nastavenia with { Cas = casText });

        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucZapnuta, nastavenia.Zapnuta ? "1" : "0");
        Databaza.Zapis(db, KlucPerioda, nastavenia.Perioda.ToString());
        Databaza.Zapis(db, KlucPriecinok, nastavenia.Priecinok.Trim());
        Databaza.Zapis(db, KlucPonechat, nastavenia.Ponechat.ToString());
        Databaza.Zapis(db, KlucPlanovac, nastavenia.PlanovacWindows ? "1" : "0");
        Databaza.Zapis(db, KlucCas, casText);
        Databaza.Zapis(db, KlucSHeslom, nastavenia.AutomatickaSHeslom ? "1" : "0");
        db.SaveChanges();
    }

    /// <summary>
    /// Uloží heslo pre automatické zálohy zašifrované ochranou Windows (DPAPI) – rozšifrovať ho dokáže len tento
    /// počítač. Null heslo zmaže.
    /// </summary>
    public void NastavHesloAutomatickych(string? heslo)
    {
        string? ulozene = null;
        if (!string.IsNullOrEmpty(heslo))
        {
            var zasifrovane = System.Security.Cryptography.ProtectedData.Protect(
                System.Text.Encoding.UTF8.GetBytes(heslo), Entropia, System.Security.Cryptography.DataProtectionScope.LocalMachine);
            ulozene = Convert.ToBase64String(zasifrovane);
        }

        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucHeslo, ulozene);
        if (ulozene is null)
        {
            Databaza.Zapis(db, KlucSHeslom, "0");
        }

        db.SaveChanges();
    }

    public bool JeHesloAutomatickychNastavene() => NacitajHesloAutomatickych() is not null;

    private static string? NacitajHesloAutomatickych()
    {
        try
        {
            using var db = Databaza.Otvor();
            if (Databaza.Citaj(db, KlucHeslo) is not { Length: > 0 } ulozene)
            {
                return null;
            }

            var odsifrovane = System.Security.Cryptography.ProtectedData.Unprotect(
                Convert.FromBase64String(ulozene), Entropia, System.Security.Cryptography.DataProtectionScope.LocalMachine);
            return System.Text.Encoding.UTF8.GetString(odsifrovane);
        }
        catch (Exception)
        {
            // heslo sa nedá rozšifrovať (napr. databáza prenesená z iného počítača)
            return null;
        }
    }

    // ------------------------------------------------------------------ stav

    public ZaznamZalohy? PoslednyZaznam()
    {
        using var db = Databaza.Otvor();
        return db.ZaznamyZaloh.AsNoTracking()
            .Where(z => z.Typ != "obnova")
            .OrderByDescending(z => z.Cas).ThenByDescending(z => z.Id)
            .FirstOrDefault();
    }

    public DateTime? PoslednaUspesna()
    {
        using var db = Databaza.Otvor();
        return db.ZaznamyZaloh.AsNoTracking()
            .Where(z => z.Uspech && z.Typ != "obnova")
            .OrderByDescending(z => z.Cas)
            .Select(z => (DateTime?)z.Cas)
            .FirstOrDefault();
    }

    public List<ZaznamZalohy> Zaznamy(int pocet = 30)
    {
        using var db = Databaza.Otvor();
        return db.ZaznamyZaloh.AsNoTracking()
            .OrderByDescending(z => z.Cas).ThenByDescending(z => z.Id)
            .Take(pocet)
            .ToList();
    }

    /// <summary>Kedy treba urobiť ďalšiu pravidelnú zálohu.</summary>
    public static DateTime DalsiTermin(DateTime posledna, PeriodaZaloh perioda) => perioda switch
    {
        PeriodaZaloh.Denne => posledna.Date.AddDays(1),
        PeriodaZaloh.Tyzdenne => posledna.Date.AddDays(7),
        PeriodaZaloh.Mesacne => posledna.Date.AddMonths(1),
        _ => posledna.Date.AddYears(1),
    };

    public bool JeCasNaZalohu(NastaveniaZaloh nastavenia)
    {
        if (!nastavenia.Zapnuta)
        {
            return false;
        }

        var posledna = PoslednaUspesna();
        return posledna is null || DateTime.Now >= DalsiTermin(posledna.Value, nastavenia.Perioda);
    }

    /// <summary>Urobí pravidelnú zálohu, ak už je čas. Chybu len zapíše do záznamu záloh. Vráti cestu alebo null.</summary>
    public string? AutomatickaAkTreba()
    {
        try
        {
            var nastavenia = NacitajNastavenia();
            return JeCasNaZalohu(nastavenia) ? Zalohuj(TypZalohy.Automaticka) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ záloha

    /// <summary>
    /// Vytvorí zálohu celej databázy do jedného súboru ZIP (voliteľne zašifrovaného heslom AES-256).
    /// Kópia databázy sa robí cez SQLite (VACUUM INTO), takže je v poriadku aj počas práce s programom.
    /// Každý pokus sa zapíše do záznamu záloh. Vráti cestu k súboru.
    /// </summary>
    public string Zalohuj(TypZalohy typ, string? heslo = null, string? poznamka = null, string? priecinok = null)
    {
        lock (Zamok)
        {
            var nastavenia = NacitajNastavenia();
            priecinok ??= nastavenia.Priecinok;
            var cas = DateTime.Now;
            if (typ == TypZalohy.Automaticka && nastavenia.AutomatickaSHeslom && heslo is null)
            {
                heslo = NacitajHesloAutomatickych();
                if (heslo is null)
                {
                    Zaznamenaj(new ZaznamZalohy
                    {
                        Cas = cas,
                        Typ = NazovTypu(typ),
                        Cesta = priecinok,
                        Uspech = false,
                        Zasifrovana = true,
                        Chyba = "Heslo pre automatické zálohy nie je dostupné na tomto počítači. Nastavte ho znova v Servis → Zálohy.",
                    });
                    throw new EvitaException("Heslo pre automatické zálohy nie je dostupné. Nastavte ho znova v Servis → Zálohy.");
                }
            }

            var docasny = Path.Combine(Path.GetTempPath(), "EviTA_" + Guid.NewGuid().ToString("N"));
            string? ciel = null;

            try
            {
                Directory.CreateDirectory(priecinok);
                Directory.CreateDirectory(docasny);

                var kopia = Path.Combine(docasny, "evita.db");
                using (var spojenie = Databaza.OtvorSpojenie())
                using (var prikaz = spojenie.CreateCommand())
                {
                    prikaz.CommandText = "VACUUM INTO $cesta;";
                    prikaz.Parameters.AddWithValue("$cesta", kopia);
                    prikaz.ExecuteNonQuery();
                }

                int polozky;
                bool ukazkove;
                using (var db = Databaza.Otvor())
                {
                    polozky = db.Polozky.Count();
                    ukazkove = Databaza.Citaj(db, Databaza.KlucUkazkoveData) == "1";
                }

                var popis = new
                {
                    program = "EviTA",
                    verziaProgramu = typeof(ZalohaService).Assembly.GetName().Version?.ToString(3),
                    verziaSchemy = Databaza.VerziaSchemy,
                    vytvorene = cas.ToString("yyyy-MM-ddTHH:mm:ss"),
                    typ = NazovTypu(typ),
                    pocitac = Environment.MachineName,
                    pouzivatel = Environment.UserName,
                    polozky,
                    ukazkoveData = ukazkove,
                    poznamka,
                    sha256 = KontrolnySucet(kopia),
                };
                var info = Path.Combine(docasny, "info.json");
                File.WriteAllText(info, JsonSerializer.Serialize(popis, new JsonSerializerOptions { WriteIndented = true }));

                var predpona = typ switch
                {
                    TypZalohy.Rucna => "EviTA_zaloha_",
                    TypZalohy.Automaticka => "EviTA_auto_",
                    TypZalohy.PredAktualizaciou => "EviTA_pred_aktualizaciou_",
                    TypZalohy.PredObnovou => "EviTA_pred_obnovou_",
                    TypZalohy.PredVymazanim => "EviTA_pred_vymazanim_",
                    TypZalohy.PredImportom => "EviTA_pred_importom_",
                    _ => "EviTA_pred_",
                };
                ciel = CislovanaCesta(priecinok, $"{predpona}{cas:yyyy-MM-dd_HHmmss}", ".zip");

                // Kalendárium (meniny, sviatky a vlastné dáta) – ak je dostupné, ide do zálohy tiež
                var subory = new List<(string, string)> { ("evita.db", kopia), ("info.json", info) };
                try
                {
                    var kalendarium = Kalendarium.KalendariumDb.Spolocna;
                    if (kalendarium.Existuje)
                    {
                        var kopiaKalendaria = Path.Combine(docasny, "kalendarium.db");
                        kalendarium.SkopirujDo(kopiaKalendaria);
                        subory.Add(("kalendarium.db", kopiaKalendaria));
                    }
                }
                catch (Exception)
                {
                    // záloha evidencie je dôležitejšia ako záloha Kalendária
                }

                ZapisZip(ciel, heslo, cas, subory.ToArray());

                Zaznamenaj(new ZaznamZalohy
                {
                    Cas = cas,
                    Typ = NazovTypu(typ),
                    Cesta = ciel,
                    Velkost = new FileInfo(ciel).Length,
                    Uspech = true,
                    Zasifrovana = heslo is not null,
                });

                if (typ == TypZalohy.Automaticka)
                {
                    Rotuj(priecinok, "*EviTA_auto_*.zip", nastavenia.Ponechat);
                }
                else if (typ is not TypZalohy.Rucna)
                {
                    Rotuj(priecinok, "*EviTA_pred_*.zip", PonechatPredOperaciou);
                }

                return ciel;
            }
            catch (Exception ex)
            {
                if (ciel is not null && File.Exists(ciel))
                {
                    TichoVymaz(ciel);
                }

                try
                {
                    Zaznamenaj(new ZaznamZalohy
                    {
                        Cas = cas,
                        Typ = NazovTypu(typ),
                        Cesta = priecinok,
                        Uspech = false,
                        Zasifrovana = heslo is not null,
                        Chyba = ex.GetBaseException().Message,
                    });
                }
                catch (Exception)
                {
                    // ak nejde zapísať ani záznam, pôvodná chyba je dôležitejšia
                }

                throw new EvitaException($"Zálohu sa nepodarilo vytvoriť: {ex.GetBaseException().Message}");
            }
            finally
            {
                TichoVymazPriecinok(docasny);
            }
        }
    }

    // ------------------------------------------------------------------ obnova

    public bool JeZasifrovana(string zip)
    {
        using var subor = new ZipFile(zip);
        foreach (ZipEntry polozka in subor)
        {
            if (polozka.IsCrypted)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Prečíta popis zálohy (a pri zašifrovanej overí heslo).</summary>
    public InfoOZalohe PrecitajInfo(string zip, string? heslo)
    {
        var docasny = Path.Combine(Path.GetTempPath(), "EviTA_" + Guid.NewGuid().ToString("N"));
        try
        {
            return Rozbal(zip, heslo, docasny, lenInfo: true);
        }
        finally
        {
            TichoVymazPriecinok(docasny);
        }
    }

    /// <summary>
    /// Nahradí súčasnú databázu databázou zo zálohy. Pred tým overí kontrolný súčet a integritu zálohy
    /// a súčasný stav zálohuje. Vráti cestu k bezpečnostnej zálohe súčasného stavu.
    /// </summary>
    public string? Obnov(string zip, string? heslo)
    {
        lock (Zamok)
        {
            var docasny = Path.Combine(Path.GetTempPath(), "EviTA_" + Guid.NewGuid().ToString("N"));
            try
            {
                var info = Rozbal(zip, heslo, docasny, lenInfo: false);
                var subor = Path.Combine(docasny, "evita.db");

                int verziaZalohy;
                using (var spojenie = new SqliteConnection($"Data Source={subor};Pooling=False"))
                {
                    spojenie.Open();
                    using (var prikaz = spojenie.CreateCommand())
                    {
                        prikaz.CommandText = "PRAGMA integrity_check;";
                        var vysledok = prikaz.ExecuteScalar() as string;
                        if (vysledok != "ok")
                        {
                            throw new EvitaException($"Databáza v zálohe je poškodená ({vysledok}). Vyberte inú zálohu.");
                        }
                    }

                    using (var prikaz = spojenie.CreateCommand())
                    {
                        prikaz.CommandText = "SELECT \"Hodnota\" FROM \"Nastavenia\" WHERE \"Kluc\" = $kluc;";
                        prikaz.Parameters.AddWithValue("$kluc", Databaza.KlucVerziaSchemy);
                        verziaZalohy = int.TryParse(prikaz.ExecuteScalar() as string, out var v) ? v : 1;
                    }
                }

                if (verziaZalohy > Databaza.VerziaSchemy)
                {
                    throw new EvitaException("Záloha pochádza z novšej verzie programu EviTA. Najprv nainštalujte najnovšiu verziu programu.");
                }

                // bezpečnostná záloha súčasného stavu
                string? bezpecnostna = null;
                try
                {
                    bezpecnostna = Zalohuj(TypZalohy.PredObnovou, poznamka: "Automatická záloha pred obnovou zo zálohy");
                }
                catch (Exception)
                {
                    // súčasná databáza môže byť poškodená – uložíme aspoň surovú kópiu súboru
                    if (File.Exists(Databaza.Cesta))
                    {
                        var nastavenia = NacitajNastaveniaBezpecne();
                        Directory.CreateDirectory(nastavenia);
                        bezpecnostna = CislovanaCesta(nastavenia, $"EviTA_pred_obnovou_subor_{DateTime.Now:yyyy-MM-dd_HHmmss}", ".db");
                        File.Copy(Databaza.Cesta, bezpecnostna, overwrite: true);
                    }
                }

                SqliteConnection.ClearAllPools();
                foreach (var pripona in new[] { "", "-wal", "-shm" })
                {
                    if (File.Exists(Databaza.Cesta + pripona))
                    {
                        File.Delete(Databaza.Cesta + pripona);
                    }
                }

                File.Copy(subor, Databaza.Cesta);
                Databaza.Inicializuj();

                var teraz = DateTime.Now;
                if (bezpecnostna is not null)
                {
                    Zaznamenaj(new ZaznamZalohy
                    {
                        Cas = teraz,
                        Typ = NazovTypu(TypZalohy.PredObnovou),
                        Cesta = bezpecnostna,
                        Velkost = new FileInfo(bezpecnostna).Length,
                        Uspech = true,
                    });
                }

                Zaznamenaj(new ZaznamZalohy
                {
                    Cas = teraz,
                    Typ = "obnova",
                    Cesta = zip,
                    Velkost = new FileInfo(zip).Length,
                    Uspech = true,
                    Zasifrovana = info.Zasifrovana,
                });

                return bezpecnostna;
            }
            finally
            {
                TichoVymazPriecinok(docasny);
            }
        }
    }

    // ------------------------------------------------------------------ číslovanie záloh

    /// <summary>
    /// Cesta pre nový súbor zálohy s poradovým číslom na začiatku, napr. „00012_EviTA_zaloha_2026-10-20_122501.zip“.
    /// Čísla sú v priečinku jedinečné a stúpajú, takže zoradenie podľa názvu je zoradenie podľa poradia.
    /// </summary>
    public static string CislovanaCesta(string priecinok, string zaklad, string pripona)
    {
        lock (Zamok)
        {
            Directory.CreateDirectory(priecinok);
            var cislo = DalsieCislo(priecinok);
            var cesta = Path.Combine(priecinok, $"{cislo:D5}_{zaklad}{pripona}");
            while (File.Exists(cesta))
            {
                cislo++;
                cesta = Path.Combine(priecinok, $"{cislo:D5}_{zaklad}{pripona}");
            }

            return cesta;
        }
    }

    /// <summary>Najbližšie voľné poradové číslo zálohy v priečinku.</summary>
    public static int DalsieCislo(string priecinok)
    {
        var najvacsie = 0;
        if (Directory.Exists(priecinok))
        {
            foreach (var subor in Directory.EnumerateFiles(priecinok, "*_EviTA_*"))
            {
                var nazov = Path.GetFileName(subor);
                var podciarka = nazov.IndexOf('_');
                if (podciarka > 0 && int.TryParse(nazov[..podciarka], out var cislo))
                {
                    najvacsie = Math.Max(najvacsie, cislo);
                }
            }
        }

        return najvacsie + 1;
    }

    /// <summary>
    /// Zálohy zo starších verzií programu (bez poradového čísla) prečísluje podľa dátumu vzniku
    /// a opraví na ne odkazy v zázname záloh. Vráti počet premenovaných súborov.
    /// </summary>
    /// <summary>
    /// Záznamy bezpečnostných záloh zo starších verzií mali spoločný typ „pred operáciou“. Podľa poznámky
    /// v zálohe im doplní presný typ (pred aktualizáciou, pred obnovou, pred vymazaním dát). Robí sa raz.
    /// </summary>
    public int OpravTypyStarychZaznamov()
    {
        const string kluc = "Zaloha.TypyOpravene";
        var opravene = 0;
        using var db = Databaza.Otvor();
        if (Databaza.Citaj(db, kluc) == "1")
        {
            return 0;
        }

        var vseobecny = NazovTypu(TypZalohy.PredOperaciou);
        foreach (var zaznam in db.ZaznamyZaloh.Where(z => z.Typ == vseobecny).ToList())
        {
            if (zaznam.Cesta is null || !zaznam.Cesta.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(zaznam.Cesta))
            {
                continue;
            }

            try
            {
                var poznamka = PrecitajInfo(zaznam.Cesta, null).Poznamka ?? "";
                TypZalohy? typ = poznamka.Contains("aktualiz", StringComparison.OrdinalIgnoreCase) ? TypZalohy.PredAktualizaciou
                    : poznamka.Contains("obnov", StringComparison.OrdinalIgnoreCase) ? TypZalohy.PredObnovou
                    : poznamka.Contains("vymaz", StringComparison.OrdinalIgnoreCase) ? TypZalohy.PredVymazanim
                    : null;
                if (typ is { } presny)
                {
                    zaznam.Typ = NazovTypu(presny);
                    opravene++;
                }
            }
            catch (Exception)
            {
                // zálohu sa nepodarilo prečítať – typ ponecháme
            }
        }

        Databaza.Zapis(db, kluc, "1");
        db.SaveChanges();
        return opravene;
    }

    public int PrecislujStareZalohy()
    {
        var priecinky = new[] { NacitajNastavenia().Priecinok, Databaza.PriecinokZaloh }
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var premenovane = 0;

        lock (Zamok)
        {
            using var db = Databaza.Otvor();
            foreach (var priecinok in priecinky)
            {
                var stare = new DirectoryInfo(priecinok).GetFiles("EviTA_*")
                    .Where(f => f.Extension is ".zip" or ".db")
                    .OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal)
                    .ToList();
                var cislo = DalsieCislo(priecinok);

                foreach (var subor in stare)
                {
                    var novy = Path.Combine(priecinok, $"{cislo:D5}_{subor.Name}");
                    if (File.Exists(novy))
                    {
                        continue;
                    }

                    File.Move(subor.FullName, novy);
                    if (File.Exists(subor.FullName + "-wal"))
                    {
                        File.Move(subor.FullName + "-wal", novy + "-wal");
                    }

                    foreach (var zaznam in db.ZaznamyZaloh.Where(z => z.Cesta == subor.FullName))
                    {
                        zaznam.Cesta = novy;
                    }

                    cislo++;
                    premenovane++;
                }
            }

            db.SaveChanges();
        }

        return premenovane;
    }

    // ------------------------------------------------------------------ Plánovač úloh Windows

    public bool JePlanovacNastaveny()
    {
        try
        {
            return SpustiSchtasks("/Query", "/TN", NazovUlohyPlanovaca).Kod == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void NastavPlanovac(NastaveniaZaloh nastavenia)
    {
        if (!nastavenia.Zapnuta || !nastavenia.PlanovacWindows)
        {
            try
            {
                SpustiSchtasks("/Delete", "/TN", NazovUlohyPlanovaca, "/F");
            }
            catch (Exception)
            {
                // úloha neexistuje alebo plánovač nie je dostupný – nevadí
            }

            return;
        }

        var program = Environment.ProcessPath ?? throw new EvitaException("Nepodarilo sa zistiť umiestnenie programu.");
        var argumenty = new List<string>
        {
            "/Create", "/F", "/TN", NazovUlohyPlanovaca,
            "/TR", $"\"{program}\" --zaloha",
            "/ST", nastavenia.Cas,
        };
        argumenty.AddRange(nastavenia.Perioda switch
        {
            PeriodaZaloh.Denne => new[] { "/SC", "DAILY" },
            PeriodaZaloh.Tyzdenne => new[] { "/SC", "WEEKLY", "/D", "MON" },
            PeriodaZaloh.Mesacne => new[] { "/SC", "MONTHLY", "/D", "1" },
            _ => new[] { "/SC", "MONTHLY", "/MO", "12", "/D", "1" },
        });

        var (kod, vystup) = SpustiSchtasks(argumenty.ToArray());
        if (kod != 0)
        {
            throw new EvitaException($"Úlohu v Plánovači úloh Windows sa nepodarilo vytvoriť.\n\n{vystup.Trim()}");
        }
    }

    private static (int Kod, string Vystup) SpustiSchtasks(params string[] argumenty)
    {
        var start = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in argumenty)
        {
            start.ArgumentList.Add(argument);
        }

        using var proces = Process.Start(start) ?? throw new EvitaException("Plánovač úloh Windows sa nepodarilo spustiť.");
        var vystup = proces.StandardOutput.ReadToEnd() + proces.StandardError.ReadToEnd();
        proces.WaitForExit(20000);
        return (proces.ExitCode, vystup);
    }

    // ------------------------------------------------------------------ pomocné

    private static void ZapisZip(string ciel, string? heslo, DateTime cas, params (string Nazov, string Cesta)[] subory)
    {
        using var vystup = File.Create(ciel);
        using var zip = new ZipOutputStream(vystup);
        zip.SetLevel(6);
        zip.UseZip64 = UseZip64.Off;
        if (heslo is not null)
        {
            zip.Password = heslo;
        }

        foreach (var (nazov, cesta) in subory)
        {
            var polozka = new ZipEntry(nazov) { DateTime = cas };
            if (heslo is not null)
            {
                polozka.AESKeySize = 256;
            }

            zip.PutNextEntry(polozka);
            using (var zdroj = File.OpenRead(cesta))
            {
                zdroj.CopyTo(zip);
            }

            zip.CloseEntry();
        }

        zip.Finish();
    }

    private InfoOZalohe Rozbal(string zip, string? heslo, string docasny, bool lenInfo)
    {
        if (!File.Exists(zip))
        {
            throw new EvitaException("Súbor zálohy neexistuje.");
        }

        Directory.CreateDirectory(docasny);
        ZipFile subor;
        try
        {
            subor = new ZipFile(zip);
        }
        catch (Exception)
        {
            throw new EvitaException("Súbor nie je platná záloha EviTA (nedá sa otvoriť ako ZIP).");
        }

        using (subor)
        {
            var databaza = subor.GetEntry("evita.db") ?? throw new EvitaException("Súbor nie je záloha EviTA (chýba v ňom databáza).");
            var popis = subor.GetEntry("info.json");
            var zasifrovana = databaza.IsCrypted;

            if (zasifrovana)
            {
                if (string.IsNullOrEmpty(heslo))
                {
                    throw new HesloPotrebneException();
                }

                subor.Password = heslo;
            }

            JsonElement? json = null;
            try
            {
                if (popis is not null)
                {
                    using var citac = new StreamReader(subor.GetInputStream(popis));
                    json = JsonDocument.Parse(citac.ReadToEnd()).RootElement.Clone();
                }

                if (!lenInfo || popis is null)
                {
                    using var vstup = subor.GetInputStream(databaza);
                    using var vystupDb = File.Create(Path.Combine(docasny, "evita.db"));
                    vstup.CopyTo(vystupDb);
                }
            }
            catch (ZipException) when (zasifrovana)
            {
                throw new EvitaException("Nesprávne heslo zálohy.");
            }

            string? Text(string nazov) =>
                json is { } j && j.TryGetProperty(nazov, out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;

            int Cislo(string nazov) =>
                json is { } j && j.TryGetProperty(nazov, out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : 0;

            if (!lenInfo && Text("sha256") is { Length: > 0 } sucet)
            {
                if (!string.Equals(sucet, KontrolnySucet(Path.Combine(docasny, "evita.db")), StringComparison.OrdinalIgnoreCase))
                {
                    throw new EvitaException("Kontrolný súčet databázy v zálohe nesedí – súbor je poškodený. Vyberte inú zálohu.");
                }
            }

            return new InfoOZalohe(
                zip,
                DateTime.TryParse(Text("vytvorene"), out var vytvorene) ? vytvorene : null,
                Text("verziaProgramu"),
                Cislo("verziaSchemy"),
                Cislo("polozky"),
                json is { } js && js.TryGetProperty("ukazkoveData", out var u) && u.ValueKind == JsonValueKind.True,
                zasifrovana,
                Text("pocitac"),
                Text("poznamka"));
        }
    }

    private string NacitajNastaveniaBezpecne()
    {
        try
        {
            return NacitajNastavenia().Priecinok;
        }
        catch (Exception)
        {
            return Databaza.PriecinokZaloh;
        }
    }

    private static void Zaznamenaj(ZaznamZalohy zaznam)
    {
        using var db = Databaza.Otvor();
        db.ZaznamyZaloh.Add(zaznam);
        db.SaveChanges();
    }

    private static void Rotuj(string priecinok, string vzor, int ponechat)
    {
        var stare = new DirectoryInfo(priecinok).GetFiles(vzor)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(ponechat);
        foreach (var subor in stare)
        {
            TichoVymaz(subor.FullName);
        }
    }

    private static string KontrolnySucet(string subor)
    {
        using var stream = File.OpenRead(subor);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void TichoVymaz(string subor)
    {
        try
        {
            File.Delete(subor);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TichoVymazPriecinok(string priecinok)
    {
        try
        {
            if (Directory.Exists(priecinok))
            {
                Directory.Delete(priecinok, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
