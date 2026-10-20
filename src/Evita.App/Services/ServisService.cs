using System.IO;
using Evita.Data;
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

/// <summary>Informácie o databáze, ukážkové dáta a vymazanie dát.</summary>
public sealed class ServisService
{
    private readonly ZalohaService _zalohy = new();

    public InfoODatabaze Info()
    {
        using var db = Databaza.Otvor();
        return new InfoODatabaze(
            Databaza.Cesta,
            VelkostDatabazy(),
            db.Polozky.Count(),
            db.Polozky.Count(p => p.Stav == Models.StavPolozky.Vyradene),
            db.Kategorie.Count(),
            db.Miestnosti.Count(),
            db.Osoby.Count(),
            db.Historia.Count(),
            int.TryParse(Databaza.Citaj(db, Databaza.KlucVerziaSchemy), out var v) ? v : 0,
            Databaza.Citaj(db, Databaza.KlucUkazkoveData) == "1");
    }

    public static long VelkostDatabazy()
    {
        long velkost = 0;
        foreach (var subor in new[] { Databaza.Cesta, Databaza.Cesta + "-wal" })
        {
            if (File.Exists(subor))
            {
                velkost += new FileInfo(subor).Length;
            }
        }

        return velkost;
    }

    /// <summary>
    /// Vymaže položky, históriu, (miestnosti), (osoby). Kategórie a nastavenia zostanú,
    /// prírastkové číslo sa vráti na 1. Pred vymazaním sa vždy urobí záloha – vráti jej cestu.
    /// </summary>
    public string VymazatData(bool ponechatMiestnosti, bool ponechatOsoby)
    {
        var zaloha = _zalohy.Zalohuj(TypZalohy.PredVymazanim, poznamka: "Automatická záloha pred vymazaním dát");

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

        new DiagnostikaService().Kompaktuj();
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
}
