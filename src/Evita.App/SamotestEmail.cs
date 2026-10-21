using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;

namespace Evita;

/// <summary>Samotest posielania e-mailom: skúšobný server SMTP na tomto počítači (bez šifrovania, SSL/TLS, STARTTLS), MIME, poštový program.</summary>
internal static partial class Samotest
{
    /// <summary>Čo prijal skúšobný server.</summary>
    private sealed class PrijatyEmail
    {
        public string Prihlasenie = "";
        public readonly List<string> Adresati = new();
        public string Data = "";
        public bool Sifrovane;
    }

    /// <summary>Jednoduchý server SMTP pre jedno spojenie (len na skúšku klienta EviTA).</summary>
    private static (int Port, Task<PrijatyEmail> Prijem) SkusobnySmtp(ZabezpecenieSmtp zabezpecenie, X509Certificate2 certifikat)
    {
        var poslucha = new TcpListener(IPAddress.Loopback, 0);
        poslucha.Start();
        var port = ((IPEndPoint)poslucha.LocalEndpoint).Port;
        var prijem = Task.Run(async () =>
        {
            var vysledok = new PrijatyEmail();
            using var klient = await poslucha.AcceptTcpClientAsync();
            poslucha.Stop();
            Stream prud = klient.GetStream();
            async Task Sifruj()
            {
                var ssl = new SslStream(prud, false);
                await ssl.AuthenticateAsServerAsync(certifikat);
                prud = ssl;
                vysledok.Sifrovane = true;
            }

            if (zabezpecenie == ZabezpecenieSmtp.SslTls)
            {
                await Sifruj();
            }

            async Task Posli(string text) => await prud.WriteAsync(Encoding.ASCII.GetBytes(text + "\r\n"));
            async Task<string> Citaj()
            {
                var bajty = new List<byte>();
                var b = new byte[1];
                while (await prud.ReadAsync(b) == 1 && b[0] != '\n')
                {
                    if (b[0] != '\r')
                    {
                        bajty.Add(b[0]);
                    }
                }

                return Encoding.ASCII.GetString(bajty.ToArray());
            }

            await Posli("220 skusobny server ESMTP");
            while (true)
            {
                var riadok = await Citaj();
                var prikaz = riadok.Split(' ')[0].ToUpperInvariant();
                switch (prikaz)
                {
                    case "EHLO":
                        await Posli(zabezpecenie == ZabezpecenieSmtp.StartTls && !vysledok.Sifrovane
                            ? "250-skusobny\r\n250-SIZE 10000000\r\n250 STARTTLS"
                            : "250-skusobny\r\n250-SIZE 10000000\r\n250 AUTH LOGIN PLAIN");
                        break;
                    case "STARTTLS":
                        await Posli("220 2.0.0 Ready to start TLS");
                        await Sifruj();
                        break;
                    case "AUTH":
                        vysledok.Prihlasenie = Encoding.UTF8.GetString(Convert.FromBase64String(riadok.Split(' ')[2])).Replace('\0', '|');
                        await Posli("235 2.7.0 Authentication successful");
                        break;
                    case "MAIL":
                        await Posli("250 OK");
                        break;
                    case "RCPT":
                        vysledok.Adresati.Add(riadok[(riadok.IndexOf('<') + 1)..riadok.IndexOf('>')]);
                        await Posli("250 OK");
                        break;
                    case "DATA":
                        await Posli("354 End data with <CR><LF>.<CR><LF>");
                        var data = new StringBuilder();
                        while (await Citaj() is var r && r != ".")
                        {
                            data.Append(r.StartsWith("..", StringComparison.Ordinal) ? r[1..] : r).Append("\r\n");
                        }

                        vysledok.Data = data.ToString();
                        await Posli("250 2.0.0 queued");
                        break;
                    case "QUIT":
                        await Posli("221 bye");
                        return vysledok;
                    default:
                        await Posli("500 unknown");
                        return vysledok;
                }
            }
        });
        return (port, prijem);
    }

    private static void SamotestEmail()
    {
        // certifikát skúšobného servera (len v pamäti tohto testu)
        using var rsa = RSA.Create(2048);
        var ziadost = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var docasny = ziadost.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        using var certifikat = X509CertificateLoader.LoadPkcs12(docasny.Export(X509ContentType.Pfx), null);
        SmtpKlient.OverenieCertifikatu = (_, _, _, _) => true;
        string? povodne;
        using (var db = Databaza.Otvor())
        {
            povodne = Databaza.Citaj(db, EmailNastavenia.KlucNastavenia);
        }

        try
        {
            // heslo zašifrované pre používateľa Windows
            EmailNastavenia.UlozHeslo("tajné heslo 123");
            string ulozene;
            using (var db = Databaza.Otvor())
            {
                ulozene = Databaza.Citaj(db, EmailNastavenia.KlucHeslo) ?? "";
            }

            Over(EmailNastavenia.Heslo() == "tajné heslo 123" && !ulozene.Contains("tajn") && ulozene.Length > 20, "e-mail: heslo sa ukladá zašifrované (DPAPI)");
            var (platne, neplatne) = EmailNastavenia.Adresy("jana@skola.sk; peter@skola.sk, zla-adresa  jana@skola.sk");
            Over(platne.Count == 2 && neplatne is ["zla-adresa"], "e-mail: kontrola adries (viac adresátov, neplatná adresa)");
            Over(SmtpKlient.Slovo("Súhrnný zoznam – miestnosti") is var slovo && slovo.StartsWith("=?UTF-8?B?", StringComparison.Ordinal)
                 && SmtpKlient.Slovo("Report") == "Report", "e-mail: predmet s diakritikou ako encoded-word");

            var subory = PdfVystup.Vytvor(new ZostavyService().Vytvor(new MoznostiZostavy
            {
                Druh = DruhZostavy.SuhrnnyZoznam,
                Stlpce = new[] { StlpcePrenosu.Cislo, StlpcePrenosu.Nazov, StlpcePrenosu.Miestnost },
            }), "Súhrnný zoznam",
                new VolbyPdf());
            foreach (var zabezpecenie in new[] { ZabezpecenieSmtp.Ziadne, ZabezpecenieSmtp.SslTls, ZabezpecenieSmtp.StartTls })
            {
                var (port, prijem) = SkusobnySmtp(zabezpecenie, certifikat);
                var nastavenia = new EmailNastavenia
                {
                    Sposob = SposobOdoslania.Smtp,
                    Server = "127.0.0.1",
                    Port = port,
                    Zabezpecenie = zabezpecenie,
                    Odosielatel = "evita@skola.sk",
                    MenoOdosielatela = "Základná škola Ukážková",
                    Pouzivatel = "evita@skola.sk",
                };
                var uloha = SmtpKlient.PosliAsync(nastavenia, "tajné heslo 123", new SpravaEmailu(nastavenia.Odosielatel, nastavenia.MenoOdosielatela,
                    new[] { "jana@skola.sk" }, new[] { "evita@skola.sk" }, "Súhrnný zoznam", "Dobrý deň,\n.bodka na začiatku\nS pozdravom", subory));
                CakajNa(uloha);
                var chyba = uloha.IsFaulted ? uloha.Exception!.GetBaseException() : uloha.IsCompleted ? null : new TimeoutException("bez odpovede");

                var prijate = prijem.Wait(TimeSpan.FromSeconds(20)) ? prijem.Result : new PrijatyEmail();
                var priloha = Priloha(prijate.Data);
                Over(chyba is null && prijate.Sifrovane == (zabezpecenie != ZabezpecenieSmtp.Ziadne)
                     && prijate.Prihlasenie == "|evita@skola.sk|tajné heslo 123"
                     && prijate.Adresati.SequenceEqual(new[] { "jana@skola.sk", "evita@skola.sk" })
                     && !prijate.Data.Contains("Bcc", StringComparison.OrdinalIgnoreCase)
                     && prijate.Data.Contains("filename*=UTF-8''S%C3%BAhrnn%C3%BD%20zoznam.pdf")
                     && priloha.SequenceEqual(subory[0].Data)
                     && Telo(prijate.Data).Contains("\r\n.bodka na začiatku\r\n"),
                    $"e-mail cez SMTP ({zabezpecenie}): prihlásenie, adresáti so skrytou kópiou, príloha PDF bez zmeny ({priloha.Length} B)"
                    + (chyba is null ? "" : " – " + chyba.Message));
            }

            // okno nastavenia: skúšobný e-mail cez SSL/TLS
            var (portOkna, prijemOkna) = SkusobnySmtp(ZabezpecenieSmtp.SslTls, certifikat);
            var oknoNastavenia = new EmailNastaveniaWindow();
            oknoNastavenia.Show();
            oknoNastavenia.Vypln(new EmailNastavenia
            {
                Sposob = SposobOdoslania.Smtp, Server = "127.0.0.1", Port = portOkna, Zabezpecenie = ZabezpecenieSmtp.SslTls,
                Odosielatel = "evita@skola.sk", MenoOdosielatela = "Základná škola Ukážková", Pouzivatel = "evita@skola.sk",
            });
            oknoNastavenia.NastavHeslo("tajné heslo 123");
            Pumpuj();
            var skusobny = Cakaj(oknoNastavenia.OdoslatSkusobnyAsync());
            Pumpuj();
            Snimka(oknoNastavenia, "okno_email_nastavenia");
            Over(skusobny && prijemOkna.Wait(TimeSpan.FromSeconds(10)) && prijemOkna.Result.Adresati is ["evita@skola.sk"],
                "nastavenie e-mailu: skúšobný e-mail odoslaný (" + oknoNastavenia.StavText + ")");
            oknoNastavenia.Nastavenia.Uloz();
            oknoNastavenia.Close();
            Pumpuj();

            // okno Poslať e-mailom (STARTTLS) – adresát, predmet, text, prílohy
            var (portEmailu, prijemEmailu) = SkusobnySmtp(ZabezpecenieSmtp.StartTls, certifikat);
            (EmailNastavenia.Nacitaj() with { Port = portEmailu, Zabezpecenie = ZabezpecenieSmtp.StartTls, KopiaSebe = true }).Uloz();
            var oknoEmailu = new EmailWindow(subory, "Súhrnný zoznam");
            oknoEmailu.Show();
            oknoEmailu.NastavKomu("jana@skola.sk; peter@skola.sk");
            Pumpuj();
            Snimka(oknoEmailu, "okno_email");
            var odoslane = Cakaj(oknoEmailu.OdoslatAsync());
            Over(odoslane && prijemEmailu.Wait(TimeSpan.FromSeconds(10))
                 && prijemEmailu.Result.Adresati.SequenceEqual(new[] { "jana@skola.sk", "peter@skola.sk", "evita@skola.sk" })
                 && EmailNastavenia.Nacitaj().PoslednyAdresat == "jana@skola.sk, peter@skola.sk",
                "okno Poslať e-mailom: odoslané dvom adresátom + skrytá kópia, adresáti sa zapamätajú (" + oknoEmailu.StavText + ")");
            oknoEmailu.Close();
            Pumpuj();

            // poštový program (v samoteste bez okna poštového programu)
            (EmailNastavenia.Nacitaj() with { Sposob = SposobOdoslania.PostovyProgram }).Uloz();
            var pocet = Dialogy.TicheHlasenia.Count;
            var otvorene = Cakaj(EmailService.PosliAsync(subory, "Súhrnný zoznam", null));
            Over(otvorene && Dialogy.TicheHlasenia.Skip(pocet).Any(h => h.StartsWith("[E-mail] Súhrnný zoznam: Súhrnný zoznam.pdf", StringComparison.Ordinal))
                 && Directory.GetFiles(PostovyProgram.Priecinok, "Súhrnný zoznam.pdf", SearchOption.AllDirectories).Length > 0,
                "e-mail cez poštový program: príloha pripravená v dočasnom priečinku");
        }
        finally
        {
            SmtpKlient.OverenieCertifikatu = null;
            EmailNastavenia.UlozHeslo(null);
            using var db = Databaza.Otvor();
            Databaza.Zapis(db, EmailNastavenia.KlucNastavenia, povodne);
            db.SaveChanges();
        }

        Krok("posielanie e-mailom v poriadku");
    }

    /// <summary>Telo e-mailu (text/plain) dekódované z base64.</summary>
    private static string Telo(string data)
    {
        var zaciatok = data.IndexOf("Content-Type: text/plain", StringComparison.Ordinal);
        if (zaciatok < 0)
        {
            return "";
        }

        var od = data.IndexOf("\r\n\r\n", zaciatok, StringComparison.Ordinal) + 4;
        var po = data.IndexOf("\r\n--", od, StringComparison.Ordinal);
        return Encoding.UTF8.GetString(Convert.FromBase64String(data[od..po].Replace("\r\n", "")));
    }

    /// <summary>Prvá príloha e-mailu dekódovaná z base64.</summary>
    private static byte[] Priloha(string data)
    {
        var zaciatok = data.IndexOf("Content-Disposition: attachment", StringComparison.Ordinal);
        if (zaciatok < 0)
        {
            return Array.Empty<byte>();
        }

        var od = data.IndexOf("\r\n\r\n", zaciatok, StringComparison.Ordinal) + 4;
        var po = data.IndexOf("\r\n--", od, StringComparison.Ordinal);
        return Convert.FromBase64String(data[od..po].Replace("\r\n", ""));
    }
}
