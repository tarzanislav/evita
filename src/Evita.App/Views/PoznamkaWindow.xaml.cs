using System.Windows;
using Evita.Pomocne;
using Evita.Services;
using Evita.ViewModels;

namespace Evita.Views;

public partial class PoznamkaWindow : Window
{
    public PoznamkaWindow(PoznamkaEditViewModel model)
    {
        Model = model;
        DataContext = model;
        InitializeComponent();
        model.Hotovo += _ =>
        {
            try
            {
                DialogResult = true;
            }
            catch (InvalidOperationException)
            {
                Close();
            }
        };
        Loaded += (_, _) => Editor.Zameraj(model.JeNova);
        Closing += (_, e) =>
        {
            if (DialogResult != true && Model.Zmenena
                && Dialogy.Potvrd(Preklad.T("Poznámka má neuložené zmeny. Uložiť ich?")) && !Model.Uloz())
            {
                e.Cancel = true;
            }
        };
    }

    public PoznamkaEditViewModel Model { get; }

    /// <summary>Otvorí poznámku (alebo novú) a vráti true, ak sa niečo zmenilo.</summary>
    public static bool Otvor(PoznamkaEditViewModel model)
    {
        var okno = new PoznamkaWindow(model) { Owner = Dialogy.Vlastnik };
        var vysledok = okno.ShowDialog() == true;
        return vysledok || !model.JeNova;
    }
}
