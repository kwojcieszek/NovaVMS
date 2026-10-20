using NovaVMS.App.Localization;

namespace NovaVMS.App.Services;

/// <summary>
/// Applies the configured interface language whenever the configuration changes.
/// </summary>
public sealed class LanguageService
{
    private readonly ConfigurationService _configuration;

    public LanguageService(ConfigurationService configuration)
    {
        _configuration = configuration;
        _configuration.Changed += OnConfigurationChanged;
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        Localizer.Instance.SetLanguage(_configuration.Current.Settings.Language);
    }
}
