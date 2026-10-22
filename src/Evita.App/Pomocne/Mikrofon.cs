using System.Runtime.InteropServices;
using Evita.Services;

namespace Evita.Pomocne;

/// <summary>
/// Nahrávanie z mikrofónu cez rozhranie Windows (winmm.dll, waveIn) – 16 kHz, mono, 16 bit, ako potrebuje prepis.
/// Zvuk zostáva len v pamäti počítača.
/// </summary>
public sealed class Mikrofon : IDisposable
{
    private const int WaveMapper = -1;
    private const int CallbackNull = 0;
    private const int WhdrDone = 1;
    private const int PocetBufferov = 8;
    private const int BajtovBuffera = 3200; // 100 ms

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort Format;
        public ushort Kanaly;
        public uint Frekvencia;
        public uint BajtovZaSekundu;
        public ushort Blok;
        public ushort Bity;
        public ushort Doplnok;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        public IntPtr Data;
        public uint Dlzka;
        public uint Nahrane;
        public IntPtr Pouzivatel;
        public uint Priznaky;
        public uint Slucky;
        public IntPtr Dalsi;
        public IntPtr Rezervovane;
    }

    [DllImport("winmm.dll")]
    private static extern int waveInGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out IntPtr zariadenie, int id, ref WaveFormatEx format, IntPtr spatne, IntPtr instancia, int priznaky);

    [DllImport("winmm.dll")]
    private static extern int waveInPrepareHeader(IntPtr zariadenie, IntPtr hlavicka, int velkost);

    [DllImport("winmm.dll")]
    private static extern int waveInUnprepareHeader(IntPtr zariadenie, IntPtr hlavicka, int velkost);

    [DllImport("winmm.dll")]
    private static extern int waveInAddBuffer(IntPtr zariadenie, IntPtr hlavicka, int velkost);

    [DllImport("winmm.dll")]
    private static extern int waveInStart(IntPtr zariadenie);

    [DllImport("winmm.dll")]
    private static extern int waveInReset(IntPtr zariadenie);

    [DllImport("winmm.dll")]
    private static extern int waveInClose(IntPtr zariadenie);

    private readonly List<short> _vzorky = new();
    private readonly IntPtr[] _hlavicky = new IntPtr[PocetBufferov];
    private IntPtr _zariadenie;
    private Thread? _vlakno;
    private volatile bool _bezi;

    /// <summary>Hlasitosť posledných 100 ms (0 – 1) – na ukazovateľ v okne.</summary>
    public event Action<double>? Uroven;

    public static bool MaZariadenie => waveInGetNumDevs() > 0;

    /// <summary>Dĺžka nahrávky v sekundách.</summary>
    public double Sekund
    {
        get
        {
            lock (_vzorky)
            {
                return _vzorky.Count / (double)PrepisHlasu.VzorkovaFrekvencia;
            }
        }
    }

    public void Start()
    {
        if (!MaZariadenie)
        {
            throw new EvitaException(Preklad.T("V počítači nie je mikrofón (alebo je vypnutý)."));
        }

        var format = new WaveFormatEx
        {
            Format = 1, Kanaly = 1, Frekvencia = (uint)PrepisHlasu.VzorkovaFrekvencia, BajtovZaSekundu = (uint)PrepisHlasu.VzorkovaFrekvencia * 2,
            Blok = 2, Bity = 16, Doplnok = 0,
        };
        var vysledok = waveInOpen(out _zariadenie, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, CallbackNull);
        if (vysledok != 0)
        {
            throw new EvitaException(Preklad.F($"Mikrofón sa nepodarilo otvoriť (kód {vysledok}). Skontrolujte v Nastaveniach Windows → Ochrana súkromia → Mikrofón, či majú klasické aplikácie povolený prístup."));
        }

        var velkost = Marshal.SizeOf<WaveHdr>();
        for (var i = 0; i < PocetBufferov; i++)
        {
            var hlavicka = new WaveHdr { Data = Marshal.AllocHGlobal(BajtovBuffera), Dlzka = BajtovBuffera };
            _hlavicky[i] = Marshal.AllocHGlobal(velkost);
            Marshal.StructureToPtr(hlavicka, _hlavicky[i], false);
            waveInPrepareHeader(_zariadenie, _hlavicky[i], velkost);
            waveInAddBuffer(_zariadenie, _hlavicky[i], velkost);
        }

        _bezi = true;
        waveInStart(_zariadenie);
        _vlakno = new Thread(Zbieraj) { IsBackground = true, Name = "EviTA mikrofón" };
        _vlakno.Start();
    }

    private void Zbieraj()
    {
        var velkost = Marshal.SizeOf<WaveHdr>();
        var buffer = new short[BajtovBuffera / 2];
        while (_bezi)
        {
            var nieco = false;
            foreach (var ptr in _hlavicky)
            {
                var h = Marshal.PtrToStructure<WaveHdr>(ptr);
                if ((h.Priznaky & WhdrDone) == 0)
                {
                    continue;
                }

                nieco = true;
                var pocet = (int)h.Nahrane / 2;
                Marshal.Copy(h.Data, buffer, 0, pocet);
                double energia = 0;
                lock (_vzorky)
                {
                    for (var i = 0; i < pocet; i++)
                    {
                        _vzorky.Add(buffer[i]);
                        energia += buffer[i] / 32768.0 * (buffer[i] / 32768.0);
                    }
                }

                Uroven?.Invoke(pocet > 0 ? Math.Min(1, Math.Sqrt(energia / pocet) * 4) : 0);
                if (_bezi)
                {
                    h.Priznaky &= ~(uint)WhdrDone;
                    h.Nahrane = 0;
                    Marshal.StructureToPtr(h, ptr, false);
                    waveInAddBuffer(_zariadenie, ptr, velkost);
                }
            }

            if (!nieco)
            {
                Thread.Sleep(20);
            }
        }
    }

    /// <summary>Ukončí nahrávanie a vráti zvuk (16 kHz mono, float).</summary>
    public float[] Stop()
    {
        if (!_bezi)
        {
            return Array.Empty<float>();
        }

        _bezi = false;
        _vlakno?.Join(500);
        Uvolni();
        lock (_vzorky)
        {
            return _vzorky.Select(v => v / 32768f).ToArray();
        }
    }

    private void Uvolni()
    {
        if (_zariadenie == IntPtr.Zero)
        {
            return;
        }

        waveInReset(_zariadenie);
        var velkost = Marshal.SizeOf<WaveHdr>();
        foreach (var ptr in _hlavicky.Where(p => p != IntPtr.Zero))
        {
            var h = Marshal.PtrToStructure<WaveHdr>(ptr);
            waveInUnprepareHeader(_zariadenie, ptr, velkost);
            Marshal.FreeHGlobal(h.Data);
            Marshal.FreeHGlobal(ptr);
        }

        Array.Clear(_hlavicky);
        waveInClose(_zariadenie);
        _zariadenie = IntPtr.Zero;
    }

    public void Dispose()
    {
        _bezi = false;
        _vlakno?.Join(500);
        Uvolni();
    }
}
