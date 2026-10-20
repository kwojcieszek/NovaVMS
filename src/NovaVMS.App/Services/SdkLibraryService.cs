using NovaVMS.Protocols.HcNetSdk;

namespace NovaVMS.App.Services;

/// <summary>
/// Points the Hikvision SDK loader to the library folder chosen in the settings.
/// </summary>
public sealed class SdkLibraryService
{
    private readonly ConfigurationService _configuration;

    public SdkLibraryService(ConfigurationService configuration)
    {
        _configuration = configuration;
        _configuration.Changed += OnConfigurationChanged;
        Apply();
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        Apply();
    }

    private void Apply()
    {
        HcNetSdkRuntime.LibraryDirectory = _configuration.Current.Settings.HcNetSdkDirectory;
    }
}
