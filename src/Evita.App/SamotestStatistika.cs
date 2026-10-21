using Evita.Models;
using Evita.Services;
using Evita.ViewModels;
using Evita.Views;

namespace Evita;

/// <summary>Samotest obrazovky Štatistika: výpočty (kategórie, vek, plán obnovy, miestnosti …), podokno, zostava, prechod do Evidencie.</summary>
internal static partial class Samotest
{
    private static void SamotestStatistiky(MainWindow okno, MainViewModel model)
    {
        // výpočet na známych údajoch
        var dnes = new DateTime(2026, 10, 21);
        PolozkaStatistiky P(int id, string kategoria, string? miestnost, DateTime? nakup, decimal? cena, StavPolozky stav = StavPolozky.VPrevadzke) =>
            new(id, id, "Položka " + id, kategoria[0], kategoria, null, null, miestnost, null, stav, nakup, cena, null, null, null, null);
        var polozky = new List<PolozkaStatistiky>
        {
            P(1, "notebook", "Učebňa 1", dnes.AddYears(-6), 1000),
            P(2, "notebook", "Učebňa 1", dnes.AddYears(-2), 800),
            P(3, "tlačiareň", "Kabinet", dnes.AddYears(-4).AddMonths(-6), 300),
            P(4, "tlačiareň", null, null, null),
            P(5, "projektor", "Učebňa 1", dnes.AddYears(-11), 500, StavPolozky.VOprave),
        };
        var s = StatistikaEvidencie.Vypocitaj(polozky, dnes, 5);
        Over(s.Spolu == 5 && s.Hodnota == 2600 && s.Stare.Count == 2
             && s.Kategorie.Sum(r => r.Pocet) == 5 && s.Kategorie[0].Nazov == "notebook" && s.Kategorie[0].Suma == 1800
             && s.VekoveSkupiny.Sum(r => r.Pocet) == 5 && s.VekoveSkupiny.Last().Pocet == 1
             && s.PlanObnovy[0].Pocet == 2 && s.PlanObnovy[0].Suma == 1500
             && s.PlanObnovy.Single(r => r.Nazov == "2027").Pocet == 1 && s.PlanObnovy.Single(r => r.Nazov == "2029").Suma == 800
             && s.Miestnosti.Sum(r => r.Pocet) == 5 && s.Miestnosti[0].Nazov == "Učebňa 1" && s.Miestnosti.Last().Upozornit
             && s.Stavy.Single(r => r.Polozky.Any(p => p.Id == 5)).Pocet == 1
             && s.Kvalita[0].Pocet == 1 && s.Kvalita[1].Pocet == 1,
            "štatistika: kategórie, vek, plán obnovy po rokoch, miestnosti, stav a úplnosť údajov sedia");

        // obrazovka so vzorovými údajmi
        okno.OznacSekciu("statistika");
        Pumpuj();
        var st = model.Statistika;
        st.Nastav(false, 5);
        Pumpuj();
        var vypocet = st.Statistika!;
        Over(st.Bloky.Count == 10 && st.Dlazdice.Count == 5 && vypocet.Spolu > 0
             && vypocet.Kategorie.Sum(r => r.Pocet) == vypocet.Spolu && vypocet.Kategorie.Sum(r => r.Suma) == vypocet.Hodnota
             && vypocet.Miestnosti.Sum(r => r.Pocet) == vypocet.Spolu && vypocet.VekoveSkupiny.Sum(r => r.Pocet) == vypocet.Spolu,
            $"štatistika: {vypocet.Spolu} položiek, hodnota {Sk.Suma(vypocet.Hodnota)}, súčty blokov sedia");
        var kategoria = vypocet.Kategorie[0];
        st.VybratCommand.Execute(kategoria);
        Pumpuj();
        Over(st.JeVybrany && st.DetailPolozky.Count == kategoria.Pocet && st.DetailSuhrn.Contains(Sk.Suma(kategoria.Suma), StringComparison.Ordinal),
            $"štatistika: podokno s položkami kategórie {kategoria.Nazov} ({st.DetailSuhrn})");
        Snimka(okno, "okno_statistika_podokno");
        st.VybratCommand.Execute(vypocet.PlanObnovy[0]);
        Pumpuj();
        Snimka(okno, "okno_statistika_obnova");

        st.TlacitCommand.Execute(null);
        Pumpuj();
        Over(st.Posledny is { PocetStran: > 0 }, $"štatistika: zostava na tlač a PDF ({st.Posledny?.PocetStran} strán)");
        st.Posledny?.Close();
        st.TlacitPodoknoCommand.Execute(null);
        Pumpuj();
        Over(st.Posledny is { PocetStran: > 0 }, "štatistika: zoznam z podokna na tlač");
        st.Posledny?.Close();
        Pumpuj();

        var pocetObnova = vypocet.PlanObnovy[0].Pocet;
        st.ZobrazitVEvidenciiCommand.Execute(null);
        Pumpuj();
        Over(model.AktualnyObsah == model.Evidencia && model.Evidencia.Pohlad?.Count == pocetObnova,
            $"štatistika: položky z podokna v Evidencii ({model.Evidencia.Pohlad?.Count})");
        model.Evidencia.VycistitFiltreCommand.Execute(null);
        st.ZavrietPodoknoCommand.Execute(null);
        Pumpuj();
        Krok("štatistika v poriadku");
    }
}
