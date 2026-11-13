namespace NovaVMS.Presentation.Localization;

/// <summary>
/// Language with a complete translation in the shared catalog (NovaVMS.Core/Resources/Strings/{Code}.json).
/// </summary>
/// <param name="Code">Two letter language code stored in the settings and used as the catalog file name.</param>
/// <param name="CultureName">Culture used for dates and numbers; it may name a script, e.g. simplified Chinese.</param>
/// <param name="NativeName">Name shown in the language list, written in the language itself.</param>
public sealed record InterfaceLanguage
{
    /// <summary>Languages of the interface; the first one is the fallback for missing texts and unknown languages.</summary>
    public static IReadOnlyList<InterfaceLanguage> All { get; } =
    [
        new("en", "en", "English"),
        new("pl", "pl", "Polski")
    ];

    public string Code { get; init; }

    public string CultureName { get; init; }

    public string NativeName { get; init; }

    public InterfaceLanguage(string code, string cultureName, string nativeName)
    {
        Code = code;
        CultureName = cultureName;
        NativeName = nativeName;
    }

    /// <summary>The language for <paramref name="code"/>, or the fallback language when it has no translation.</summary>
    public static InterfaceLanguage Find(string code)
    {
        return All.FirstOrDefault(language => string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }
}
