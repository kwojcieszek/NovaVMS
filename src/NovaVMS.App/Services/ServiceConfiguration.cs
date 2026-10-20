using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Abstractions;
using NovaVMS.Infrastructure.Import;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Protocols;
using NovaVMS.Protocols.Onvif;

namespace NovaVMS.App.Services;

/// <summary>
/// Registers application dependencies.
/// </summary>
internal static class ServiceConfiguration
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConfigurationSerializer, JsonConfigurationSerializer>();
        services.AddSingleton<IConfigurationStore>(provider => new JsonConfigurationStore(provider.GetRequiredService<IConfigurationSerializer>()));
        services.AddSingleton<IDeviceDiscovery, OnvifDiscovery>();
        services.AddSingleton<IDeviceClientFactory, DeviceClientFactory>();
        services.AddSingleton<IDeviceReachability, DeviceReachability>();
        services.AddSingleton<DeviceCsvReader>();
        services.AddSingleton(_ => new LibVLC());

        services.AddSingleton<ConfigurationService>();
        services.AddSingleton<DeviceSyncService>();
        services.AddSingleton<StatusBarService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<LanguageService>();
        services.AddSingleton<SdkLibraryService>();
        services.AddSingleton<IDialogService, DialogService>();

        services.AddSingleton<ResourceTreeViewModel>();
        services.AddSingleton<LiveViewViewModel>();
        services.AddSingleton<PlaybackViewModel>();
        services.AddSingleton<PtzViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        services.AddTransient<DevicesSettingsViewModel>();
        services.AddTransient<GroupsSettingsViewModel>();
        services.AddTransient<GeneralSettingsViewModel>();
        services.AddTransient<SettingsWindowViewModel>();
        services.AddSingleton<Func<SettingsWindowViewModel>>(provider => () => provider.GetRequiredService<SettingsWindowViewModel>());

        return services.BuildServiceProvider();
    }
}
