using System.IO;
using System.Runtime.InteropServices;
using Evita.Services;

namespace Evita.Pomocne;

/// <summary>
/// Nový e-mail s prílohou v poštovom programe počítača cez rozhranie Simple MAPI systému Windows (mapi32.dll) –
/// funguje s klasickým Outlookom, Thunderbirdom, eM Clientom a pod. Nový Outlook a pošta v prehliadači ho nepodporujú.
/// Prílohy sa pripravia v dočasnom priečinku, ktorý sa po dni sám vymaže.
/// </summary>
public static class PostovyProgram
{
    public enum Vysledok
    {
        Otvorene,
        Zrusene,
        Nedostupne,
    }

    private const int MapiLogonUi = 0x1;
    private const int MapiDialog = 0x8;
    private const int MapiUserAbort = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MapiMessageW
    {
        public int Reserved;
        public string? Subject;
        public string? NoteText;
        public string? MessageType;
        public string? DateReceived;
        public string? ConversationId;
        public int Flags;
        public IntPtr Originator;
        public int RecipCount;
        public IntPtr Recips;
        public int FileCount;
        public IntPtr Files;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MapiFileDescW
    {
        public int Reserved;
        public int Flags;
        public int Position;
        public string? PathName;
        public string? FileName;
        public IntPtr FileType;
    }

    [DllImport("mapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MAPISendMailW(IntPtr session, IntPtr okno, ref MapiMessageW sprava, int volby, int rezervovane);

    /// <summary>Priečinok s dočasnými prílohami.</summary>
    public static string Priecinok => Path.Combine(Path.GetTempPath(), "EviTA", "E-mail");

    /// <summary>Zapíše prílohy do nového dočasného priečinka a vráti ho (staršie ako deň sa zmažú).</summary>
    public static string Priprav(IReadOnlyList<SuborVystupu> subory)
    {
        try
        {
            if (Directory.Exists(Priecinok))
            {
                foreach (var stary in Directory.GetDirectories(Priecinok).Where(d => Directory.GetCreationTime(d) < DateTime.Now.AddDays(-1)))
                {
                    Directory.Delete(stary, true);
                }
            }
        }
        catch (Exception)
        {
            // upratanie nie je dôležité
        }

        var priecinok = Path.Combine(Priecinok, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(priecinok);
        foreach (var subor in subory)
        {
            File.WriteAllBytes(Path.Combine(priecinok, subor.Nazov), subor.Data);
        }

        return priecinok;
    }

    /// <summary>Otvorí nový e-mail s prílohami (vo vlákne STA, okno EviTA sa medzitým nezablokuje).</summary>
    public static Task<Vysledok> OtvorAsync(IReadOnlyList<string> prilohy, string predmet, string text)
    {
        if (Dialogy.TichyRezim)
        {
            Dialogy.TicheHlasenia.Add($"[E-mail] {predmet}: {string.Join(", ", prilohy.Select(Path.GetFileName))}");
            return Task.FromResult(Vysledok.Otvorene);
        }

        var hotovo = new TaskCompletionSource<Vysledok>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vlakno = new Thread(() =>
        {
            try
            {
                hotovo.SetResult(Posli(prilohy, predmet, text));
            }
            catch (Exception)
            {
                hotovo.SetResult(Vysledok.Nedostupne);
            }
        })
        {
            IsBackground = true,
            Name = "EviTA e-mail",
        };
        vlakno.SetApartmentState(ApartmentState.STA);
        vlakno.Start();
        return hotovo.Task;
    }

    private static Vysledok Posli(IReadOnlyList<string> prilohy, string predmet, string text)
    {
        var velkost = Marshal.SizeOf<MapiFileDescW>();
        var pole = Marshal.AllocHGlobal(velkost * prilohy.Count);
        try
        {
            for (var i = 0; i < prilohy.Count; i++)
            {
                var priloha = new MapiFileDescW { Position = -1, PathName = prilohy[i], FileName = Path.GetFileName(prilohy[i]) };
                Marshal.StructureToPtr(priloha, pole + i * velkost, false);
            }

            var sprava = new MapiMessageW { Subject = predmet, NoteText = text, FileCount = prilohy.Count, Files = pole };
            var kod = MAPISendMailW(IntPtr.Zero, IntPtr.Zero, ref sprava, MapiLogonUi | MapiDialog, 0);
            return kod switch
            {
                0 => Vysledok.Otvorene,
                MapiUserAbort => Vysledok.Zrusene,
                _ => Vysledok.Nedostupne,
            };
        }
        finally
        {
            for (var i = 0; i < prilohy.Count; i++)
            {
                Marshal.DestroyStructure<MapiFileDescW>(pole + i * velkost);
            }

            Marshal.FreeHGlobal(pole);
        }
    }
}
