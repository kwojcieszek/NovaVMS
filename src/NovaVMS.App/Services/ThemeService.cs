using Avalonia;
using Avalonia.Styling;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>
/// Applies the configured theme whenever the configuration changes.
/// </summary>
public sealed class ThemeService
{
    private readonly ConfigurationService _configuration;

    public ThemeService(ConfigurationService configuration)
    {
        _configuration = configuration;
        _configuration.Changed += OnConfigurationChanged;
    }

    public void Apply(ThemeMode mode)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        application.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        Apply(_configuration.Current.Settings.Theme);
    }
}
