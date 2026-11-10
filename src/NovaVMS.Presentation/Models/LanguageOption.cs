using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.Presentation.Localization;

namespace NovaVMS.Presentation.Models;

/// <summary>
/// Entry of the language list in the settings. Language names are shown in their own language;
/// the first entry follows the system language.
/// </summary>
public sealed class LanguageOption : ObservableObject
{
    private readonly string? _nativeName;

    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new(string.Empty, null),
        .. InterfaceLanguage.All.Select(language => new LanguageOption(language.Code, language.NativeName))
    ];

    /// <summary>
    /// Language code stored in the settings; empty means the system language.
    /// </summary>
    public string Code { get; }

    public string Label => _nativeName ?? Localizer.Text("Language_System");

    private LanguageOption(string code, string? nativeName)
    {
        Code = code;
        _nativeName = nativeName;

        Localizer.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public static LanguageOption Find(string code)
    {
        return All.FirstOrDefault(option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }
}
