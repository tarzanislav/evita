namespace Evita.Models;

/// <summary>Stav položky v evidencii.</summary>
public enum StavPolozky
{
    VPrevadzke = 0,
    VSklade = 1,
    VOprave = 2,
    Vyradene = 3,
}

/// <summary>Jeden databázový záznam evidencie (zariadenie, príslušenstvo…).</summary>
public class Polozka
{
    public int Id { get; set; }
    public Guid Guid { get; set; } = Guid.NewGuid();
    public int PrirastkoveCislo { get; set; }
    public string Nazov { get; set; } = "";

    public int KategoriaId { get; set; }
    public Kategoria? Kategoria { get; set; }

    public int? MiestnostId { get; set; }
    public Miestnost? Miestnost { get; set; }

    public int? OsobaId { get; set; }
    public Osoba? Osoba { get; set; }

    public string? Vyrobca { get; set; }
    public string? Model { get; set; }
    public string? SerioveCislo { get; set; }
    public string? InventarneCislo { get; set; }
    public StavPolozky Stav { get; set; } = StavPolozky.VPrevadzke;
    public DateTime? DatumNakupu { get; set; }
    public decimal? Cena { get; set; }
    public DateTime? ZarukaDo { get; set; }
    public DateTime? DalsiaRevizia { get; set; }
    public DateTime DatumPridania { get; set; }
    public DateTime DatumZmeny { get; set; }
    public DateTime? DatumVyradenia { get; set; }
    public string? DovodVyradenia { get; set; }
    public string? Poznamka { get; set; }

    /// <summary>Číslo verzie riadku – chráni pred súčasnou úpravou z dvoch miest.</summary>
    public int Verzia { get; set; } = 1;
}

public class Kategoria
{
    public int Id { get; set; }
    public string Nazov { get; set; } = "";
    public bool Aktivna { get; set; } = true;
}

public class Miestnost
{
    public int Id { get; set; }
    public string Nazov { get; set; } = "";
    public string? Budova { get; set; }
    public string? Poznamka { get; set; }
    public bool Aktivna { get; set; } = true;
}

public class Osoba
{
    public int Id { get; set; }
    public string Meno { get; set; } = "";
    public string Priezvisko { get; set; } = "";
    public string? Funkcia { get; set; }
    public bool Aktivna { get; set; } = true;

    public string CeleMeno => $"{Meno} {Priezvisko}".Trim();
}

/// <summary>Jeden riadok histórie zmien. Zostáva zachovaný aj po vymazaní položky.</summary>
public class HistoriaZmeny
{
    public int Id { get; set; }
    public int PolozkaId { get; set; }
    public int PrirastkoveCislo { get; set; }
    public string NazovPolozky { get; set; } = "";
    public DateTime Cas { get; set; }
    public string Pouzivatel { get; set; } = "";
    /// <summary>vytvorenie, zmena, vyradenie, obnovenie, vymazanie, import</summary>
    public string Akcia { get; set; } = "";
    public string? Pole { get; set; }
    public string? StaraHodnota { get; set; }
    public string? NovaHodnota { get; set; }
}

/// <summary>Jednoduché nastavenia uložené v databáze (kľúč – hodnota).</summary>
public class Nastavenie
{
    public string Kluc { get; set; } = "";
    public string? Hodnota { get; set; }
}
