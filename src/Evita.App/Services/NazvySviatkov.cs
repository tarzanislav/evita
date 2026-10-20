using Kalendarium;

namespace Evita.Services;

/// <summary>Názvy sviatkov a pamätných dní z Kalendária (po slovensky) – zobrazujú sa v jazyku programu.</summary>
public static class NazvySviatkov
{
    /// <summary>Texty na preklad (názvy sú v Kalendáriu uložené po slovensky).</summary>
    internal static readonly string[] Texty =
    {
        Preklad.N("štátny sviatok"),
        Preklad.N("sviatok"),
        Preklad.N("pamätný deň"),
        Preklad.N("Deň vzniku Slovenskej republiky"),
        Preklad.N("Sviatok svätého Cyrila a svätého Metoda"),
        Preklad.N("Výročie Slovenského národného povstania"),
        Preklad.N("Deň Ústavy Slovenskej republiky"),
        Preklad.N("Deň vzniku samostatného česko-slovenského štátu"),
        Preklad.N("Deň boja za slobodu a demokraciu"),
        Preklad.N("Zjavenie Pána (Traja králi)"),
        Preklad.N("Veľký piatok"),
        Preklad.N("Veľkonočný pondelok"),
        Preklad.N("Sviatok práce"),
        Preklad.N("Deň víťazstva nad fašizmom"),
        Preklad.N("Sedembolestná Panna Mária"),
        Preklad.N("Sviatok Všetkých svätých"),
        Preklad.N("Štedrý deň"),
        Preklad.N("Prvý sviatok vianočný"),
        Preklad.N("Druhý sviatok vianočný"),
        Preklad.N("Deň obetí pandémie COVID-19"),
        Preklad.N("Deň zápasu za ľudské práva"),
        Preklad.N("Deň nespravodlivo stíhaných"),
        Preklad.N("Deň pristúpenia Slovenskej republiky k Európskej únii"),
        Preklad.N("Výročie úmrtia M. R. Štefánika"),
        Preklad.N("Výročie Memoranda národa slovenského"),
        Preklad.N("Deň odchodu okupačných vojsk sovietskej armády z Česko-Slovenska v roku 1991"),
        Preklad.N("Deň pamiatky obetí komunistického režimu"),
        Preklad.N("Deň Slovákov žijúcich v zahraničí"),
        Preklad.N("Výročie Deklarácie o zvrchovanosti Slovenskej republiky"),
        Preklad.N("Deň Matice slovenskej"),
        Preklad.N("Deň obetí banských nešťastí"),
        Preklad.N("Deň obetí okupácie Česko-Slovenska v roku 1968"),
        Preklad.N("Deň obetí holokaustu a rasového násilia"),
        Preklad.N("Deň vzniku Slovenskej národnej rady"),
        Preklad.N("Deň boja proti nenávistným prejavom na deťoch"),
        Preklad.N("Deň hrdinov Karpatsko-duklianskej operácie"),
        Preklad.N("Deň samizdatu"),
        Preklad.N("Deň černovskej tragédie"),
        Preklad.N("Deň narodenia Ľudovíta Štúra"),
        Preklad.N("Výročie Deklarácie slovenského národa"),
        Preklad.N("Deň reformácie"),
        Preklad.N("Deň vyhlásenia Slovenska za samostatnú cirkevnú provinciu"),
    };

    /// <summary>Názov sviatku v jazyku programu.</summary>
    public static string Nazov(SviatokDna sviatok) => Preklad.TAkJe(sviatok.Nazov);

    /// <summary>Názov sviatku v jazyku programu.</summary>
    public static string Nazov(string nazov) => Preklad.TAkJe(nazov);

    /// <summary>Druh (štátny sviatok, sviatok, pamätný deň) v jazyku programu.</summary>
    public static string Druh(SviatokDna sviatok) => Preklad.TAkJe(sviatok.DruhText);
}
