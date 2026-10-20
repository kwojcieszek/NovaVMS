using System.ComponentModel;
using System.Globalization;
using NovaVMS.Core.Localization;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Localization;

/// <summary>
/// Translated interface texts (shared catalog in NovaVMS.Core/Resources/Strings). XAML binds to the indexer (through <see cref="TrExtension"/>),
/// so switching the language updates the whole interface without a restart.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    /// <summary>
    /// Languages with a complete translation. The first one is the fallback.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedLanguages = new[] { "en", "pl" };

    // Avalonia refreshes indexer bindings on "Item"; "Item[]" is the name used by other XAML frameworks.
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item");
    private static readonly PropertyChangedEventArgs IndexerArrayChanged = new("Item[]");

    private readonly JsonTextCatalog _catalog = JsonTextCatalog.Shared;
    private CultureInfo _culture = ResolveCulture(string.Empty);

    private Localizer()
    {
        ApplyCulture(_culture);
    }

    public static Localizer Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised after the interface language has changed.
    /// </summary>
    public event EventHandler? LanguageChanged;

    public CultureInfo Culture => _culture;

    public string this[string key] => _catalog.Get(key, _culture);

    public static string Text(string key)
    {
        return Instance[key];
    }

    public static string Format(string key, params object?[] arguments)
    {
        return string.Format(CultureInfo.CurrentCulture, Instance[key], arguments);
    }

    /// <summary>
    /// Sets the interface language; an empty code selects the system language.
    /// </summary>
    public void SetLanguage(string languageCode)
    {
        var culture = ResolveCulture(languageCode);
        if (culture.Name == _culture.Name)
        {
            return;
        }

        _culture = culture;
        ApplyCulture(culture);

        PropertyChanged?.Invoke(this, IndexerChanged);
        PropertyChanged?.Invoke(this, IndexerArrayChanged);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static CultureInfo ResolveCulture(string languageCode)
    {
        var requested = string.IsNullOrWhiteSpace(languageCode)
            ? CultureInfo.InstalledUICulture.TwoLetterISOLanguageName
            : languageCode.Trim().ToLowerInvariant();

        var supported = SupportedLanguages.Contains(requested) ? requested : SupportedLanguages[0];
        return CultureInfo.GetCultureInfo(supported);
    }

    // Library messages (protocol errors, import notes) are resolved in the current UI culture.
    // The default channel name is set before LanguageChanged is raised, so rebuilt lists already use it.
    private void ApplyCulture(CultureInfo culture)
    {
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Channel.DefaultNameFormat = _catalog.Get("Camera_DefaultName", culture);
    }
}
