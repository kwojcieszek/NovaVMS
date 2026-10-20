using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.App.Localization;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Models;

/// <summary>
/// Entry of the theme list in the settings.
/// </summary>
public sealed class ThemeOption : ObservableObject
{
    private readonly string _labelKey;

    private ThemeOption(ThemeMode value, string labelKey)
    {
        Value = value;
        _labelKey = labelKey;
        Localizer.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public static IReadOnlyList<ThemeOption> All { get; } = new List<ThemeOption>
    {
        new(ThemeMode.System, "Theme_System"),
        new(ThemeMode.Light, "Theme_Light"),
        new(ThemeMode.Dark, "Theme_Dark")
    };

    public ThemeMode Value { get; }

    public string Label => Localizer.Text(_labelKey);

    public static ThemeOption Find(ThemeMode value)
    {
        return All.FirstOrDefault(option => option.Value == value) ?? All[0];
    }
}
