using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Models;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.ViewModels;

/// <summary>Naraz zmení miestnosť, pridelenie alebo stav viacerým položkám.</summary>
public partial class HromadnaZmenaViewModel : ObservableObject
{
    private readonly List<int> _idPoloziek;

    public HromadnaZmenaViewModel(List<int> idPoloziek)
    {
        _idPoloziek = idPoloziek;
        var ciselniky = new CiselnikService();
        Miestnosti = ciselniky.Volby(TypCiselnika.Miestnosti, null, "(nezadaná)");
        Osoby = ciselniky.Volby(TypCiselnika.Osoby, null, "(nikto)");
        Popis = $"Zmena sa použije na {idPoloziek.Count} označených položiek. Každá zmena sa zapíše do histórie.";
    }

    public string Popis { get; }
    public List<Volba> Miestnosti { get; }
    public List<Volba> Osoby { get; }
    public IReadOnlyList<VolbaStavu> Stavy => StavyPoloziek.Vsetky;

    [ObservableProperty] private bool _zmenitMiestnost;
    [ObservableProperty] private int _miestnostId;
    [ObservableProperty] private bool _zmenitOsobu;
    [ObservableProperty] private int _osobaId;
    [ObservableProperty] private bool _zmenitStav;
    [ObservableProperty] private StavPolozky _stav = StavPolozky.VPrevadzke;

    public event Action<bool>? Zatvorit;

    partial void OnMiestnostIdChanged(int value) => ZmenitMiestnost = true;
    partial void OnOsobaIdChanged(int value) => ZmenitOsobu = true;
    partial void OnStavChanged(StavPolozky value) => ZmenitStav = true;

    [RelayCommand]
    private void Ulozit()
    {
        if (!ZmenitMiestnost && !ZmenitOsobu && !ZmenitStav)
        {
            Dialogy.Upozornenie("Zaškrtnite, čo chcete zmeniť.");
            return;
        }

        var miestnost = MiestnostId > 0 ? MiestnostId : (int?)null;
        var osoba = OsobaId > 0 ? OsobaId : (int?)null;
        var zmenene = 0;

        Dialogy.Cakaj(() =>
        {
            zmenene = new PolozkaService().HromadnaZmena(_idPoloziek, p =>
            {
                if (ZmenitMiestnost)
                {
                    p.MiestnostId = miestnost;
                }

                if (ZmenitOsobu)
                {
                    p.OsobaId = osoba;
                }

                if (ZmenitStav && p.Stav != Stav)
                {
                    p.Stav = Stav;
                    if (Stav == StavPolozky.Vyradene)
                    {
                        p.DatumVyradenia ??= DateTime.Today;
                    }
                    else
                    {
                        p.DatumVyradenia = null;
                        p.DovodVyradenia = null;
                    }
                }
            });
        });

        Dialogy.Info($"Zmenených položiek: {zmenene}.");
        Zatvorit?.Invoke(true);
    }

    [RelayCommand]
    private void Zrusit() => Zatvorit?.Invoke(false);
}
