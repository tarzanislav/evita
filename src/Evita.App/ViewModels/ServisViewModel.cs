using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Data;
using Evita.Pomocne;
using Evita.Services;
using Evita.Views;

namespace Evita.ViewModels;

public partial class ServisViewModel : ObservableObject, IObnovitelny
{
    private readonly MainViewModel _hlavne;
    private readonly ServisService _sluzba = new();

    public ServisViewModel(MainViewModel hlavne)
    {
        _hlavne = hlavne;
    }

    [ObservableProperty] private string _cestaDatabazy = "";
    [ObservableProperty] private string _velkost = "";
    [ObservableProperty] private string _pocty = "";
    [ObservableProperty] private string _verziaSchemy = "";
    [ObservableProperty] private bool _ukazkoveData;
    [ObservableProperty] private string _poslednaZaloha = "";

    public string PriecinokZaloh => Databaza.PriecinokZaloh;

    public void Obnov()
    {
        var info = _sluzba.Info();
        CestaDatabazy = info.Cesta;
        Velkost = info.VelkostBajtov < 1024 * 1024
            ? $"{info.VelkostBajtov / 1024.0:0.0} kB"
            : $"{info.VelkostBajtov / 1024.0 / 1024.0:0.0} MB";
        Pocty = $"{info.Polozky} položiek (z toho {info.Vyradene} vyradených), {info.Kategorie} kategórií, " +
                $"{info.Miestnosti} miestností, {info.Osoby} osôb, {info.ZaznamyHistorie} záznamov v histórii zmien";
        VerziaSchemy = info.VerziaSchemy.ToString();
        UkazkoveData = info.UkazkoveData;

        var zalohy = Directory.Exists(PriecinokZaloh)
            ? new DirectoryInfo(PriecinokZaloh).GetFiles("EviTA_zaloha_*.zip").OrderByDescending(f => f.LastWriteTime).ToList()
            : new List<FileInfo>();
        PoslednaZaloha = zalohy.Count == 0
            ? "Zatiaľ žiadna záloha."
            : $"Posledná záloha: {Sk.DatumCas(zalohy[0].LastWriteTime)} ({zalohy[0].Name}), spolu záloh: {zalohy.Count}";
    }

    [RelayCommand]
    private void ZalohovatTeraz()
    {
        string cesta = "";
        Dialogy.Cakaj(() => cesta = _sluzba.Zalohuj());
        Obnov();
        Dialogy.Info($"Záloha je hotová:\n\n{cesta}");
    }

    [RelayCommand]
    private void OtvoritPriecinokZaloh()
    {
        Directory.CreateDirectory(PriecinokZaloh);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{PriecinokZaloh}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void NacitatUkazkove()
    {
        var info = _sluzba.Info();
        var otazka = info.Polozky > 0
            ? "Načítať ukážkové dáta? Súčasné položky, história, miestnosti a osoby sa najprv zálohujú a potom vymažú."
            : "Načítať ukážkové dáta (200 položiek, 30 osôb, 25 miestností)?";
        if (!Dialogy.Potvrd(otazka))
        {
            return;
        }

        string? zaloha = null;
        Dialogy.Cakaj(() => zaloha = _sluzba.NacitatUkazkoveData());
        _hlavne.DataZmenene();
        Obnov();
        Dialogy.Info(zaloha is null
            ? "Ukážkové dáta sú načítané."
            : $"Ukážkové dáta sú načítané. Pôvodné dáta sú zálohované tu:\n\n{zaloha}");
    }

    [RelayCommand]
    private void VymazatVsetko()
    {
        var okno = new VymazanieWindow { Owner = Dialogy.Vlastnik };
        if (okno.ShowDialog() != true)
        {
            return;
        }

        string zaloha = "";
        Dialogy.Cakaj(() => zaloha = _sluzba.VymazatData(okno.PonechatMiestnosti, okno.PonechatOsoby));
        _hlavne.DataZmenene();
        Obnov();
        Dialogy.Info($"Dáta sú vymazané a evidencia začína od prírastkového čísla 1.\n\nZáloha pred vymazaním:\n{zaloha}");
    }

    [RelayCommand]
    private void Kompaktovat()
    {
        Dialogy.Cakaj(() => _sluzba.Kompaktuj());
        Obnov();
        Dialogy.Info("Databáza je kompaktovaná.");
    }
}
