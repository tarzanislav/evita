using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;
using Microsoft.Win32;

namespace Evita.ViewModels;

public sealed partial class VolbaStlpca : ObservableObject
{
    public VolbaStlpca(StlpecPrenosu stlpec, bool vybrany)
    {
        Stlpec = stlpec;
        _vybrany = vybrany;
    }

    public StlpecPrenosu Stlpec { get; }
    public string Nazov => Stlpec.Nazov;

    [ObservableProperty] private bool _vybrany;
}

public sealed partial class VolbaFormatu : ObservableObject
{
    private readonly Action<VolbaFormatu> _priVybere;

    public VolbaFormatu(PopisFormatu popis, bool vybrany, Action<VolbaFormatu> priVybere)
    {
        Popis = popis;
        _vybrany = vybrany;
        _priVybere = priVybere;
    }

    public PopisFormatu Popis { get; }
    public string Nazov => Popis.Nazov;
    public string Text => Popis.Popis;

    [ObservableProperty] private bool _vybrany;

    partial void OnVybranyChanged(bool value)
    {
        if (value)
        {
            _priVybere(this);
        }
    }
}

/// <summary>Okno exportu: rozsah (celá evidencia, vyfiltrované, označené), formát a stĺpce.</summary>
public partial class ExportViewModel : ObservableObject
{
    private const string KlucFormat = "Export.Format";
    private const string KlucPriecinok = "Export.Priecinok";

    private readonly ExportService _sluzba = new();
    private readonly IReadOnlyList<int>? _vyfiltrovane;
    private readonly IReadOnlyList<int>? _oznacene;
    private readonly string? _popisFiltra;

    public ExportViewModel(IReadOnlyList<int>? vyfiltrovane, IReadOnlyList<int>? oznacene, string? popisFiltra)
    {
        _vyfiltrovane = vyfiltrovane;
        _oznacene = oznacene is { Count: > 0 } ? oznacene : null;
        _popisFiltra = popisFiltra;

        int celkom;
        FormatExportu ulozeny;
        using (var db = Databaza.Otvor())
        {
            celkom = db.Polozky.Count();
            ulozeny = Enum.TryParse<FormatExportu>(Databaza.Citaj(db, KlucFormat), out var f) ? f : FormatExportu.Excel;
        }

        TextCela = $"Celá evidencia ({ExportService.PocetPoloziek(celkom)})";
        TextVyfiltrovane = _vyfiltrovane is null ? "Vyfiltrované položky" : $"Vyfiltrované položky ({ExportService.PocetPoloziek(_vyfiltrovane.Count)})";
        TextOznacene = _oznacene is null ? "Označené položky" : $"Označené položky ({ExportService.PocetPoloziek(_oznacene.Count)})";

        // predvolený rozsah: označené (ak sú aspoň dve), inak vyfiltrované, inak celá evidencia
        if (_oznacene is { Count: > 1 })
        {
            _rozsahOznacene = true;
        }
        else if (_vyfiltrovane is not null && _vyfiltrovane.Count != celkom)
        {
            _rozsahVyfiltrovane = true;
        }
        else
        {
            _rozsahCela = true;
        }

        Formaty = ExportService.Formaty.Select(p => new VolbaFormatu(p, p.Format == ulozeny, PriVybereFormatu)).ToList();
        _format = ulozeny;

        var vybrane = _sluzba.NacitajStlpce();
        Stlpce = StlpcePrenosu.Vsetky.Select(s => new VolbaStlpca(s, vybrane.Contains(s.Kluc))).ToList();
    }

    public string TextCela { get; }
    public string TextVyfiltrovane { get; }
    public string TextOznacene { get; }
    public bool MaVyfiltrovane => _vyfiltrovane is not null;
    public bool MaOznacene => _oznacene is not null;

    [ObservableProperty] private bool _rozsahCela;
    [ObservableProperty] private bool _rozsahVyfiltrovane;
    [ObservableProperty] private bool _rozsahOznacene;

    public List<VolbaFormatu> Formaty { get; }
    public List<VolbaStlpca> Stlpce { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UkazStlpce), nameof(UkazHistoriu), nameof(UkazHarky), nameof(PoznamkaFormatu))]
    private FormatExportu _format;

    [ObservableProperty] private bool _sHistoriou;
    [ObservableProperty] private bool _harokPreKategoriu;
    [ObservableProperty] private bool _otvoritPoExporte = true;

    public bool UkazStlpce => ExportService.MaVolbuStlpcov(Format);
    public bool UkazHistoriu => !UkazStlpce;
    public bool UkazHarky => Format == FormatExportu.Excel;

    public string PoznamkaFormatu => UkazStlpce
        ? ""
        : "JSON a XML obsahujú vždy všetky údaje položiek vrátane identifikátora a číselníkov, aby sa dali naimportovať do inej EviTA.";

    /// <summary>Pre automatický test: cesta k súboru bez okna Uložiť ako.</summary>
    public string? CestaBezDialogu { get; set; }

    /// <summary>Cesta k poslednému vyexportovanému súboru.</summary>
    public string? Vysledok { get; private set; }

    public event Action<bool>? Zatvorit;

    private void PriVybereFormatu(VolbaFormatu volba) => Format = volba.Popis.Format;

    [RelayCommand]
    private void VsetkyStlpce()
    {
        foreach (var s in Stlpce)
        {
            s.Vybrany = true;
        }
    }

    [RelayCommand]
    private void PredvoleneStlpce()
    {
        foreach (var s in Stlpce)
        {
            s.Vybrany = s.Stlpec.PredvolenyExport;
        }
    }

    [RelayCommand]
    private void Exportovat()
    {
        var stlpce = Stlpce.Where(s => s.Vybrany).Select(s => s.Stlpec.Kluc).ToList();
        if (UkazStlpce && stlpce.Count == 0)
        {
            Dialogy.Info("Vyberte aspoň jeden stĺpec.");
            return;
        }

        IReadOnlyCollection<int>? id = RozsahOznacene ? _oznacene : RozsahVyfiltrovane ? _vyfiltrovane : null;
        if (id is { Count: 0 })
        {
            Dialogy.Info("V zvolenom rozsahu nie je žiadna položka.");
            return;
        }

        var popis = RozsahOznacene ? "označené položky"
            : RozsahVyfiltrovane ? (string.IsNullOrWhiteSpace(_popisFiltra) ? "vyfiltrované položky" : $"vyfiltrované položky ({_popisFiltra})")
            : "celá evidencia";

        var cesta = CestaBezDialogu ?? VyberSubor();
        if (cesta is null)
        {
            return;
        }

        try
        {
            var pocet = 0;
            Dialogy.Cakaj(() => pocet = _sluzba.Exportuj(new MoznostiExportu(Format, cesta, id, stlpce, SHistoriou, HarokPreKategoriu, popis)));
            Vysledok = cesta;
            _sluzba.UlozStlpce(Stlpce.Where(s => s.Vybrany).Select(s => s.Stlpec.Kluc));
            UlozNastavenie(KlucFormat, Format.ToString());
            UlozNastavenie(KlucPriecinok, Path.GetDirectoryName(cesta));

            if (OtvoritPoExporte)
            {
                Tlac.OtvorSubor(cesta);
            }
            else
            {
                Dialogy.Info($"Vyexportované: {ExportService.PocetPoloziek(pocet)}.\n\n{cesta}");
            }

            Zatvorit?.Invoke(true);
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba("Export sa nepodaril.", ex);
        }
    }

    private string? VyberSubor()
    {
        string? priecinok;
        using (var db = Databaza.Otvor())
        {
            priecinok = Databaza.Citaj(db, KlucPriecinok);
        }

        if (string.IsNullOrEmpty(priecinok) || !Directory.Exists(priecinok))
        {
            priecinok = Path.Combine(Path.GetDirectoryName(VzoryService.Priecinok) ?? "", "Exporty");
            try
            {
                Directory.CreateDirectory(priecinok);
            }
            catch (Exception)
            {
                priecinok = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
        }

        var rozsah = RozsahOznacene ? "oznacene" : RozsahVyfiltrovane ? "vyber" : "evidencia";
        var dialog = new SaveFileDialog
        {
            Title = "Exportovať evidenciu",
            Filter = ExportService.Filter(Format),
            FileName = $"EviTA_{rozsah}_{DateTime.Now:yyyy-MM-dd}{ExportService.Pripona(Format)}",
            InitialDirectory = priecinok,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static void UlozNastavenie(string kluc, string? hodnota)
    {
        using var db = Databaza.Otvor();
        Databaza.Zapis(db, kluc, hodnota);
        db.SaveChanges();
    }
}
