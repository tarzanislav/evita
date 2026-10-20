using System.IO;
using PdfSharp.Fonts;

namespace Evita.Services;

/// <summary>
/// Čínske písmo pre PDF. Písma Windows s čínskymi znakmi sú väčšinou kolekcie (.ttc), ktoré PDFsharp
/// nevie načítať cez systém – tu sa z kolekcie vyberie prvé písmo a odovzdá sa ako samostatný súbor TTF.
/// Ostatné písma rieši PDFsharp sám (návrat null).
/// </summary>
public sealed class PismaPdf : IFontResolver
{
    public const string NazovCinskeho = "EviTA CJK";

    private static readonly (string Normalne, string? Tucne)[] Subory =
    {
        ("msyh.ttc", "msyhbd.ttc"),
        ("Deng.ttf", "Dengb.ttf"),
        ("simhei.ttf", null),
        ("simsun.ttc", null),
    };

    private static readonly object Zamok = new();
    private static bool _zaregistrovane;
    private static (string Normalne, string? Tucne)? _najdene;
    private readonly Dictionary<string, byte[]> _data = new();

    /// <summary>Zaregistruje resolver (raz, pred prvým použitím PDFsharp).</summary>
    public static void Zaregistruj()
    {
        lock (Zamok)
        {
            if (_zaregistrovane)
            {
                return;
            }

            _zaregistrovane = true;
            try
            {
                GlobalFontSettings.FontResolver ??= new PismaPdf();
            }
            catch (Exception)
            {
                // resolver už nemožno zmeniť – čínske písmo sa vyberie zo systému
            }
        }
    }

    /// <summary>Súbory čínskeho písma, ak sú vo Windows.</summary>
    public static (string Normalne, string? Tucne)? NajdiSubory()
    {
        lock (Zamok)
        {
            if (_najdene is null)
            {
                var priecinok = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                foreach (var (normalne, tucne) in Subory)
                {
                    var cesta = Path.Combine(priecinok, normalne);
                    if (File.Exists(cesta))
                    {
                        var cestaTucne = tucne is null ? null : Path.Combine(priecinok, tucne);
                        _najdene = (cesta, cestaTucne is not null && File.Exists(cestaTucne) ? cestaTucne : null);
                        break;
                    }
                }

                _najdene ??= ("", null);
            }

            return _najdene.Value.Normalne.Length > 0 ? _najdene : null;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        if (!string.Equals(familyName, NazovCinskeho, StringComparison.OrdinalIgnoreCase) || NajdiSubory() is not { } subory)
        {
            return null;
        }

        var tucne = isBold && subory.Tucne is not null;
        return new FontResolverInfo(tucne ? "cjk-tucne" : "cjk", isBold && !tucne, isItalic);
    }

    public byte[]? GetFont(string faceName)
    {
        lock (_data)
        {
            if (_data.TryGetValue(faceName, out var data))
            {
                return data;
            }

            if (NajdiSubory() is not { } subory)
            {
                return null;
            }

            data = PrvePismoZKolekcie(File.ReadAllBytes(faceName == "cjk-tucne" ? subory.Tucne! : subory.Normalne));
            _data[faceName] = data;
            return data;
        }
    }

    /// <summary>Z kolekcie TrueType (ttcf) vytvorí samostatné písmo z prvého písma kolekcie; iný súbor vráti bez zmeny.</summary>
    public static byte[] PrvePismoZKolekcie(byte[] data)
    {
        if (data.Length < 16 || data[0] != 't' || data[1] != 't' || data[2] != 'c' || data[3] != 'f')
        {
            return data;
        }

        uint U32(int o) => (uint)(data[o] << 24 | data[o + 1] << 16 | data[o + 2] << 8 | data[o + 3]);
        int U16(int o) => data[o] << 8 | data[o + 1];

        var zaciatok = (int)U32(12);
        var pocetTabuliek = U16(zaciatok + 4);
        var tabulky = new List<(uint Znacka, uint Sucet, int Odkial, int Dlzka)>();
        for (var i = 0; i < pocetTabuliek; i++)
        {
            var z = zaciatok + 12 + 16 * i;
            tabulky.Add((U32(z), U32(z + 4), (int)U32(z + 8), (int)U32(z + 12)));
        }

        using var vystup = new MemoryStream();
        void Zapis32(uint h)
        {
            vystup.WriteByte((byte)(h >> 24));
            vystup.WriteByte((byte)(h >> 16));
            vystup.WriteByte((byte)(h >> 8));
            vystup.WriteByte((byte)h);
        }

        vystup.Write(data, zaciatok, 12);
        var pozicia = 12 + 16 * pocetTabuliek;
        foreach (var t in tabulky)
        {
            Zapis32(t.Znacka);
            Zapis32(t.Sucet);
            Zapis32((uint)pozicia);
            Zapis32((uint)t.Dlzka);
            pozicia += (t.Dlzka + 3) & ~3;
        }

        foreach (var t in tabulky)
        {
            vystup.Write(data, t.Odkial, t.Dlzka);
            for (var k = t.Dlzka; k % 4 != 0; k++)
            {
                vystup.WriteByte(0);
            }
        }

        return vystup.ToArray();
    }
}
