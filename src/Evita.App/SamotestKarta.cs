using System.Text.Json;
using Evita.Services;
using Evita.ViewModels;

namespace Evita;

/// <summary>Samotest karty novej položky: doplnenie záruky a revízie podľa dátumu nákupu, výrobca a model podľa názvu.</summary>
internal static partial class Samotest
{
    private static void KartaDoplnanie()
    {
        // ------------------------------------------------------------------ dátumy
        var karta = new PolozkaEditViewModel(null, null);
        karta.DatumNakupu = new DateTime(2026, 10, 21);
        Over(karta.ZarukaDo == new DateTime(2028, 10, 21) && karta.DalsiaRevizia == new DateTime(2027, 10, 21),
            $"dátum nákupu 21. 10. 2026 → záruka 21. 10. 2028, revízia 21. 10. 2027 (je {karta.ZarukaDo:d}, {karta.DalsiaRevizia:d})");
        karta.DatumNakupu = new DateTime(2025, 12, 24);
        Over(karta.ZarukaDo == new DateTime(2027, 12, 24) && karta.DalsiaRevizia == new DateTime(2026, 12, 28),
            $"zmena dátumu nákupu prepočíta automatické dátumy; revízia cez sviatky a víkend až na pondelok 28. 12. 2026 (je {karta.DalsiaRevizia:d})");
        karta.ZarukaDo = new DateTime(2029, 1, 31);
        karta.DatumNakupu = new DateTime(2026, 2, 27);
        Over(karta.ZarukaDo == new DateTime(2029, 1, 31) && karta.DalsiaRevizia == new DateTime(2027, 3, 1),
            $"ručne opravená záruka sa neprepíše; revízia zo soboty 27. 2. 2027 na pondelok 1. 3. 2027 (je {karta.ZarukaDo:d}, {karta.DalsiaRevizia:d})");
        Over(PracovneDni.NajblizsiPracovny(new DateTime(2027, 5, 1)) == new DateTime(2027, 5, 3)
             && PracovneDni.NajblizsiPracovny(new DateTime(2027, 1, 1)) == new DateTime(2027, 1, 4)
             && PracovneDni.NajblizsiPracovny(new DateTime(2027, 10, 20)) == new DateTime(2027, 10, 20),
            "najbližší pracovný deň (sviatok, víkend, bežný deň)");

        // ------------------------------------------------------------------ výrobca a model z názvu (bez internetu)
        var evidencia = VyrobcaModelService.NacitajZEvidencie();
        Over(evidencia.Count > 0, $"výrobcovia a modely z evidencie ({evidencia.Count})");
        (string Nazov, string? Vyrobca, string? Model)[] pripady =
        {
            ("Router TP-Link Archer AX55", "TP-Link", "Archer AX55"),
            ("Notebook HP ProBook 450 G9", "HP", "ProBook 450 G9"),
            ("Tlačiareň Brother HL-L2350DW (kancelária)", "Brother", "HL-L2350DW"),
            ("Monitor dell p2422h", "Dell", "P2422H"),
            ("Router tp link Archer C6", "TP-Link", "Archer C6"),
            ("Záložný zdroj Eaton 3S Mini 36W", "Eaton", "3S Mini 36W"),
            ("Kancelárska stolička", null, null),
        };
        foreach (var (nazov, vyrobca, model) in pripady)
        {
            var navrh = VyrobcaModelService.ZNazvu(nazov, evidencia);
            Over(navrh?.Vyrobca == vyrobca && navrh?.Model == model,
                $"výrobca a model z názvu „{nazov}“ → {vyrobca ?? "nič"} / {model ?? "nič"} (je {navrh?.Vyrobca ?? "nič"} / {navrh?.Model ?? "nič"}, {navrh?.Zdroj})");
        }

        // odpoveď UPCitemdb (ukážka): výrobca podľa väčšiny, model len ak je v názve
        using (var ukazka = JsonDocument.Parse("""
            {"code":"OK","items":[
              {"title":"Dell Latitude 5540 Laptop","brand":"Dell","model":"Latitude 5540"},
              {"title":"DELL Latitude 15 5540","brand":"DELL","model":"5540"},
              {"title":"Case for laptop","brand":"Generic","model":"X1"}]}
            """))
        {
            var sModelom = VyrobcaModelService.ZVysledkov("Latitude 5540", ukazka.RootElement);
            var bezModelu = VyrobcaModelService.ZVysledkov("Latitude", ukazka.RootElement);
            Over(sModelom is { Vyrobca: "Dell", Model: "Latitude 5540" } && bezModelu is { Vyrobca: "Dell", Model: null },
                $"výrobca a model z odpovede UPCitemdb ({sModelom?.Vyrobca}/{sModelom?.Model}; {bezModelu?.Vyrobca}/{bezModelu?.Model})");
        }

        // ------------------------------------------------------------------ karta: doplnenie pri písaní názvu
        var oneskorenie = PolozkaEditViewModel.OneskorenieHladaniaVyrobcu;
        PolozkaEditViewModel.OneskorenieHladaniaVyrobcu = TimeSpan.FromMilliseconds(50);
        try
        {
            var nova = new PolozkaEditViewModel(null, null);
            nova.Nazov = "Notebook Lenovo ThinkPad T14 Gen 4";
            CakajNa(nova.PosledneHladanieVyrobcu);
            Over(nova.Vyrobca == "Lenovo" && nova.Model == "ThinkPad T14 Gen 4", $"karta: výrobca a model po zadaní názvu ({nova.Vyrobca} / {nova.Model})");

            nova.Nazov = "Notebook Dell Latitude 3540";
            CakajNa(nova.PosledneHladanieVyrobcu);
            Over(nova.Vyrobca == "Dell" && nova.Model == "Latitude 3540", $"karta: zmena názvu zmení aj automaticky doplnené údaje ({nova.Vyrobca} / {nova.Model})");

            nova.Model = "Latitude 3540 i5";
            nova.Nazov = "Notebook HP ProBook 450 G9";
            CakajNa(nova.PosledneHladanieVyrobcu);
            Over(nova.Vyrobca == "HP" && nova.Model == "Latitude 3540 i5", $"karta: ručne zadaný model sa neprepíše ({nova.Vyrobca} / {nova.Model})");

            var upravovana = new PolozkaEditViewModel(new PolozkaService().NacitajRiadky()[0].Id, null);
            var povodny = upravovana.Vyrobca;
            upravovana.Nazov = "Router TP-Link Archer AX55";
            Pumpuj();
            Over(upravovana.PosledneHladanieVyrobcu is null && upravovana.Vyrobca == povodny, "karta existujúcej položky: výrobca sa sám nemení");

            // voľba v Evidencii: vypnuté hľadanie na internete sa zapamätá
            VyrobcaModelService.ZInternetuZapnute = false;
            var vypnute = Cakaj(VyrobcaModelService.ZInternetuAsync("Monitor Latitude 5540 test", null, CancellationToken.None));
            Over(!VyrobcaModelService.ZInternetuZapnute && vypnute.Navrh is null, "vypnuté hľadanie výrobcu na internete sa nepýta internetu");
            VyrobcaModelService.ZInternetuZapnute = true;

            // vyčerpaný denný limit: nová položka bez výrobcu sa zaradí na neskoršie doplnenie
            VyrobcaModelService.OznacVycerpanyLimit();
            var sLimitom = new PolozkaEditViewModel(null, null);
            sLimitom.Nazov = "Monitor Latitude 5540";
            CakajNa(sLimitom.PosledneHladanieVyrobcu);
            sLimitom.KategoriaId = sLimitom.Kategorie.First(k => k.Id > 0).Id;
            sLimitom.UlozitCommand.Execute(null);
            var cakajuca = sLimitom.UlozeneId.Single();
            Over(string.IsNullOrEmpty(sLimitom.Vyrobca) && VyrobcaModelService.Cakajuce().Contains(cakajuca),
                $"po vyčerpaní limitu čaká položka {cakajuca} na doplnenie výrobcu a modelu");
            Over(Cakaj(VyrobcaModelService.DoplnCakajuceAsync()) == 0 && VyrobcaModelService.Cakajuce().Contains(cakajuca),
                "v deň vyčerpaného limitu sa na doplnenie nečaká – zostane na ďalší deň");

            // ďalší deň (limit zrušený): doplnenie na pozadí – výsledok len do záznamu, služba nemusí byť dostupná
            VyrobcaModelService.ZrusLimit();
            var cas = System.Diagnostics.Stopwatch.StartNew();
            var doplnene = Cakaj(VyrobcaModelService.DoplnCakajuceAsync());
            var poDoplneni = new PolozkaService().Nacitaj(cakajuca)!;
            Krok($"doplnenie čakajúcich položiek: {doplnene}, položka {cakajuca}: {poDoplneni.Vyrobca ?? "nič"} / {poDoplneni.Model ?? "nič"}, "
                 + $"ešte čaká: {VyrobcaModelService.Cakajuce().Count} ({cas.ElapsedMilliseconds} ms)");
            Over(cas.Elapsed < VyrobcaModelService.LimitInternetu + TimeSpan.FromSeconds(5), "doplnenie čakajúcich položiek neprekročí časový limit");
            if (VyrobcaModelService.Cakajuce().Contains(cakajuca))
            {
                Krok("internet/UPCitemdb nedostupný – položka čaká ďalej (správne)");
            }

            // doplnená položka sa z frontu odstráni aj vtedy, keď výrobcu a model medzičasom zadal používateľ
            VyrobcaModelService.PridajCakajuce(new[] { cakajuca });
            var rucne = new PolozkaService().Nacitaj(cakajuca)!;
            rucne.Vyrobca = "Dell";
            rucne.Model = "Latitude 5540";
            new PolozkaService().Uloz(rucne);
            Cakaj(VyrobcaModelService.DoplnCakajuceAsync());
            Over(!VyrobcaModelService.Cakajuce().Contains(cakajuca), "položka s ručne doplneným výrobcom a modelom už nečaká");
            new PolozkaService().Vymaz(new[] { cakajuca });
        }
        finally
        {
            PolozkaEditViewModel.OneskorenieHladaniaVyrobcu = oneskorenie;
        }

        Krok("karta položky: doplnenie dátumov, výrobcu a modelu v poriadku");
    }

    private static void CakajNa(Task? uloha)
    {
        var cakanie = System.Diagnostics.Stopwatch.StartNew();
        while (uloha is not null && !uloha.IsCompleted && cakanie.Elapsed < TimeSpan.FromSeconds(15))
        {
            Pumpuj();
            Thread.Sleep(10);
        }

        Pumpuj();
    }
}
