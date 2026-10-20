using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Evita.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Evita.Services;

public sealed record InfoODatabaze(
    string Cesta,
    long VelkostBajtov,
    int Polozky,
    int Vyradene,
    int Kategorie,
    int Miestnosti,
    int Osoby,
    int ZaznamyHistorie,
    int VerziaSchemy,
    bool UkazkoveData);

/// <summary>Zálohy, údržba a vymazanie databázy.</summary>
public sealed class ServisService
{
    public InfoODatabaze Info()
    {
        using var db = Databaza.Otvor();
        long velkost = 0;
        foreach (var subor in new[] { Databaza.Cesta, Databaza.Cesta + "-wal" })
        {
            if (File.Exists(subor))
            {
                velkost += new FileInfo(subor).Length;
            }
        }

        return new InfoODatabaze(
            Databaza.Cesta,
            velkost,
            db.Polozky.Count(),
            db.Polozky.Count(p => p.Stav == Models.StavPolozky.Vyradene),
            db.Kategorie.Count(),
            db.Miestnosti.Count(),
            db.Osoby.Count(),
            db.Historia.Count(),
            int.TryParse(Databaza.Citaj(db, Databaza.KlucVerziaSchemy), out var v) ? v : 0,
            Databaza.Citaj(db, Databaza.KlucUkazkoveData) == "1");
    }

    /// <summary>
    /// Vytvorí zálohu celej databázy do jedného súboru ZIP. Kópia databázy sa robí cez SQLite
    /// (VACUUM INTO), takže je v poriadku aj počas práce s programom. Vráti cestu k súboru.
    /// </summary>
    public string Zalohuj(string? priecinok = null, string? poznamka = null)
    {
        priecinok ??= Databaza.PriecinokZaloh;
        Directory.CreateDirectory(priecinok);

        var cas = DateTime.Now;
        var nazov = $"EviTA_zaloha_{cas:yyyy-MM-dd_HHmmss}";
        var docasnyPriecinok = Path.Combine(Path.GetTempPath(), "EviTA_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(docasnyPriecinok);

        try
        {
            var kopia = Path.Combine(docasnyPriecinok, "evita.db");
            using (var spojenie = Databaza.OtvorSpojenie())
            using (var prikaz = spojenie.CreateCommand())
            {
                prikaz.CommandText = "VACUUM INTO $cesta;";
                prikaz.Parameters.AddWithValue("$cesta", kopia);
                prikaz.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();

            var info = Info();
            string kontrolnySucet;
            using (var stream = File.OpenRead(kopia))
            {
                kontrolnySucet = Convert.ToHexString(SHA256.HashData(stream));
            }

            var popis = new
            {
                program = "EviTA",
                verziaProgramu = typeof(ServisService).Assembly.GetName().Version?.ToString(),
                verziaSchemy = info.VerziaSchemy,
                vytvorene = cas.ToString("yyyy-MM-ddTHH:mm:ss"),
                pocitac = Environment.MachineName,
                pouzivatel = Environment.UserName,
                polozky = info.Polozky,
                ukazkoveData = info.UkazkoveData,
                poznamka,
                sha256 = kontrolnySucet,
            };
            File.WriteAllText(Path.Combine(docasnyPriecinok, "info.json"),
                JsonSerializer.Serialize(popis, new JsonSerializerOptions { WriteIndented = true }));

            var ciel = Path.Combine(priecinok, nazov + ".zip");
            for (var poradie = 2; File.Exists(ciel); poradie++)
            {
                ciel = Path.Combine(priecinok, $"{nazov}_{poradie}.zip");
            }

            ZipFile.CreateFromDirectory(docasnyPriecinok, ciel, CompressionLevel.Optimal, includeBaseDirectory: false);
            return ciel;
        }
        finally
        {
            try
            {
                Directory.Delete(docasnyPriecinok, recursive: true);
            }
            catch (IOException)
            {
                // dočasný priečinok vymaže Windows neskôr
            }
        }
    }

    /// <summary>
    /// Vymaže položky, históriu, (miestnosti), (osoby). Kategórie a nastavenia zostanú,
    /// prírastkové číslo sa vráti na 1. Pred vymazaním sa vždy urobí záloha – vráti jej cestu.
    /// </summary>
    public string VymazatData(bool ponechatMiestnosti, bool ponechatOsoby)
    {
        var zaloha = Zalohuj(poznamka: "Automatická záloha pred vymazaním dát");

        using (var db = Databaza.Otvor())
        using (var transakcia = db.Database.BeginTransaction())
        {
            db.Historia.ExecuteDelete();
            db.Polozky.ExecuteDelete();
            if (!ponechatMiestnosti)
            {
                db.Miestnosti.ExecuteDelete();
            }

            if (!ponechatOsoby)
            {
                db.Osoby.ExecuteDelete();
            }

            Databaza.Zapis(db, Databaza.KlucPoslednePrirastkove, "0");
            Databaza.Zapis(db, Databaza.KlucUkazkoveData, "0");
            db.SaveChanges();
            transakcia.Commit();
        }

        Kompaktuj();
        return zaloha;
    }

    /// <summary>Vymaže všetko a naplní databázu ukážkovými dátami. Vráti cestu k zálohe (ak sa robila).</summary>
    public string? NacitatUkazkoveData()
    {
        string? zaloha = null;
        using (var db = Databaza.Otvor())
        {
            if (db.Polozky.Any() || db.Miestnosti.Any() || db.Osoby.Any())
            {
                zaloha = VymazatData(ponechatMiestnosti: false, ponechatOsoby: false);
            }
        }

        using (var db = Databaza.Otvor())
        {
            UkazkoveData.Generuj(db);
        }

        return zaloha;
    }

    /// <summary>Uvoľní nepoužité miesto v súbore databázy.</summary>
    public void Kompaktuj()
    {
        using var spojenie = Databaza.OtvorSpojenie();
        using var prikaz = spojenie.CreateCommand();
        prikaz.CommandText = "VACUUM;";
        prikaz.ExecuteNonQuery();
    }
}
