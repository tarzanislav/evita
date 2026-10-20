using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using Evita.Services;

namespace Evita.Pomocne;

/// <summary>
/// Preklad textov zapísaných priamo v XAML (nadpisy, tlačidlá, popisy, tipy…). Pri zobrazení každého prvku sa
/// pevné slovenské texty nahradia prekladom; texty z dát (väzby na údaje) sa neprekladajú – okrem ich formátu
/// (StringFormat), ktorý môže obsahovať slová.
/// </summary>
public static class PrekladacXaml
{
    private static readonly DependencyProperty PrelozeneProperty =
        DependencyProperty.RegisterAttached("Prelozene", typeof(bool), typeof(PrekladacXaml), new PropertyMetadata(false));

    private static bool _zapnuty;

    /// <summary>Zaregistruje preklad pre všetky prvky (volá sa raz pri štarte).</summary>
    public static void Zapni()
    {
        if (_zapnuty)
        {
            return;
        }

        _zapnuty = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, new RoutedEventHandler(PriNacitani), true);
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(PriNacitani), true);
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(PriNacitani), true);
    }

    private static void PriNacitani(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker datum)
        {
            NastavVodoznak(datum);
        }

        if (Preklad.JeSlovencina || sender is not FrameworkElement prvok)
        {
            return;
        }

        try
        {
            Preloz(prvok);
        }
        catch (Exception)
        {
            // preklad nesmie zastaviť zobrazenie okna
        }
    }

    /// <summary>Text „Vyberte dátum“ v prázdnom poli dátumu (WPF ho má len v niektorých jazykoch).</summary>
    private static void NastavVodoznak(DatePicker datum)
    {
        try
        {
            datum.ApplyTemplate();
            if (datum.Template?.FindName("PART_TextBox", datum) is DatePickerTextBox pole)
            {
                pole.ApplyTemplate();
                if (pole.Template?.FindName("PART_Watermark", pole) is ContentControl vodoznak)
                {
                    vodoznak.Content = Preklad.T("Vyberte dátum");
                }
            }
        }
        catch (Exception)
        {
            // pole dátumu funguje aj bez vlastného textu
        }
    }

    /// <summary>Preloží jeden prvok (bez potomkov – tie majú vlastnú udalosť Loaded).</summary>
    public static void Preloz(FrameworkElement prvok)
    {
        if ((bool)prvok.GetValue(PrelozeneProperty))
        {
            return;
        }

        prvok.SetValue(PrelozeneProperty, true);

        switch (prvok)
        {
            case Window okno:
                okno.Language = XmlLanguage.GetLanguage(Preklad.Kultura.IetfLanguageTag);
                PrelozVlastnost(okno, Window.TitleProperty);
                break;
            case TextBlock text:
                PrelozTextBlock(text);
                break;
            case DataGrid tabulka:
                foreach (var stlpec in tabulka.Columns)
                {
                    PrelozVlastnost(stlpec, DataGridColumn.HeaderProperty);
                    if (stlpec is DataGridBoundColumn { Binding: Binding vazba } viazany && PrelozenaVazba(vazba) is { } nova)
                    {
                        viazany.Binding = nova;
                    }
                }

                break;
        }

        if (prvok is HeaderedContentControl or HeaderedItemsControl)
        {
            PrelozVlastnost(prvok, prvok is HeaderedContentControl ? HeaderedContentControl.HeaderProperty : HeaderedItemsControl.HeaderProperty);
        }

        if (prvok is ContentControl and not Window && !JeKontajnerDat(prvok))
        {
            PrelozVlastnost(prvok, ContentControl.ContentProperty);
        }

        PrelozVlastnost(prvok, FrameworkElement.ToolTipProperty);
    }

    /// <summary>Riadok zoznamu vytvorený pre údaj (napr. text z databázy) – jeho obsah sa neprekladá.</summary>
    private static bool JeKontajnerDat(FrameworkElement prvok)
    {
        var zoznam = ItemsControl.ItemsControlFromItemContainer(prvok);
        if (zoznam is null)
        {
            return false;
        }

        var udaj = zoznam.ItemContainerGenerator.ItemFromContainer(prvok);
        return udaj != DependencyProperty.UnsetValue && !ReferenceEquals(udaj, prvok);
    }

    private static void PrelozTextBlock(TextBlock text)
    {
        // väzba na celý text (Text="{Binding …}") – len formát
        if (BindingOperations.GetBindingBase(text, TextBlock.TextProperty) is not null)
        {
            PrelozVlastnost(text, TextBlock.TextProperty);
            return;
        }

        // viac častí (Run, Hyperlink …) – každá zvlášť; jeden obyčajný Run = celý text
        var inlines = text.Inlines;
        if (inlines.Count > 1 || inlines.FirstInline is not Run || inlines.FirstInline is Run prvy && BindingOperations.IsDataBound(prvy, Run.TextProperty))
        {
            PrelozInlines(inlines);
            return;
        }

        PrelozVlastnost(text, TextBlock.TextProperty);
    }

    private static void PrelozInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines.ToList())
        {
            switch (inline)
            {
                case Run run:
                    PrelozVlastnost(run, Run.TextProperty);
                    break;
                case Span span:
                    PrelozInlines(span.Inlines);
                    break;
            }

            PrelozVlastnost(inline, FrameworkContentElement.ToolTipProperty);
        }
    }

    /// <summary>Preloží pevný text vlastnosti; pri väzbe na dáta preloží len jej formát a náhradné texty.</summary>
    private static void PrelozVlastnost(DependencyObject prvok, DependencyProperty vlastnost)
    {
        var zdroj = DependencyPropertyHelper.GetValueSource(prvok, vlastnost);
        if (zdroj.IsExpression)
        {
            // väzbu zo štýlu alebo jeho spúšťača nemožno nahradiť (lokálna hodnota by prebila spúšťače)
            if (zdroj.BaseValueSource is BaseValueSource.Local or BaseValueSource.ParentTemplate
                && BindingOperations.GetBindingBase(prvok, vlastnost) is { } vazba && PrelozenaVazba(vazba) is { } nova)
            {
                BindingOperations.SetBinding(prvok, vlastnost, nova);
            }

            return;
        }

        if (zdroj.BaseValueSource is not (BaseValueSource.Local or BaseValueSource.ParentTemplate or BaseValueSource.Style))
        {
            return;
        }

        if (prvok.GetValue(vlastnost) is string text && text.Length > 0)
        {
            var preklad = Preklad.T(text);
            if (!ReferenceEquals(preklad, text) && preklad != text)
            {
                prvok.SetValue(vlastnost, preklad);
            }
        }
    }

    /// <summary>Kópia väzby s preloženým formátom (StringFormat, TargetNullValue, FallbackValue), alebo null, ak netreba.</summary>
    private static BindingBase? PrelozenaVazba(BindingBase vazba)
    {
        string? Prelozeny(string? text) => string.IsNullOrEmpty(text) ? text : Preklad.T(text);
        object Hodnota(object hodnota) => hodnota is string s && s.Length > 0 ? Preklad.T(s) : hodnota;

        var format = Prelozeny(vazba.StringFormat);
        var nulova = Hodnota(vazba.TargetNullValue);
        var nahradna = Hodnota(vazba.FallbackValue);
        if (format == vazba.StringFormat && Equals(nulova, vazba.TargetNullValue) && Equals(nahradna, vazba.FallbackValue))
        {
            return null;
        }

        switch (vazba)
        {
            case Binding b:
                var nova = new Binding
                {
                    Path = b.Path,
                    Mode = b.Mode,
                    Converter = b.Converter,
                    ConverterParameter = b.ConverterParameter,
                    ConverterCulture = b.ConverterCulture is null ? null : Preklad.Kultura,
                    UpdateSourceTrigger = b.UpdateSourceTrigger,
                    StringFormat = format,
                    TargetNullValue = nulova,
                    FallbackValue = nahradna,
                    Delay = b.Delay,
                    NotifyOnSourceUpdated = b.NotifyOnSourceUpdated,
                    NotifyOnTargetUpdated = b.NotifyOnTargetUpdated,
                    NotifyOnValidationError = b.NotifyOnValidationError,
                    ValidatesOnDataErrors = b.ValidatesOnDataErrors,
                    ValidatesOnExceptions = b.ValidatesOnExceptions,
                    ValidatesOnNotifyDataErrors = b.ValidatesOnNotifyDataErrors,
                    XPath = b.XPath,
                    BindsDirectlyToSource = b.BindsDirectlyToSource,
                    IsAsync = b.IsAsync,
                };
                if (b.ElementName is not null)
                {
                    nova.ElementName = b.ElementName;
                }
                else if (b.RelativeSource is not null)
                {
                    nova.RelativeSource = b.RelativeSource;
                }
                else if (b.Source is not null)
                {
                    nova.Source = b.Source;
                }

                foreach (var pravidlo in b.ValidationRules)
                {
                    nova.ValidationRules.Add(pravidlo);
                }

                return nova;

            case MultiBinding m:
                var novaM = new MultiBinding
                {
                    Mode = m.Mode,
                    Converter = m.Converter,
                    ConverterParameter = m.ConverterParameter,
                    ConverterCulture = m.ConverterCulture is null ? null : Preklad.Kultura,
                    UpdateSourceTrigger = m.UpdateSourceTrigger,
                    StringFormat = format,
                    TargetNullValue = nulova,
                    FallbackValue = nahradna,
                };
                foreach (var cast in m.Bindings)
                {
                    novaM.Bindings.Add(cast);
                }

                return novaM;

            default:
                return null;
        }
    }

    /// <summary>Texty v okne, ktoré nie sú preložené (kontrola prekladu v samoteste).</summary>
    public static IEnumerable<string> NeprelozeneTexty(DependencyObject koren)
    {
        var videne = new HashSet<DependencyObject>();
        var zasobnik = new Stack<DependencyObject>();
        zasobnik.Push(koren);
        while (zasobnik.Count > 0)
        {
            var prvok = zasobnik.Pop();
            if (!videne.Add(prvok))
            {
                continue;
            }

            foreach (var text in TextyPrvku(prvok))
            {
                if (!string.IsNullOrWhiteSpace(text) && !Preklad.MaPreklad(text) && JeSlovenskyText(text))
                {
                    yield return text;
                }
            }

            var pocet = prvok is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetChildrenCount(prvok)
                : 0;
            for (var i = 0; i < pocet; i++)
            {
                zasobnik.Push(System.Windows.Media.VisualTreeHelper.GetChild(prvok, i));
            }

            foreach (var dieta in LogicalTreeHelper.GetChildren(prvok).OfType<DependencyObject>())
            {
                zasobnik.Push(dieta);
            }
        }
    }

    private static IEnumerable<string?> TextyPrvku(DependencyObject prvok)
    {
        switch (prvok)
        {
            case TextBlock t:
                yield return t.Text;
                break;
            case Run r:
                yield return r.Text;
                break;
            case HeaderedContentControl h:
                yield return h.Header as string;
                yield return h.Content as string;
                break;
            case ContentControl c and not Window:
                yield return c.Content as string;
                break;
            case Window w:
                yield return w.Title;
                break;
            case DataGrid g:
                foreach (var s in g.Columns)
                {
                    yield return s.Header as string;
                }

                break;
        }

        if (prvok is FrameworkElement { ToolTip: string tip })
        {
            yield return tip;
        }
    }

    /// <summary>Text so slovenskými znakmi (á, č, ľ, ň, ô, š, ť, ž …) – v inom jazyku je to pravdepodobne nepreložený text.</summary>
    public static bool JeSlovenskyText(string text) => text.Any(z => "áäčďéíĺľňóôŕšťúýžÁÄČĎÉÍĹĽŇÓÔŔŠŤÚÝŽ".Contains(z));
}
