using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Evita.Pomocne;

namespace Evita.Services;

/// <summary>E-mail na odoslanie.</summary>
public sealed record SpravaEmailu(
    string Odosielatel,
    string MenoOdosielatela,
    IReadOnlyList<string> Komu,
    IReadOnlyList<string> SkrytaKopia,
    string Predmet,
    string Text,
    IReadOnlyList<SuborVystupu> Prilohy);

/// <summary>
/// Odoslanie e-mailu cez server odchádzajúcej pošty (SMTP) – SSL/TLS (port 465) alebo STARTTLS (port 587), prihlásenie
/// AUTH PLAIN alebo LOGIN. Vlastná jednoduchá implementácia bez ďalších knižníc; príloha ide priamo z pamäte.
/// </summary>
public static class SmtpKlient
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(40);

    /// <summary>Len pre samotest: overenie certifikátu skúšobného servera (inak sa overuje normálne cez Windows).</summary>
    internal static RemoteCertificateValidationCallback? OverenieCertifikatu { get; set; }

    public static async Task PosliAsync(EmailNastavenia nastavenia, string? heslo, SpravaEmailu sprava, CancellationToken zrusenie = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(zrusenie);
        limit.CancelAfter(Limit);
        var server = nastavenia.Server.Trim();
        using var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(server, nastavenia.Port, limit.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            throw new EvitaException(Preklad.F($"Nepodarilo sa pripojiť k serveru {server}, port {nastavenia.Port}. Skontrolujte pripojenie k internetu, názov servera a port.\n\n{ex.Message}"));
        }

        Stream prud = tcp.GetStream();
        try
        {
            if (nastavenia.Zabezpecenie == ZabezpecenieSmtp.SslTls)
            {
                prud = await Sifruj(prud, server, limit.Token);
            }

            var spojenie = new Spojenie(prud, limit.Token);
            await spojenie.Odpoved(220);
            var schopnosti = await spojenie.Ehlo();
            if (nastavenia.Zabezpecenie == ZabezpecenieSmtp.StartTls)
            {
                if (!schopnosti.Any(s => s.StartsWith("STARTTLS", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new EvitaException(Preklad.T("Server nepodporuje šifrovanie STARTTLS. Skúste zabezpečenie SSL/TLS (port 465)."));
                }

                await spojenie.Prikaz("STARTTLS", 220);
                prud = await Sifruj(prud, server, limit.Token);
                spojenie = new Spojenie(prud, limit.Token);
                schopnosti = await spojenie.Ehlo();
            }

            if (nastavenia.Pouzivatel.Trim().Length > 0)
            {
                await Prihlas(spojenie, schopnosti, nastavenia, heslo ?? "", JeMiestny(tcp));
            }

            await spojenie.Prikaz($"MAIL FROM:<{sprava.Odosielatel}>", 250);
            foreach (var adresat in sprava.Komu.Concat(sprava.SkrytaKopia).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await spojenie.Prikaz($"RCPT TO:<{adresat}>", 250, 251);
            }

            await spojenie.Prikaz("DATA", 354);
            await spojenie.ZapisData(Mime(sprava));
            await spojenie.Odpoved(250);
            try
            {
                await spojenie.Prikaz("QUIT", 221);
            }
            catch (Exception)
            {
                // e-mail je už prevzatý
            }
        }
        catch (ChybaSmtp ex)
        {
            throw new EvitaException(Preklad.F($"Server {server} e-mail neprijal.\n\n{ex.Message}"));
        }
        catch (AuthenticationException ex)
        {
            throw new EvitaException(Preklad.F($"Šifrované spojenie so serverom {server} sa nepodarilo nadviazať (certifikát alebo nastavenie zabezpečenia).\n\n{ex.GetBaseException().Message}"));
        }
        catch (OperationCanceledException) when (!zrusenie.IsCancellationRequested)
        {
            throw new EvitaException(Preklad.F($"Server {server} neodpovedá. Skontrolujte port a zabezpečenie (465 = SSL/TLS, 587 = STARTTLS)."));
        }
        catch (IOException ex)
        {
            throw new EvitaException(Preklad.F($"Spojenie so serverom {server} sa prerušilo. Skontrolujte port a zabezpečenie (465 = SSL/TLS, 587 = STARTTLS).\n\n{ex.Message}"));
        }
        finally
        {
            await prud.DisposeAsync();
        }
    }

    private static bool JeMiestny(TcpClient tcp) => tcp.Client.RemoteEndPoint is IPEndPoint { Address: var adresa } && IPAddress.IsLoopback(adresa);

    private static async Task<Stream> Sifruj(Stream prud, string server, CancellationToken zrusenie)
    {
        var ssl = new SslStream(prud, false, OverenieCertifikatu);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = server }, zrusenie);
        return ssl;
    }

    private static async Task Prihlas(Spojenie spojenie, IReadOnlyList<string> schopnosti, EmailNastavenia nastavenia, string heslo, bool miestny)
    {
        if (nastavenia.Zabezpecenie == ZabezpecenieSmtp.Ziadne && !miestny)
        {
            throw new EvitaException(Preklad.T("Heslo sa nesmie posielať nezašifrovane. Zvoľte zabezpečenie SSL/TLS alebo STARTTLS."));
        }

        var auth = schopnosti.FirstOrDefault(s => s.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase))?.ToUpperInvariant() ?? "";
        var pouzivatel = nastavenia.Pouzivatel.Trim();
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        try
        {
            if (auth.Contains("PLAIN"))
            {
                await spojenie.Prikaz("AUTH PLAIN " + B64($"\0{pouzivatel}\0{heslo}"), 235);
            }
            else if (auth.Contains("LOGIN"))
            {
                await spojenie.Prikaz("AUTH LOGIN", 334);
                await spojenie.Prikaz(B64(pouzivatel), 334);
                await spojenie.Prikaz(B64(heslo), 235);
            }
            else
            {
                throw new EvitaException(Preklad.T("Server neponúka prihlásenie menom a heslom (AUTH PLAIN ani LOGIN)."));
            }
        }
        catch (ChybaSmtp ex) when (ex.Kod is 535 or 534 or 530 or 454)
        {
            var server = nastavenia.Server.ToLowerInvariant();
            var rada = server.Contains("gmail") || server.Contains("google")
                ? Preklad.T("Gmail vyžaduje dvojstupňové overenie a heslo aplikácie (Účet Google → Zabezpečenie → Heslá aplikácií) – bežné heslo nefunguje.")
                : server.Contains("office365") || server.Contains("outlook")
                    ? Preklad.T("Microsoft 365 povoľuje prihlásenie heslom (SMTP AUTH) len vtedy, keď ho správca zapne pre schránku.")
                    : Preklad.T("Skontrolujte prihlasovacie meno a heslo.");
            throw new EvitaException(Preklad.F($"Server odmietol prihlásenie.\n\n{rada}\n\n{ex.Message}"));
        }
    }

    // ------------------------------------------------------------------ MIME

    /// <summary>Správa vo formáte MIME (len znaky ASCII: hlavičky ako encoded-word, text a prílohy v base64).</summary>
    public static string Mime(SpravaEmailu sprava)
    {
        var hranica = "----=_EviTA_" + Guid.NewGuid().ToString("N");
        var domena = sprava.Odosielatel.Contains('@') ? sprava.Odosielatel[(sprava.Odosielatel.LastIndexOf('@') + 1)..] : "evita";
        var m = new StringBuilder();
        m.Append("From: ").Append(sprava.MenoOdosielatela.Trim().Length > 0 ? Slovo(sprava.MenoOdosielatela.Trim()) + " " : "")
            .Append('<').Append(sprava.Odosielatel).Append(">\r\n");
        m.Append("To: ").Append(string.Join(",\r\n ", sprava.Komu)).Append("\r\n");
        m.Append("Subject: ").Append(Slovo(sprava.Predmet)).Append("\r\n");
        m.Append("Date: ").Append(Datum(DateTimeOffset.Now)).Append("\r\n");
        m.Append("Message-ID: <").Append(Guid.NewGuid().ToString("N")).Append('@').Append(domena).Append(">\r\n");
        m.Append("MIME-Version: 1.0\r\n");
        m.Append("X-Mailer: EviTA ").Append(AktualizaciaService.AktualnaVerzia).Append("\r\n");
        m.Append("Content-Type: multipart/mixed;\r\n boundary=\"").Append(hranica).Append("\"\r\n\r\n");
        m.Append("This is a multi-part message in MIME format.\r\n\r\n");

        m.Append("--").Append(hranica).Append("\r\n");
        m.Append("Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
        Base64(m, Encoding.UTF8.GetBytes(sprava.Text.Replace("\r\n", "\n").Replace("\n", "\r\n")));
        foreach (var priloha in sprava.Prilohy)
        {
            m.Append("--").Append(hranica).Append("\r\n");
            m.Append("Content-Type: ").Append(priloha.Mime).Append(";\r\n name=\"").Append(Slovo(priloha.Nazov)).Append("\"\r\n");
            m.Append("Content-Transfer-Encoding: base64\r\n");
            m.Append("Content-Disposition: attachment;\r\n filename*=UTF-8''").Append(Uri.EscapeDataString(priloha.Nazov)).Append(";\r\n filename=\"")
                .Append(Slovo(priloha.Nazov)).Append("\"\r\n\r\n");
            Base64(m, priloha.Data);
        }

        m.Append("--").Append(hranica).Append("--\r\n");
        return m.ToString();
    }

    private static string Datum(DateTimeOffset cas)
    {
        var posun = cas.Offset;
        return cas.ToString("ddd, dd MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture)
               + (posun < TimeSpan.Zero ? "-" : "+") + posun.ToString("hhmm", CultureInfo.InvariantCulture);
    }

    /// <summary>Text hlavičky: čisté ASCII bez úprav, inak =?UTF-8?B?…?= po kúskoch (najviac 75 znakov na slovo).</summary>
    public static string Slovo(string text)
    {
        if (text.All(c => c is >= ' ' and <= '~' and not '"' and not '=' and not '?'))
        {
            return text;
        }

        var slova = new List<string>();
        var kusok = new StringBuilder();
        foreach (var prvok in EnumerujZnaky(text))
        {
            if (Encoding.UTF8.GetByteCount(kusok + prvok) > 45)
            {
                slova.Add("=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(kusok.ToString())) + "?=");
                kusok.Clear();
            }

            kusok.Append(prvok);
        }

        if (kusok.Length > 0)
        {
            slova.Add("=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(kusok.ToString())) + "?=");
        }

        return string.Join("\r\n ", slova);
    }

    private static IEnumerable<string> EnumerujZnaky(string text)
    {
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            yield return e.GetTextElement();
        }
    }

    private static void Base64(StringBuilder m, byte[] data)
    {
        var text = Convert.ToBase64String(data);
        for (var i = 0; i < text.Length; i += 76)
        {
            m.Append(text, i, Math.Min(76, text.Length - i)).Append("\r\n");
        }
    }

    // ------------------------------------------------------------------ spojenie

    private sealed class ChybaSmtp(int kod, string sprava) : Exception(sprava)
    {
        public int Kod { get; } = kod;
    }

    private sealed class Spojenie(Stream prud, CancellationToken zrusenie)
    {
        public async Task<List<string>> Ehlo()
        {
            var riadky = await Prikaz("EHLO " + Pocitac(), 250);
            return riadky.Skip(1).ToList();
        }

        private static string Pocitac()
        {
            var meno = new string(Environment.MachineName.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
            return meno.Length > 0 ? meno : "evita";
        }

        public async Task<List<string>> Prikaz(string prikaz, params int[] ocakavane)
        {
            await Zapis(prikaz + "\r\n");
            return await Odpoved(ocakavane);
        }

        public async Task ZapisData(string data)
        {
            // riadky začínajúce bodkou sa zdvoja (RFC 5321), koniec = samotná bodka
            var text = new StringBuilder(data.Length + 16);
            foreach (var riadok in data.Split("\r\n"))
            {
                text.Append(riadok.StartsWith('.') ? "." + riadok : riadok).Append("\r\n");
            }

            if (data.EndsWith("\r\n", StringComparison.Ordinal))
            {
                text.Length -= 2;
            }

            text.Append(".\r\n");
            await Zapis(text.ToString());
        }

        private async Task Zapis(string text)
        {
            await prud.WriteAsync(Encoding.ASCII.GetBytes(text), zrusenie);
            await prud.FlushAsync(zrusenie);
        }

        public async Task<List<string>> Odpoved(params int[] ocakavane)
        {
            var riadky = new List<string>();
            int kod;
            while (true)
            {
                var riadok = await Riadok();
                if (riadok.Length < 3 || !int.TryParse(riadok.AsSpan(0, 3), out kod))
                {
                    throw new IOException("Neplatná odpoveď servera: " + riadok);
                }

                riadky.Add(riadok.Length > 4 ? riadok[4..] : "");
                if (riadok.Length == 3 || riadok[3] != '-')
                {
                    break;
                }
            }

            if (!ocakavane.Contains(kod))
            {
                throw new ChybaSmtp(kod, $"{kod} {string.Join(" ", riadky)}");
            }

            return riadky;
        }

        private async Task<string> Riadok()
        {
            var bajty = new List<byte>();
            var jeden = new byte[1];
            while (true)
            {
                if (await prud.ReadAsync(jeden, zrusenie) == 0)
                {
                    throw new IOException("Server ukončil spojenie.");
                }

                if (jeden[0] == '\n')
                {
                    break;
                }

                if (jeden[0] != '\r')
                {
                    bajty.Add(jeden[0]);
                }

                if (bajty.Count > 4000)
                {
                    throw new IOException("Príliš dlhá odpoveď servera.");
                }
            }

            return Encoding.UTF8.GetString(bajty.ToArray());
        }
    }
}
