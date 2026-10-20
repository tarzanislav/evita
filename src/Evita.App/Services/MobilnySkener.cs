using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace Evita.Services;

/// <summary>
/// Mobil ako skener: EviTA spustí v miestnej sieti jednoduchú webovú stránku. Mobil ju otvorí cez QR kód
/// z obrazovky, odfotí čiarový alebo QR kód (alebo kód napíše) a počítač ho prečíta a spracuje ako kód z čítačky.
/// Prístup chráni náhodný kľúč v adrese – stránku otvorí len ten, kto naskenoval QR kód z obrazovky.
/// </summary>
public sealed class MobilnySkener : IDisposable
{
    private const int NajvacsiaFotka = 15 * 1024 * 1024;

    private readonly Dispatcher _rozhranie;
    private TcpListener? _server;
    private CancellationTokenSource? _zrusenie;

    public MobilnySkener(Dispatcher rozhranie)
    {
        _rozhranie = rozhranie;
        Kluc = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    }

    public string Kluc { get; }
    public int Port { get; private set; }
    public bool Bezi => _server is not null;

    /// <summary>Počet pripojení (stránka otvorená v mobile).</summary>
    public int Pripojenia { get; private set; }

    /// <summary>Nový kód z mobilu (už spracovaný, volá sa vo vlákne rozhrania).</summary>
    public event Action<string, VysledokSpracovania>? Prijate;

    /// <summary>Mobil otvoril stránku (volá sa vo vlákne rozhrania).</summary>
    public event Action? Pripojene;

    /// <summary>Adresy počítača v miestnej sieti (IPv4), najprv tie s bránou (Wi-Fi, ethernet).</summary>
    public static List<string> AdresyPocitaca()
    {
        var adresy = new List<(string Adresa, int Poradie)>();
        foreach (var rozhranie in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (rozhranie.OperationalStatus != OperationalStatus.Up
                || rozhranie.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var vlastnosti = rozhranie.GetIPProperties();
            var maBranu = vlastnosti.GatewayAddresses.Any(b => b.Address.AddressFamily == AddressFamily.InterNetwork && !b.Address.Equals(IPAddress.Any));
            var virtualne = rozhranie.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                            || rozhranie.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
                            || rozhranie.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase);
            foreach (var adresa in vlastnosti.UnicastAddresses)
            {
                if (adresa.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(adresa.Address)
                    || adresa.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue;
                }

                adresy.Add((adresa.Address.ToString(), (maBranu ? 0 : 2) + (virtualne ? 1 : 0)));
            }
        }

        return adresy.OrderBy(a => a.Poradie).Select(a => a.Adresa).Distinct().ToList();
    }

    public string Adresa(string ip) => $"http://{ip}:{Port}/?k={Kluc}";

    /// <summary>Spustí server na zvolenom porte (ak je obsadený, skúsi ďalšie).</summary>
    public void Spusti(int port)
    {
        Zastav();
        Exception? posledna = null;
        for (var p = port; p < port + 5; p++)
        {
            try
            {
                var server = new TcpListener(IPAddress.Any, p);
                server.Start();
                _server = server;
                Port = p;
                break;
            }
            catch (SocketException ex)
            {
                posledna = ex;
            }
        }

        if (_server is null)
        {
            throw new EvitaException(Preklad.F($"Port {port} je obsadený iným programom. Zmeňte port v nastaveniach skenovania."), posledna!);
        }

        _zrusenie = new CancellationTokenSource();
        _ = PrijimajAsync(_server, _zrusenie.Token);
    }

    public void Zastav()
    {
        _zrusenie?.Cancel();
        _zrusenie = null;
        try
        {
            _server?.Stop();
        }
        catch (Exception)
        {
            // server už nebeží
        }

        _server = null;
    }

    public void Dispose() => Zastav();

    private async Task PrijimajAsync(TcpListener server, CancellationToken zrusenie)
    {
        while (!zrusenie.IsCancellationRequested)
        {
            TcpClient klient;
            try
            {
                klient = await server.AcceptTcpClientAsync(zrusenie);
            }
            catch (Exception)
            {
                return;
            }

            _ = Task.Run(() => ObsluzAsync(klient, zrusenie), zrusenie);
        }
    }

    private async Task ObsluzAsync(TcpClient klient, CancellationToken zrusenie)
    {
        using var _ = klient;
        try
        {
            klient.ReceiveTimeout = 30000;
            klient.SendTimeout = 30000;
            var prud = klient.GetStream();
            var poziadavka = await CitajAsync(prud, zrusenie);
            if (poziadavka is null)
            {
                return;
            }

            var (metoda, cesta, telo, hlavicky) = poziadavka.Value;
            var adresa = new Uri("http://evita" + cesta);
            var parametre = System.Web.HttpUtility.ParseQueryString(adresa.Query);
            if (parametre["k"] != Kluc)
            {
                await OdpovedzAsync(prud, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(Preklad.T("Neplatná adresa. Naskenujte QR kód z obrazovky EviTA znova.")));
                return;
            }

            switch (metoda, adresa.AbsolutePath)
            {
                case ("GET", "/"):
                    await _rozhranie.InvokeAsync(() =>
                    {
                        Pripojenia++;
                        Pripojene?.Invoke();
                    });
                    await OdpovedzAsync(prud, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Stranka()));
                    break;

                case ("POST", "/kod"):
                {
                    var text = KodyEvita.Ocisti(Encoding.UTF8.GetString(telo));
                    var vysledok = text.Length == 0
                        ? new VysledokSpracovania(false, Preklad.T("Prázdny kód."))
                        : await DoplnZInternetuAsync(text, await SpracujAsync(text));
                    await OdpovedzJsonAsync(prud, text, vysledok);
                    break;
                }

                case ("POST", "/foto"):
                {
                    // na fotke býva viac kódov (EAN, sériové číslo, QR s odkazom) – spracuje sa ten najvhodnejší
                    var (citanie, chyba) = await PrecitajFotkuAsync(telo);
                    var kody = citanie?.Kody ?? new List<string>();
                    var text = kody.Count == 0 ? null : await Task.Run(() => new SkenovanieService().VyberKod(kody));
                    VysledokSpracovania vysledok;
                    if (text is not null)
                    {
                        vysledok = await DoplnZInternetuAsync(text, await SpracujAsync(text));
                    }
                    else
                    {
                        // neúspech: povedať, čo počítač dostal (rozmer, veľkosť), a fotku odložiť na rozbor
                        var popis = citanie?.Popis ?? $"{CiarovyKod.DruhObrazka(telo)}, {Math.Max(1, telo.Length / 1024)} kB";
                        var zMobilu = HodnotaHlavicky(hlavicky, "X-Evita-Foto");
                        UlozNecitatelnu(telo, popis, zMobilu, chyba);
                        vysledok = new VysledokSpracovania(false, chyba is not null
                            ? Preklad.F($"Fotku sa nepodarilo spracovať: {chyba}")
                            : citanie is not null && Math.Max(citanie.Sirka, citanie.Vyska) < 1000
                                ? Preklad.F($"Počítač dostal len malý náhľad fotky ({citanie.Sirka}×{citanie.Vyska} bodov), na ňom sa kód prečítať nedá. Otvorte adresu z QR kódu v prehliadači Chrome (nie v aplikácii Skener) a odfoťte kód znova.")
                                : Preklad.T("Na fotke sa nenašiel žiadny kód. Odfoťte ho zblízka, ostro a bez odleskov."));
                        vysledok = vysledok with { Text = vysledok.Text + " " + Preklad.F($"(Fotka: {popis}.)") };
                    }

                    if (kody.Count > 1)
                    {
                        vysledok = vysledok with { Text = vysledok.Text + " " + Preklad.F($"Na fotke: {string.Join(", ", kody)}.") };
                    }

                    await OdpovedzJsonAsync(prud, text, vysledok);
                    break;
                }

                default:
                    await OdpovedzAsync(prud, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("404"));
                    break;
            }
        }
        catch (Exception)
        {
            // spojenie s mobilom sa prerušilo
        }
    }

    private async Task<VysledokSpracovania> SpracujAsync(string text) =>
        await _rozhranie.InvokeAsync(() =>
        {
            var vysledok = SkenerCentrum.Odosli(Sken.Z(text, ZdrojSkenu.Mobil));
            Prijate?.Invoke(text, vysledok);
            return vysledok;
        });

    /// <summary>Neznámy EAN: do odpovede pre mobil sa pridá názov výrobku z internetu (ak sa nájde).</summary>
    private static async Task<VysledokSpracovania> DoplnZInternetuAsync(string text, VysledokSpracovania vysledok)
    {
        if (vysledok.Uspech || Sken.Z(text, ZdrojSkenu.Mobil).Varianty.Select(KodyEvita.NormalizujEan).FirstOrDefault(e => e is not null) is not { } ean)
        {
            return vysledok;
        }

        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var najdene = await new EanService().HladajAsync(ean, null, limit.Token).ConfigureAwait(false);
            if (najdene.Najdene.FirstOrDefault(n => n.Nazov is not null) is { } vyrobok)
            {
                return vysledok with { Text = vysledok.Text + " " + Preklad.F($"Na internete: {vyrobok.Nazov} ({vyrobok.Zdroj}). Pridajte ho v EviTA: Skenovanie → Nová položka.") };
            }
        }
        catch (Exception)
        {
            // bez internetu – stačí základná odpoveď
        }

        return vysledok;
    }

    /// <summary>Fotka sa číta vo vlastnom vlákne (dekódovanie obrázka potrebuje vlákno STA).</summary>
    /// <remarks>Chyba sa nezahodí – vráti sa jej popis, aby ju mobil ukázal.</remarks>
    private static Task<(CiarovyKod.CitanieFotky? Citanie, string? Chyba)> PrecitajFotkuAsync(byte[] fotka)
    {
        var hotovo = new TaskCompletionSource<(CiarovyKod.CitanieFotky?, string?)>();
        var vlakno = new Thread(() =>
        {
            try
            {
                hotovo.SetResult((CiarovyKod.PrecitajFotku(fotka), null));
            }
            catch (EvitaException ex)
            {
                hotovo.SetResult((null, ex.Message));
            }
            catch (Exception ex)
            {
                hotovo.SetResult((null, $"{ex.GetType().Name}: {ex.GetBaseException().Message}"));
            }
        })
        {
            IsBackground = true,
            Name = "EviTA – fotka z mobilu",
        };
        vlakno.SetApartmentState(ApartmentState.STA);
        vlakno.Start();
        return hotovo.Task;
    }

    /// <summary>Priečinok, kam sa odloží posledná fotka z mobilu, na ktorej sa kód nenašiel (na rozbor; vždy len jedna).</summary>
    public static string PriecinokDiagnostiky => Path.Combine(Evita.Data.Databaza.Priecinok, "Diagnostika");

    /// <summary>Posledná nečitateľná fotka sa uloží (prepíše predošlú) aj s popisom – dá sa poslať na rozbor chyby.</summary>
    private static void UlozNecitatelnu(byte[] fotka, string popis, string? zMobilu, string? chyba)
    {
        try
        {
            Directory.CreateDirectory(PriecinokDiagnostiky);
            var pripona = CiarovyKod.DruhObrazka(fotka) switch
            {
                "JPEG" => ".jpg",
                "PNG" => ".png",
                "HEIC" => ".heic",
                "WebP" => ".webp",
                _ => ".bin",
            };
            foreach (var stary in Directory.GetFiles(PriecinokDiagnostiky, "necitatelna_fotka.*"))
            {
                File.Delete(stary);
            }

            File.WriteAllBytes(Path.Combine(PriecinokDiagnostiky, "necitatelna_fotka" + pripona), fotka);
            File.WriteAllText(Path.Combine(PriecinokDiagnostiky, "necitatelna_fotka.txt"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nfotka: {popis}\r\nmobil: {zMobilu ?? "-"}\r\nchyba: {chyba ?? "-"}\r\n", Encoding.UTF8);
        }
        catch (Exception)
        {
            // odloženie fotky na rozbor nie je nevyhnutné
        }
    }

    private static string? HodnotaHlavicky(IReadOnlyDictionary<string, string> hlavicky, string nazov) =>
        hlavicky.TryGetValue(nazov, out var hodnota) ? hodnota : null;

    private static async Task<(string Metoda, string Cesta, byte[] Telo, IReadOnlyDictionary<string, string> Hlavicky)?> CitajAsync(Stream prud, CancellationToken zrusenie)
    {
        // hlavička požiadavky až po prázdny riadok
        var hlavicka = new MemoryStream();
        var bajt = new byte[1];
        var koniec = 0;
        while (koniec < 4)
        {
            if (await prud.ReadAsync(bajt, zrusenie) == 0 || hlavicka.Length > 32 * 1024)
            {
                return null;
            }

            hlavicka.WriteByte(bajt[0]);
            const byte cr = 13;
            const byte lf = 10;
            if (bajt[0] == cr && koniec is 0 or 2)
            {
                koniec++;
            }
            else if (bajt[0] == lf && koniec is 1 or 3)
            {
                koniec++;
            }
            else
            {
                koniec = bajt[0] == cr ? 1 : 0;
            }
        }

        var riadky = Encoding.ASCII.GetString(hlavicka.ToArray()).Split("\r\n");
        var prvy = riadky[0].Split(' ');
        if (prvy.Length < 2)
        {
            return null;
        }

        var dlzka = 0;
        var hlavicky = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var riadok in riadky.Skip(1))
        {
            var dvojbodka = riadok.IndexOf(':');
            if (dvojbodka <= 0)
            {
                continue;
            }

            var nazov = riadok[..dvojbodka].Trim();
            var hodnota = riadok[(dvojbodka + 1)..].Trim();
            if (nazov.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(hodnota, out dlzka);
            }
            else if (nazov.Equals("X-Evita-Foto", StringComparison.OrdinalIgnoreCase))
            {
                // len popis fotky z mobilu (rozmer, veľkosť) – iba tlačiteľné znaky, krátky
                hlavicky[nazov] = new string(hodnota.Where(z => z is >= ' ' and <= '~').Take(200).ToArray());
            }
        }

        if (dlzka < 0 || dlzka > NajvacsiaFotka)
        {
            return null;
        }

        var telo = new byte[dlzka];
        var precitane = 0;
        while (precitane < dlzka)
        {
            var n = await prud.ReadAsync(telo.AsMemory(precitane), zrusenie);
            if (n == 0)
            {
                return null;
            }

            precitane += n;
        }

        return (prvy[0].ToUpperInvariant(), prvy[1], telo, hlavicky);
    }

    private static async Task OdpovedzJsonAsync(Stream prud, string? kod, VysledokSpracovania vysledok)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new { ok = vysledok.Uspech, kod, text = vysledok.Text });
        await OdpovedzAsync(prud, 200, "application/json; charset=utf-8", json);
    }

    private static async Task OdpovedzAsync(Stream prud, int kod, string typ, byte[] telo)
    {
        var stav = kod switch
        {
            200 => "OK",
            403 => "Forbidden",
            _ => "Not Found",
        };
        var hlavicka = $"HTTP/1.1 {kod} {stav}\r\nContent-Type: {typ}\r\nContent-Length: {telo.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await prud.WriteAsync(Encoding.ASCII.GetBytes(hlavicka));
        await prud.WriteAsync(telo);
        await prud.FlushAsync();
    }

    private static string Html(string text) => WebUtility.HtmlEncode(text);

    private static string Js(string text) => JsonSerializer.Serialize(text);

    /// <summary>Stránka pre mobil: odfotiť kód alebo ho napísať, s odpoveďou počítača.</summary>
    private string Stranka()
    {
        var jazyk = Preklad.Aktualny.Kod;
        var titulok = Html(Preklad.T("skener"));
        var popis = Html(Preklad.T("Mobil ako skener – kódy sa spracujú v počítači."));
        var odfotit = Html(Preklad.T("Odfotiť kód"));
        var napiste = Html(Preklad.T("alebo napíšte kód"));
        var pripravene = Html(Preklad.T("Pripravené."));
        var neodpoveda = Js(Preklad.T("Počítač neodpovedá. Je EviTA otvorená a mobil v rovnakej sieti?"));
        var citam = Js(Preklad.T("Čítam kód…"));
        var neotvori = Js(Preklad.T("prehliadač fotku neotvorí, posiela sa originál"));
        var vnutorny = Html(Preklad.T("Stránka je otvorená vo vnútornom prehliadači aplikácie (napr. Skener), ktorý z fotoaparátu posiela len malý náhľad – kód sa z neho prečítať nedá."));
        var otvoritChrome = Html(Preklad.T("Otvoriť v prehliadači Chrome"));
        return $$"""
            <!doctype html>
            <html lang="{{jazyk}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>EviTA – {{titulok}}</title>
            <style>
              :root { color-scheme: light dark; --a: #0067c0; }
              body { font-family: system-ui, -apple-system, "Segoe UI", sans-serif; margin: 0; padding: 16px; max-width: 520px; margin-inline: auto; }
              h1 { font-size: 22px; margin: 4px 0 2px; }
              p.popis { margin: 0 0 16px; opacity: .7; font-size: 14px; }
              label.foto { display: flex; align-items: center; justify-content: center; gap: 10px; background: var(--a); color: #fff;
                           border-radius: 14px; padding: 22px; font-size: 20px; font-weight: 600; cursor: pointer; }
              label.foto input { display: none; }
              form { display: flex; gap: 8px; margin-top: 14px; }
              form input { flex: 1; font-size: 18px; padding: 10px 12px; border-radius: 10px; border: 1px solid #8888; min-width: 0; }
              form button { font-size: 18px; padding: 10px 16px; border-radius: 10px; border: 0; background: #8883; }
              #stav { margin: 16px 0 8px; padding: 14px; border-radius: 12px; background: #8881; font-size: 17px; min-height: 24px; }
              #stav.ok { background: #1a7f3730; } #stav.zle { background: #c4262630; }
              #vnutorny { margin: 14px 0 0; padding: 12px 14px; border-radius: 12px; background: #e0a10030; font-size: 15px; }
              #vnutorny a { display: block; margin-top: 8px; font-weight: 600; color: var(--a); }
              ul { list-style: none; padding: 0; margin: 0; font-size: 14px; }
              li { padding: 8px 2px; border-bottom: 1px solid #8883; } li b { display: block; }
            </style>
            </head>
            <body>
              <h1>EviTA</h1>
              <p class="popis">{{popis}}</p>
              <label class="foto">📷 {{odfotit}}<input id="foto" type="file" accept="image/*" capture="environment"></label>
              <form id="rucne"><input id="kod" placeholder="{{napiste}}" autocomplete="off" autocapitalize="characters"><button>OK</button></form>
              <div id="vnutorny" hidden>{{vnutorny}} <a id="chrome" href="#">{{otvoritChrome}}</a></div>
              <div id="stav">{{pripravene}}</div>
              <ul id="historia"></ul>
            <script>
            const kluc = new URLSearchParams(location.search).get('k');
            const stav = document.getElementById('stav');
            const historia = document.getElementById('historia');
            // vnútorný prehliadač aplikácie (napr. MIUI Skener) vracia z fotoaparátu často len malý náhľad – ponúknuť Chrome
            const android = /Android/i.test(navigator.userAgent);
            const vnutorny = android && (/; wv\)|MiuiBrowser|XiaoMi|MiuiScanner|FBAN|FBAV|Instagram/i.test(navigator.userAgent));
            document.getElementById('chrome').href = 'intent://' + location.host + location.pathname + location.search
              + '#Intent;scheme=http;package=com.android.chrome;end';
            function ukazChrome() { if (android) document.getElementById('vnutorny').hidden = false; }
            if (vnutorny) ukazChrome();
            function ukaz(o) {
              stav.className = o.ok ? 'ok' : 'zle';
              stav.textContent = o.text;
              const li = document.createElement('li');
              li.innerHTML = '<b></b><span></span>';
              li.firstChild.textContent = o.kod || '–';
              li.lastChild.textContent = o.text;
              historia.prepend(li);
              if (navigator.vibrate) navigator.vibrate(o.ok ? 60 : [80, 60, 80]);
            }
            async function posli(cesta, telo, typ, info) {
              try {
                const hlavicky = { 'Content-Type': typ };
                if (info) hlavicky['X-Evita-Foto'] = info;
                const r = await fetch(cesta + '?k=' + kluc, { method: 'POST', body: telo, headers: hlavicky });
                ukaz(await r.json());
              } catch (e) {
                ukaz({ ok: false, text: {{neodpoveda}} });
              }
            }
            // fotka sa zmenší najviac na 3200 bodov (rýchlejší prenos); popis fotky ide v hlavičke – pri neúspechu ho ukáže počítač
            function zmensi(subor) {
              return new Promise((hotovo, chyba) => {
                const obr = new Image();
                obr.onload = () => {
                  if (obr.width * obr.height < 1000000) ukazChrome();
                  const m = Math.min(1, 3200 / Math.max(obr.width, obr.height));
                  const c = document.createElement('canvas');
                  c.width = Math.round(obr.width * m); c.height = Math.round(obr.height * m);
                  c.getContext('2d').drawImage(obr, 0, 0, c.width, c.height);
                  URL.revokeObjectURL(obr.src);
                  c.toBlob(b => b ? hotovo({ telo: b, info: obr.width + 'x' + obr.height + ' -> ' + c.width + 'x' + c.height + ' ' + Math.round(b.size / 1024) + ' kB' })
                                  : chyba(), 'image/jpeg', 0.92);
                };
                obr.onerror = chyba;
                obr.src = URL.createObjectURL(subor);
              });
            }
            document.getElementById('foto').addEventListener('change', async e => {
              const subor = e.target.files[0];
              e.target.value = '';
              if (!subor) return;
              stav.className = ''; stav.textContent = {{citam}};
              const povodna = (subor.type || '?') + ' ' + Math.round(subor.size / 1024) + ' kB';
              let telo = subor, info = povodna + ', ' + {{neotvori}};
              try { const z = await zmensi(subor); telo = z.telo; info = povodna + ', ' + z.info; } catch (x) { }
              stav.textContent = {{citam}} + ' (' + info + ')';
              await posli('/foto', telo, telo.type || 'image/jpeg', info.replace(/[^ -~]/g, '?'));
            });
            document.getElementById('rucne').addEventListener('submit', async e => {
              e.preventDefault();
              const pole = document.getElementById('kod');
              const text = pole.value.trim();
              if (!text) return;
              pole.value = '';
              await posli('/kod', text, 'text/plain; charset=utf-8');
            });
            </script>
            </body>
            </html>
            """;
    }
}
