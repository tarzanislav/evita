using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using Evita.Pomocne;
using Evita.Services;
using Evita.ViewModels;
using Evita.Views;

namespace Evita;

/// <summary>
/// Samotest diktovania poznámok: hlasové písanie Windows (Win+H), prevod WAV, prepis po slovensky bez internetu
/// (EvitaHlas.dll s modelom zo zostavenia a skúšobnou slovenskou nahrávkou, ak ich CI pripravilo).
/// </summary>
internal static partial class Samotest
{
    private static void SamotestDiktovania()
    {
        var povodnySposob = Diktovanie.Sposob;
        var povodnyModel = Diktovanie.Model;
        PoznamkaWindow? okno = null;
        try
        {
            // prevzorkovanie (napr. 22,05 kHz z hlasového syntetizátora) na 16 kHz pre prepis
            var ton = Enumerable.Range(0, 22050).Select(i => (float)Math.Sin(i * 2 * Math.PI * 440 / 22050)).ToArray();
            Over(Math.Abs(PrepisHlasu.Prevzorkuj(ton, 22050).Length - 16000) <= 1 && PrepisHlasu.Uprav("  Dobrý   deň [BLANK_AUDIO] ") == "Dobrý deň",
                "diktovanie: prevzorkovanie na 16 kHz a úprava textu");

            var nastavenie = new DiktovanieWindow();
            nastavenie.Show();
            Pumpuj();
            Snimka(nastavenie, "okno_diktovanie");
            nastavenie.Close();

            // hlasové písanie Windows: pole dostane fokus a stlačí sa Win+H
            Diktovanie.Sposob = SposobDiktovania.Windows;
            var model = new PoznamkaEditViewModel(null);
            okno = new PoznamkaWindow(model);
            okno.Show();
            Pumpuj();
            var pocet = Dialogy.TicheHlasenia.Count;
            okno.Editor.Diktovat.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pumpuj();
            Over(Dialogy.TicheHlasenia.Skip(pocet).Contains("[Hlasové písanie] Win+H") && okno.Editor.StavDiktovaniaText.Length > 0,
                "diktovanie: hlasové písanie Windows (Win+H) do poznámky – " + okno.Editor.StavDiktovaniaText);
            Snimka(okno, "okno_poznamka_diktovanie");

            // prepis v počítači (whisper.cpp) – ak zostavenie pripravilo model a slovenskú nahrávku
            var nahravka = Environment.GetEnvironmentVariable("EVITA_SAMOTEST_HLAS");
            var subor = Environment.GetEnvironmentVariable("EVITA_SAMOTEST_HLAS_MODEL");
            Over(PrepisHlasu.Prekazka() is null || !File.Exists(Path.Combine(AppContext.BaseDirectory, "EvitaHlas.dll")),
                "diktovanie: EvitaHlas.dll je pri programe a procesor podporuje AVX2" + (PrepisHlasu.Prekazka() is { } p ? " – " + p : ""));
            if (nahravka is null || subor is null || !File.Exists(nahravka) || !File.Exists(subor) || PrepisHlasu.Prekazka() is not null)
            {
                Krok("diktovanie bez internetu: preskočené (chýba model, nahrávka alebo EvitaHlas.dll)");
                return;
            }

            var modelHlasu = Diktovanie.Modely.First(m => m.Kluc == "small");
            Directory.CreateDirectory(Diktovanie.PriecinokModelov);
            File.Copy(subor, modelHlasu.Cesta, true);
            Over(Cakaj(Diktovanie.OverAsync(modelHlasu.Cesta, modelHlasu.Sha256)), "diktovanie: model overený podľa SHA-256");
            Diktovanie.Sposob = SposobDiktovania.Offline;
            Diktovanie.Model = modelHlasu;
            var zvuk = PrepisHlasu.NacitajWav(nahravka);
            var cas = System.Diagnostics.Stopwatch.StartNew();
            var uloha = okno.Editor.PrepisAVlozAsync(zvuk);
            while (!uloha.IsCompleted && cas.Elapsed < TimeSpan.FromMinutes(4))
            {
                Pumpuj();
                Thread.Sleep(50);
            }

            var text = model.Text;
            Krok($"diktovanie bez internetu ({zvuk.Length / 16000.0:0.0} s zvuku, {cas.Elapsed.TotalSeconds:0} s): „{text}“ – {okno.Editor.StavDiktovaniaText}");
            var slova = Sk.BezDiakritiky(text);
            var najdene = new[] { "notebook", "ucebn", "klavesnic", "tlaciare", "kabinet", "vymenit" }.Count(slova.Contains);
            Over(najdene >= 3, $"diktovanie bez internetu: slovenský text rozpoznaný ({najdene} zo 6 kľúčových slov)");
            Snimka(okno, "okno_poznamka_prepis");
        }
        finally
        {
            if (okno is not null)
            {
                okno.Model.Zmenena = false;
                okno.Close();
                Pumpuj();
            }

            Diktovanie.Sposob = povodnySposob;
            Diktovanie.Model = povodnyModel;
            PrepisHlasu.Zavri();
        }

        Krok("diktovanie v poriadku");
    }
}
