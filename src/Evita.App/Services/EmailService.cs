using System.IO;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Evita.Data;
using Evita.Pomocne;

namespace Evita.Services;

/// <summary>Ako sa PDF posiela e-mailom.</summary>
public enum SposobOdoslania
{
    /// <summary>Otvorí sa nový e-mail v poštovom programe počítača (Outlook, Thunderbird …) s prílohou.</summary>
    PostovyProgram,

    /// <summary>EviTA odošle e-mail sama cez server odchádzajúcej pošty (SMTP) – súbor sa neukladá na disk.</summary>
    Smtp,
}

/// <summary>Šifrovanie spojenia so serverom SMTP.</summary>
public enum ZabezpecenieSmtp
{
    /// <summary>SSL/TLS od začiatku spojenia (zvyčajne port 465).</summary>
    SslTls,

    /// <summary>STARTTLS – spojenie sa zašifruje po pripojení (zvyčajne port 587).</summary>
    StartTls,

    /// <summary>Bez šifrovania – len pre server v miestnej sieti bez prihlasovania.</summary>
    Ziadne,
}

/// <summary>Nastavenie odosielania e-mailov. Heslo sa ukladá zvlášť, zašifrované pre používateľa Windows (DPAPI).</summary>
public sealed record EmailNastavenia
{
    public const string KlucNastavenia = "Email.Nastavenia";
    public const string KlucHeslo = "Email.Heslo";
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("EviTA.Email.v1");

    public SposobOdoslania Sposob { get; init; } = SposobOdoslania.PostovyProgram;
    public string Server { get; init; } = "";
    public int Port { get; init; } = 465;
    public ZabezpecenieSmtp Zabezpecenie { get; init; } = ZabezpecenieSmtp.SslTls;

    /// <summary>E-mailová adresa odosielateľa.</summary>
    public string Odosielatel { get; init; } = "";
    public string MenoOdosielatela { get; init; } = "";

    /// <summary>Prihlasovacie meno (zvyčajne e-mailová adresa); prázdne = bez prihlásenia.</summary>
    public string Pouzivatel { get; init; } = "";

    /// <summary>Skrytá kópia odoslaného e-mailu na adresu odosielateľa (aby ju mal vo svojej pošte).</summary>
    public bool KopiaSebe { get; init; } = true;

    /// <summary>Komu sa naposledy posielalo (predvyplní sa).</summary>
    public string PoslednyAdresat { get; init; } = "";

    [JsonIgnore] public bool SmtpNastavene => Server.Trim().Length > 0 && Port is > 0 and < 65536 && JeAdresa(Odosielatel);

    public static EmailNastavenia Nacitaj()
    {
        try
        {
            using var db = Databaza.Otvor();
            if (Databaza.Citaj(db, KlucNastavenia) is { Length: > 0 } json && JsonSerializer.Deserialize<EmailNastavenia>(json) is { } n)
            {
                return n;
            }
        }
        catch (Exception)
        {
            // predvolené
        }

        return new EmailNastavenia();
    }

    public void Uloz()
    {
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucNastavenia, JsonSerializer.Serialize(this));
        db.SaveChanges();
    }

    /// <summary>Uložené heslo (null, ak nie je alebo sa nedá rozšifrovať – napr. iný používateľ Windows).</summary>
    public static string? Heslo()
    {
        try
        {
            using var db = Databaza.Otvor();
            if (Databaza.Citaj(db, KlucHeslo) is not { Length: > 0 } ulozene)
            {
                return null;
            }

            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ulozene), Entropia, DataProtectionScope.CurrentUser));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Uloží heslo zašifrované pre aktuálneho používateľa Windows (null alebo prázdne = zmaže ho).</summary>
    public static void UlozHeslo(string? heslo)
    {
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, KlucHeslo, string.IsNullOrEmpty(heslo)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(heslo), Entropia, DataProtectionScope.CurrentUser)));
        db.SaveChanges();
    }

    public static bool JeAdresa(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains(' '))
        {
            return false;
        }

        try
        {
            return new MailAddress(text.Trim()).Address.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Adresy oddelené čiarkou, bodkočiarkou alebo medzerou; neplatné vráti v druhom zozname.</summary>
    public static (List<string> Platne, List<string> Neplatne) Adresy(string text)
    {
        var platne = new List<string>();
        var neplatne = new List<string>();
        foreach (var cast in text.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            (JeAdresa(cast) ? platne : neplatne).Add(cast.Trim());
        }

        return (platne.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), neplatne);
    }

    /// <summary>Známe služby: server, port a šifrovanie podľa adresy odosielateľa.</summary>
    public static (string Server, int Port, ZabezpecenieSmtp Zabezpecenie)? Sluzba(string adresa)
    {
        var domena = adresa.Contains('@') ? adresa[(adresa.LastIndexOf('@') + 1)..].Trim().ToLowerInvariant() : "";
        return domena switch
        {
            "gmail.com" or "googlemail.com" => ("smtp.gmail.com", 465, ZabezpecenieSmtp.SslTls),
            _ => null,
        };
    }
}

/// <summary>Odoslanie zostavy e-mailom – podľa nastavenia cez poštový program počítača alebo priamo cez SMTP.</summary>
public static class EmailService
{
    /// <summary>Predvolený text e-mailu so zostavou.</summary>
    public static string PredvolenyText(string nazov)
    {
        var organizacia = NastaveniaTlace.Nacitaj().Nazov;
        return Preklad.F($"Dobrý deň,\n\nv prílohe posielam: {nazov}.\n\nS pozdravom") + (string.IsNullOrWhiteSpace(organizacia) ? "" : "\n" + organizacia);
    }

    /// <summary>Pošle súbory e-mailom. Vráti true, ak sa e-mail odoslal (alebo otvoril v poštovom programe).</summary>
    public static async Task<bool> PosliAsync(IReadOnlyList<SuborVystupu> subory, string nazov, Window? vlastnik)
    {
        var nastavenia = EmailNastavenia.Nacitaj();
        if (nastavenia.Sposob == SposobOdoslania.Smtp)
        {
            if (!nastavenia.SmtpNastavene)
            {
                Dialogy.Upozornenie(Preklad.T("Odosielanie priamo z EviTA nie je nastavené. Doplňte server a adresu odosielateľa v nastavení e-mailu."));
                return false;
            }

            var okno = new Views.EmailWindow(subory, nazov) { Owner = vlastnik ?? Dialogy.Vlastnik };
            return okno.ShowDialog() == true;
        }

        var priecinok = PostovyProgram.Priprav(subory);
        var cesty = subory.Select(s => Path.Combine(priecinok, s.Nazov)).ToList();
        var vysledok = await PostovyProgram.OtvorAsync(cesty, nazov, PredvolenyText(nazov));
        if (vysledok is PostovyProgram.Vysledok.Otvorene or PostovyProgram.Vysledok.Zrusene)
        {
            return vysledok == PostovyProgram.Vysledok.Otvorene;
        }

        Dialogy.Info(Preklad.F($"V počítači nie je poštový program, ktorý by vedel prevziať prílohu (napr. nový Outlook alebo pošta v prehliadači to nevedia).\n\nSúbor je pripravený v priečinku, ktorý sa teraz otvorí – pretiahnite ho myšou do nového e-mailu.\n\nAlebo v nastavení e-mailu zvoľte odosielanie priamo z EviTA (SMTP)."));
        Tlac.OtvorPriecinok(priecinok, cesty[0]);
        return false;
    }
}
