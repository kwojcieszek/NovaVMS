using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace NovaVMS.Core.Localization;

/// <summary>
/// Translated texts stored as JSON files embedded in an assembly: one flat object
/// (<c>{ "Key": "Text" }</c>) per language, e.g. <c>Resources/Strings/en.json</c> and <c>pl.json</c>.
/// Missing keys fall back to the fallback language, then to the key itself.
/// </summary>
public sealed class JsonTextCatalog
{
    public const string FallbackLanguage = "en";

    /// <summary>
    /// Translations of the whole solution, embedded in this assembly (NovaVMS.Core/Resources/Strings).
    /// </summary>
    public static JsonTextCatalog Shared { get; } = new(typeof(JsonTextCatalog).Assembly, "NovaVMS.Core.Resources.Strings.");

    private readonly Assembly _assembly;
    private readonly string _resourcePrefix;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _languages = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="assembly">Assembly with the embedded files.</param>
    /// <param name="resourcePrefix">Manifest resource name before the language code, e.g. "NovaVMS.App.Resources.Strings.".</param>
    public JsonTextCatalog(Assembly assembly, string resourcePrefix)
    {
        _assembly = assembly;
        _resourcePrefix = resourcePrefix;
    }

    public string Get(string key, CultureInfo culture)
    {
        if (Load(culture.TwoLetterISOLanguageName).TryGetValue(key, out var text))
        {
            return text;
        }

        return Load(FallbackLanguage).TryGetValue(key, out var fallback) ? fallback : key;
    }

    public string Format(string key, CultureInfo culture, params object?[] arguments)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key, culture), arguments);
    }

    private IReadOnlyDictionary<string, string> Load(string language)
    {
        return _languages.GetOrAdd(language, code =>
        {
            using var stream = _assembly.GetManifestResourceStream($"{_resourcePrefix}{code}.json");
            if (stream is null)
            {
                return new Dictionary<string, string>();
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
        });
    }
}
