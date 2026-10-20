using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;

namespace Evita.ViewModels;

/// <summary>Obrazovka Import a export.</summary>
public partial class ImportExportViewModel : ObservableObject, IObnovitelny
{
    private readonly MainViewModel _hlavne;
    private readonly ImportService _import = new();

    public ImportExportViewModel(MainViewModel hlavne)
    {
        _hlavne = hlavne;
    }

    public IReadOnlyList<VzorovySubor> Vzory => VzoryService.Zoznam;

    public string PriecinokStiahnutych => VzoryService.PriecinokStiahnutych;

    /// <summary>Kam sa uložil naposledy stiahnutý vzor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StiahnutyText))]
    private string _stiahnutyVzor = "";

    /// <summary>Kam sa uložil vzorový súbor (alebo kam sa ukladajú).</summary>
    public string StiahnutyText => StiahnutyVzor.Length > 0
        ? Preklad.F($"Uložené: {StiahnutyVzor}")
        : Preklad.F($"Súbory sa ukladajú do priečinka {PriecinokStiahnutych}");

    [ObservableProperty] private string _pocetPoloziek = "";
    [ObservableProperty] private string _poslednyImport = "";
    [ObservableProperty] private bool _mozeVratit;

    public void Obnov()
    {
        try
        {
            using (var db = Databaza.Otvor())
            {
                PocetPoloziek = Preklad.F($"V evidencii je {ExportService.PocetPoloziek(db.Polozky.Count())}.");
            }

            var posledny = _import.PoslednyImport();
            MozeVratit = posledny is not null;
            PoslednyImport = posledny is { } p
                ? Preklad.F($"Posledný import: {p.Popis}. Dá sa vrátiť – evidencia sa obnoví zo zálohy urobenej tesne pred ním.")
                : Preklad.T("Žiadny import, ktorý by sa dal vrátiť.");
        }
        catch (Exception ex)
        {
            PoslednyImport = Preklad.F($"Údaje sa nepodarilo načítať: {ex.GetBaseException().Message}");
        }
    }

    [RelayCommand]
    private void Exportovat()
    {
        var okno = new ExportWindow(new ExportViewModel(null, null, null)) { Owner = Dialogy.Vlastnik };
        okno.ShowDialog();
    }

    [RelayCommand]
    public void Importovat()
    {
        var model = new ImportViewModel();
        var okno = new ImportWindow(model) { Owner = Dialogy.Vlastnik };
        okno.ShowDialog();
        if (model.ZmenyVDatach)
        {
            _hlavne.DataZmenene();
        }

        Obnov();
    }

    [RelayCommand]
    private void StiahnutVzor(VzorovySubor? vzor)
    {
        if (vzor is null)
        {
            return;
        }

        try
        {
            string? cesta = null;
            Dialogy.Cakaj(() => cesta = VzoryService.Stiahni(vzor));
            StiahnutyVzor = cesta!;
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Vzorový súbor sa nepodarilo uložiť do priečinka Stiahnuté."), ex);
        }
    }

    [RelayCommand]
    private void ZobrazitStiahnuty()
    {
        if (StiahnutyVzor.Length > 0)
        {
            Tlac.OtvorPriecinok(System.IO.Path.GetDirectoryName(StiahnutyVzor)!, StiahnutyVzor);
        }
        else
        {
            Tlac.OtvorPriecinok(VzoryService.PriecinokStiahnutych);
        }
    }

    [RelayCommand]
    private void VratitImport()
    {
        var posledny = _import.PoslednyImport();
        if (posledny is null)
        {
            Dialogy.Info(Preklad.T("Nie je žiadny import, ktorý by sa dal vrátiť."));
            return;
        }

        if (!Dialogy.Potvrd(
                Preklad.F($"Vrátiť posledný import?\n\n{posledny.Value.Popis}\n\nEvidencia sa obnoví zo zálohy urobenej tesne pred importom. Zmeny urobené po importe sa tým tiež vrátia.")))
        {
            return;
        }

        try
        {
            Dialogy.Cakaj(_import.VratPoslednyImport);
            _hlavne.DataZmenene();
            Obnov();
            Dialogy.Info(Preklad.T("Import je vrátený. Evidencia je v stave pred importom."));
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba(Preklad.T("Import sa nepodarilo vrátiť."), ex);
        }
    }
}
