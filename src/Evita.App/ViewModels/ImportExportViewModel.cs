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

    public string PriecinokVzorov => VzoryService.Priecinok;

    [ObservableProperty] private string _pocetPoloziek = "";
    [ObservableProperty] private string _poslednyImport = "";
    [ObservableProperty] private bool _mozeVratit;

    public void Obnov()
    {
        try
        {
            using (var db = Databaza.Otvor())
            {
                PocetPoloziek = $"V evidencii je {ExportService.PocetPoloziek(db.Polozky.Count())}.";
            }

            var posledny = _import.PoslednyImport();
            MozeVratit = posledny is not null;
            PoslednyImport = posledny is { } p
                ? $"Posledný import: {p.Popis}. Dá sa vrátiť – evidencia sa obnoví zo zálohy urobenej tesne pred ním."
                : "Žiadny import, ktorý by sa dal vrátiť.";
        }
        catch (Exception ex)
        {
            PoslednyImport = $"Údaje sa nepodarilo načítať: {ex.GetBaseException().Message}";
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
    private void OtvoritVzory()
    {
        try
        {
            Dialogy.Cakaj(() => VzoryService.Vytvor());
            Tlac.OtvorPriecinok(VzoryService.Priecinok, System.IO.Path.Combine(VzoryService.Priecinok, VzoryService.Excel));
        }
        catch (Exception ex)
        {
            Dialogy.Chyba("Vzorové súbory sa nepodarilo vytvoriť.", ex);
        }
    }

    [RelayCommand]
    private void VratitImport()
    {
        var posledny = _import.PoslednyImport();
        if (posledny is null)
        {
            Dialogy.Info("Nie je žiadny import, ktorý by sa dal vrátiť.");
            return;
        }

        if (!Dialogy.Potvrd(
                $"Vrátiť posledný import?\n\n{posledny.Value.Popis}\n\nEvidencia sa obnoví zo zálohy urobenej tesne pred importom. " +
                "Zmeny urobené po importe sa tým tiež vrátia."))
        {
            return;
        }

        try
        {
            Dialogy.Cakaj(_import.VratPoslednyImport);
            _hlavne.DataZmenene();
            Obnov();
            Dialogy.Info("Import je vrátený. Evidencia je v stave pred importom.");
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
        }
        catch (Exception ex)
        {
            Dialogy.Chyba("Import sa nepodarilo vrátiť.", ex);
        }
    }
}
