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

    /// <summary>Čiarový kód výrobku (EAN-13, EAN-8, UPC) – podľa neho sa dajú doplniť údaje z internetu.</summary>
    public string? Ean { get; set; }

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

    /// <summary>Kľúč ikony (Services.IkonyKategorii); null = podľa názvu kategórie.</summary>
    public string? Ikona { get; set; }

    /// <summary>Vlastná farba ikony #RRGGBB; null = predvolená farba ikony.</summary>
    public string? Farba { get; set; }
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

public enum DruhUdalosti
{
    Udalost = 0,
    Uloha = 1,
}

public enum OpakovanieUdalosti
{
    Ziadne = 0,
    Denne = 1,
    Tyzdenne = 2,
    Mesacne = 3,
    Rocne = 4,
}

public enum PripomienkaUdalosti
{
    Ziadna = 0,
    VDen = 1,
    DenVopred = 2,
    TyzdenVopred = 3,
}

public enum StavUdalosti
{
    Naplanovane = 0,
    Hotovo = 1,
    Zrusene = 2,
}

/// <summary>Typ udalosti v kalendári (servis, revízia, nákup …) s farbou.</summary>
public class TypUdalosti
{
    public int Id { get; set; }
    public string Nazov { get; set; } = "";

    /// <summary>Farba v tvare #RRGGBB.</summary>
    public string Farba { get; set; } = "#7F8C8D";

    public bool Aktivny { get; set; } = true;
    public int Poradie { get; set; }
}

/// <summary>Udalosť alebo úloha v pracovnom kalendári, voliteľne naviazaná na položku, miestnosť alebo osobu.</summary>
public class Udalost
{
    public int Id { get; set; }
    public Guid Guid { get; set; } = Guid.NewGuid();
    public string Nazov { get; set; } = "";
    public string? Popis { get; set; }

    public int TypUdalostiId { get; set; }
    public TypUdalosti? TypUdalosti { get; set; }

    public DruhUdalosti Druh { get; set; }
    public DateTime Zaciatok { get; set; }
    public DateTime? Koniec { get; set; }
    public bool CelyDen { get; set; }
    public OpakovanieUdalosti Opakovanie { get; set; }
    public DateTime? OpakovanieDo { get; set; }
    public PripomienkaUdalosti Pripomienka { get; set; }

    public int? PolozkaId { get; set; }
    public Polozka? Polozka { get; set; }
    public int? MiestnostId { get; set; }
    public Miestnost? Miestnost { get; set; }
    public int? OsobaId { get; set; }
    public Osoba? Osoba { get; set; }

    public StavUdalosti Stav { get; set; }
    public DateTime Vytvorene { get; set; }
    public DateTime Zmenene { get; set; }
}

public enum PrioritaUlohy
{
    Nizka = 0,
    Normalna = 1,
    Vysoka = 2,
}

/// <summary>Úloha zo zoznamu úloh (To Do) – nezávislá od kalendára, voliteľne s termínom a naviazaním na položku.</summary>
public class Uloha
{
    public int Id { get; set; }
    public Guid Guid { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public string? Popis { get; set; }
    public DateTime? Termin { get; set; }
    public PrioritaUlohy Priorita { get; set; } = PrioritaUlohy.Normalna;
    public bool Hotovo { get; set; }
    public DateTime? DatumHotovo { get; set; }
    public DateTime Vytvorene { get; set; }
    public int Poradie { get; set; }

    public int? PolozkaId { get; set; }
    public Polozka? Polozka { get; set; }
}

/// <summary>Poznámka v poznámkovom bloku – voliteľne prepojená s položkou, dňom v kalendári alebo udalosťou.</summary>
public class Poznamka
{
    public int Id { get; set; }
    public Guid Guid { get; set; } = Guid.NewGuid();
    public string Nazov { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>Farba lístka v tvare #RRGGBB.</summary>
    public string Farba { get; set; } = "#FFF59D";

    public bool Pripnuta { get; set; }
    public DateTime Vytvorene { get; set; }
    public DateTime Zmenene { get; set; }

    /// <summary>Deň v kalendári, ku ktorému poznámka patrí.</summary>
    public DateTime? Datum { get; set; }

    public int? PolozkaId { get; set; }
    public Polozka? Polozka { get; set; }
    public int? UdalostId { get; set; }
    public Udalost? Udalost { get; set; }
}

/// <summary>Jeden záznam o zálohe alebo obnove (aj neúspešnej).</summary>
public class ZaznamZalohy
{
    public int Id { get; set; }
    public DateTime Cas { get; set; }
    /// <summary>ručná, automatická, pred aktualizáciou, pred obnovou, pred vymazaním dát, obnova</summary>
    public string Typ { get; set; } = "";
    public string? Cesta { get; set; }
    public long Velkost { get; set; }
    public bool Uspech { get; set; }
    public bool Zasifrovana { get; set; }
    public string? Chyba { get; set; }
}

public enum StavInventury
{
    Prebieha = 0,
    Ukoncena = 1,
}

/// <summary>Inventúra – súpis položiek skenovaním po miestnostiach.</summary>
public class Inventura
{
    public int Id { get; set; }
    public Guid Guid { get; set; } = Guid.NewGuid();
    public string Nazov { get; set; } = "";
    public DateTime Zaciatok { get; set; }
    public DateTime? Koniec { get; set; }
    public StavInventury Stav { get; set; } = StavInventury.Prebieha;
    public string? Poznamka { get; set; }

    /// <summary>Miestnosť, v ktorej sa práve skenuje (pokračovanie po prerušení).</summary>
    public int? AktualnaMiestnostId { get; set; }
}

/// <summary>Jedno naskenovanie (alebo ručné označenie) počas inventúry.</summary>
public class InventuraZaznam
{
    public int Id { get; set; }
    public int InventuraId { get; set; }
    public Inventura? Inventura { get; set; }

    /// <summary>Nájdená položka; null = naskenovaný kód, ktorý nie je v evidencii.</summary>
    public int? PolozkaId { get; set; }
    public Polozka? Polozka { get; set; }

    /// <summary>Naskenovaný text (alebo prírastkové číslo pri ručnom označení).</summary>
    public string Kod { get; set; } = "";

    /// <summary>Miestnosť, v ktorej bola položka nájdená.</summary>
    public int? MiestnostId { get; set; }

    /// <summary>Miestnosť, kde bola položka v evidencii v čase skenovania.</summary>
    public int? EvidovanaMiestnostId { get; set; }

    public DateTime Cas { get; set; }

    /// <summary>čítačka, kamera, mobil, ručne</summary>
    public string Sposob { get; set; } = "";

    public string Pouzivatel { get; set; } = "";
}
