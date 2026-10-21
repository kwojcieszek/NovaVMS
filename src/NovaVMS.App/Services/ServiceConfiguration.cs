using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Abstractions;
using NovaVMS.Infrastructure.Import;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Infrastructure.Sync;
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
        services.AddSingleton<ISettingsSyncOptionsStore, SettingsSyncOptionsStore>();
        services.AddSingleton<IRemoteSettingsStoreFactory, RemoteSettingsStoreFactory>();
        services.AddSingleton<IDeviceDiscovery, OnvifDiscovery>();
        services.AddSingleton<IDeviceClientFactory, DeviceClientFactory>();
        services.AddSingleton<IDeviceReachability, DeviceReachability>();
        services.AddSingleton<DeviceCsvReader>();
        // Internal transition snapshots must not display filenames or preview thumbnails over video.
        services.AddSingleton(_ => new LibVLC("--no-osd", "--no-snapshot-preview", "--no-video-title-show"));

        services.AddSingleton<ConfigurationService>();
        services.AddSingleton<ApplicationUpdateService>();
        services.AddSingleton<DeviceSyncService>();
        services.AddSingleton<SettingsSyncService>();
        services.AddSingleton<DeviceConnectionCleanupService>();
        services.AddSingleton<StatusBarService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<LanguageService>();
        services.AddSingleton<SdkLibraryService>();
        services.AddSingleton<IDialogService, DialogService>();

        services.AddSingleton<ResourceTreeViewModel>();
        services.AddSingleton<LiveViewViewModel>();
        services.AddSingleton<PlaybackViewModel>();
        services.AddSingleton<RecordingExporter>();
        services.AddSingleton<PtzViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        // Settings pages live in a scope per settings window. Resolved from the root provider, disposable
        // transients would be kept by it until the application closes, including the preview's video players.
        services.AddScoped<DevicesSettingsViewModel>();
        services.AddScoped<GroupCameraPreviewViewModel>();
        services.AddScoped<GroupsSettingsViewModel>();
        services.AddScoped<GeneralSettingsViewModel>();
        services.AddScoped<SyncSettingsViewModel>();
        services.AddScoped<SettingsWindowViewModel>();
        services.AddSingleton<Func<SettingsWindowViewModel>>(provider => () =>
        {
            var scope = provider.CreateScope();
            try
            {
                var viewModel = scope.ServiceProvider.GetRequiredService<SettingsWindowViewModel>();
                viewModel.OwnScope(scope);
                return viewModel;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        });

        return services.BuildServiceProvider();
    }
}

