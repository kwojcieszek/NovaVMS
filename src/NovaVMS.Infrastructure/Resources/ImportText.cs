using System.Globalization;
using NovaVMS.Core.Localization;

namespace NovaVMS.Infrastructure.Resources;

/// <summary>
/// Localized messages of the import layer (shared catalog in NovaVMS.Core), resolved in the current UI culture.
/// </summary>
internal static class ImportText
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
