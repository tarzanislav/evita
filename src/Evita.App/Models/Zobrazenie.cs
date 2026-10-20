namespace Evita.Models;

/// <summary>Položka výberového zoznamu (ComboBox). Id 0 = nič / všetky, -1 = nezadané.</summary>
public sealed record Volba(int Id, string Nazov);

public sealed record VolbaStavu(StavPolozky Stav, string Nazov);

public static class StavyPoloziek
{
    public static string Nazov(StavPolozky stav) => stav switch
    {
        StavPolozky.VPrevadzke => "v prevádzke",
        StavPolozky.VSklade => "v sklade",
        StavPolozky.VOprave => "v oprave",
        StavPolozky.Vyradene => "vyradené",
        _ => stav.ToString(),
    };

    public static IReadOnlyList<VolbaStavu> Vsetky { get; } =
        Enum.GetValues<StavPolozky>().Select(s => new VolbaStavu(s, Nazov(s))).ToList();
}

/// <summary>Jeden riadok v tabuľke evidencie (zjednodušený pohľad na položku).</summary>
public sealed class PolozkaRiadok
{
    public int Id { get; init; }
    public int PrirastkoveCislo { get; init; }
    public string Nazov { get; init; } = "";
    public int KategoriaId { get; init; }
    public string Kategoria { get; init; } = "";
    public int? MiestnostId { get; init; }
    public string? Miestnost { get; init; }
    public int? OsobaId { get; init; }
    public string? Osoba { get; init; }
    public string? Vyrobca { get; init; }
    public string? Model { get; init; }
    public string? SerioveCislo { get; init; }
    public string? InventarneCislo { get; init; }
    public StavPolozky StavHodnota { get; init; }
    public DateTime? ZarukaDo { get; init; }
    public DateTime? DalsiaRevizia { get; init; }
    public DateTime DatumPridania { get; init; }
    public DateTime DatumZmeny { get; init; }
    public string? Poznamka { get; init; }

    public string Stav => StavyPoloziek.Nazov(StavHodnota);
    public bool Vyradena => StavHodnota == StavPolozky.Vyradene;

    /// <summary>Text bez diakritiky a malými písmenami – na rýchle vyhľadávanie.</summary>
    public string Hladanie { get; set; } = "";
}

/// <summary>Riadok číselníka (kategória, miestnosť, osoba) s počtom použití.</summary>
public sealed class CiselnikRiadok
{
    public int Id { get; init; }
    public string Nazov { get; init; } = "";
    public string? Detail { get; init; }
    public bool Aktivna { get; init; }
    public int Pocet { get; init; }
    public string Pole1 { get; init; } = "";
    public string? Pole2 { get; init; }
    public string? Pole3 { get; init; }

    public string AktivnaText => Aktivna ? "áno" : "nie";
}

public enum TypCiselnika
{
    Kategorie,
    Miestnosti,
    Osoby,
}
