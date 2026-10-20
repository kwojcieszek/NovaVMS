using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.App.Localization;

namespace NovaVMS.App.Models;

/// <summary>
/// Entry of the language list in the settings. Language names are shown in their own language.
/// </summary>
public sealed class LanguageOption : ObservableObject
{
    private readonly string? _nativeName;

    private LanguageOption(string code, string? nativeName)
    {
        Code = code;
        _nativeName = nativeName;
        Localizer.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public static IReadOnlyList<LanguageOption> All { get; } = new List<LanguageOption>
    {
        new(string.Empty, null),
        new("pl", "Polski"),
        new("en", "English")
    };

    /// <summary>
    /// Language code stored in the settings; empty means the system language.
    /// </summary>
    public string Code { get; }

    public string Label => _nativeName ?? Localizer.Text("Language_System");

    public static LanguageOption Find(string code)
    {
        return All.FirstOrDefault(option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }
}
