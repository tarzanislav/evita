using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evita.Pomocne;
using Evita.Services;

namespace Evita.ViewModels;

/// <summary>Časť Poznámky: zoznam poznámok s vyhľadávaním a úprava vybranej poznámky.</summary>
public partial class PoznamkyViewModel : ObservableObject, IObnovitelny
{
    private readonly PoznamkyService _sluzba = new();
    private bool _menimVyber;

    [ObservableProperty] private List<PoznamkaRiadok> _zoznam = new();
    [ObservableProperty] private PoznamkaRiadok? _vybrana;
    [ObservableProperty] private PoznamkaEditViewModel? _editor;
    [ObservableProperty] private string _hladat = "";
    [ObservableProperty] private string _suhrn = "";

    public bool JeEditor => Editor is not null;

    partial void OnEditorChanged(PoznamkaEditViewModel? value) => OnPropertyChanged(nameof(JeEditor));

    partial void OnHladatChanged(string value) => NacitajZoznam(Editor?.Id);

    public void Obnov()
    {
        UlozAkTreba();
        NacitajZoznam(Editor is { JeNova: false } e ? e.Id : null);
        if (Editor is { JeNova: false } upravovana && Vybrana?.Id == upravovana.Id)
        {
            return;
        }

        if (Editor is null && Zoznam.Count > 0)
        {
            Vybrana = Zoznam[0];
        }
    }

    /// <summary>Pred odchodom z časti (alebo zatvorením programu) uloží rozpísanú poznámku.</summary>
    public void UlozAkTreba()
    {
        if (Editor is { Zmenena: true } e)
        {
            e.Uloz();
        }
    }

    private void NacitajZoznam(int? vybrat)
    {
        try
        {
            Zoznam = _sluzba.Zoznam(Hladat);
            var vsetky = string.IsNullOrWhiteSpace(Hladat) ? Zoznam.Count : _sluzba.Pocet();
            Suhrn = string.IsNullOrWhiteSpace(Hladat)
                ? Preklad.Pocet(vsetky, "{0} poznámka", "{0} poznámky", "{0} poznámok")
                : Preklad.F($"Nájdené: {Zoznam.Count} z {vsetky}");
            _menimVyber = true;
            Vybrana = vybrat is int id ? Zoznam.FirstOrDefault(p => p.Id == id) : null;
        }
        catch (Exception ex)
        {
            Suhrn = Preklad.F($"Poznámky sa nepodarilo načítať: {ex.GetBaseException().Message}");
        }
        finally
        {
            _menimVyber = false;
        }
    }

    partial void OnVybranaChanged(PoznamkaRiadok? value)
    {
        if (_menimVyber)
        {
            return;
        }

        var predosla = Editor;
        if (predosla is { Zmenena: true })
        {
            if (!predosla.Uloz())
            {
                return;
            }

            // názov alebo poradie sa mohli zmeniť
            NacitajZoznam(value?.Id);
        }

        OtvorVEditore(value?.Id);
    }

    private void OtvorVEditore(int? id)
    {
        if (Editor is not null)
        {
            Editor.Hotovo -= PoZmene;
        }

        try
        {
            Editor = id is int i ? new PoznamkaEditViewModel(i) : null;
        }
        catch (EvitaException ex)
        {
            Dialogy.Upozornenie(ex.Message);
            Editor = null;
        }

        if (Editor is not null)
        {
            Editor.Hotovo += PoZmene;
        }
    }

    private void PoZmene(bool vymazana)
    {
        if (vymazana)
        {
            Editor!.Hotovo -= PoZmene;
            Editor = null;
            NacitajZoznam(null);
            if (Zoznam.Count > 0)
            {
                Vybrana = Zoznam[0];
            }

            return;
        }

        NacitajZoznam(Editor?.Id);
    }

    /// <summary>Nová poznámka (voliteľne k dňu, položke alebo udalosti).</summary>
    [RelayCommand]
    private void NovaPoznamka()
    {
        UlozAkTreba();
        _menimVyber = true;
        Vybrana = null;
        _menimVyber = false;
        if (Editor is not null)
        {
            Editor.Hotovo -= PoZmene;
        }

        Editor = new PoznamkaEditViewModel(null);
        Editor.Hotovo += PoZmene;
        ZiadostOZameranie?.Invoke(true);
    }

    /// <summary>Žiadosť pre obrazovku: dať kurzor do editora (true = do názvu).</summary>
    public event Action<bool>? ZiadostOZameranie;

    /// <summary>Vyberie poznámku (napr. kliknutím na dashboarde).</summary>
    public void Vyber(int id)
    {
        Hladat = "";
        NacitajZoznam(null);
        Vybrana = Zoznam.FirstOrDefault(p => p.Id == id);
    }

    [RelayCommand]
    private void Ulozit() => Editor?.UlozitCommand.Execute(null);

    [RelayCommand]
    private void PrepnutPripnutie(PoznamkaRiadok? poznamka)
    {
        if (poznamka is null)
        {
            return;
        }

        UlozAkTreba();
        _sluzba.NastavPripnutie(poznamka.Id, !poznamka.Pripnuta);
        var vybrana = Editor?.Id;
        NacitajZoznam(vybrana);
        if (Editor is { JeNova: false } e && e.Id == poznamka.Id)
        {
            OtvorVEditore(e.Id);
        }
    }

    [RelayCommand]
    private void ZrusitHladanie() => Hladat = "";
}
