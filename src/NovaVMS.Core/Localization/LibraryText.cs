using System.Globalization;

namespace NovaVMS.Core.Localization;

/// <summary>
/// Localized messages of the libraries (protocols, import, synchronization) from the shared catalog,
/// resolved in the current UI culture set by the application.
/// </summary>
public static class LibraryText
{
    private static readonly JsonTextCatalog Catalog = JsonTextCatalog.Shared;

    public static string Get(string key)
    {
        return Catalog.Get(key, CultureInfo.CurrentUICulture);
    }

    public static string Format(string key, params object?[] arguments)
    {
        return Catalog.Format(key, CultureInfo.CurrentUICulture, arguments);
    }
}
