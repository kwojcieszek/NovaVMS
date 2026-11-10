using NovaVMS.Core.Models;

namespace NovaVMS.Presentation.Models;

/// <summary>
/// Entry of the theme list in the settings.
/// </summary>
public sealed class ThemeOption : LocalizedOption<ThemeMode>
{
    public static IReadOnlyList<ThemeOption> All { get; } =
    [
        new(ThemeMode.System, "Theme_System"),
        new(ThemeMode.Light, "Theme_Light"),
        new(ThemeMode.Dark, "Theme_Dark")
    ];

    private ThemeOption(ThemeMode value, string labelKey)
        : base(value, labelKey)
    {
    }

    public static ThemeOption Find(ThemeMode value)
    {
        return Find(All, value);
    }
}
