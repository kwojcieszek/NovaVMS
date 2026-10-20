using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace NovaVMS.App.Localization;

/// <summary>
/// XAML markup extension for translated texts: <c>Text="{l:Tr App_LiveView}"</c>.
/// Produces a binding to the localizer, so the text follows language changes.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding($"[{Key}]")
        {
            Source = Localizer.Instance,
            Mode = BindingMode.OneWay
        };
    }
}
