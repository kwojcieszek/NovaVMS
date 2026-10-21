using NovaVMS.Core.Abstractions;

namespace NovaVMS.App.Services;

/// <summary>
/// Releases long-lived device connections (Hikvision SDK logins, address choices) when devices are removed
/// or their connection settings change, instead of keeping them until the application closes.
/// </summary>
public sealed class DeviceConnectionCleanupService : IDisposable
{
    private readonly ConfigurationService _configuration;
    private readonly IDeviceClientFactory _clientFactory;
    private readonly AsyncWorkQueue _cleanups = new();
    private string? _lastSignature;
    public Task Disposal { get; private set; } = Task.CompletedTask;

    public DeviceConnectionCleanupService(ConfigurationService configuration, IDeviceClientFactory clientFactory)
    {
        _configuration = configuration;
        _clientFactory = clientFactory;
        _configuration.Changed += OnConfigurationChanged;
    }

    public void Dispose()
    {
        _configuration.Changed -= OnConfigurationChanged;
        Disposal = _cleanups.WhenIdle;
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        // Snapshot on the UI thread; the configuration objects are not thread-safe.
        var devices = _configuration.Current.Devices.Select(device => device.ForHost(device.Host)).ToList();
        var signature = string.Join("\n", devices.Select(device => device.GetConnectionSignature()));
        if (signature == _lastSignature)
        {
            // Most saves change only settings or groups; nothing to release.
            return;
        }

        _lastSignature = signature;
        _ = _cleanups.RunAsync(() => _clientFactory.ReleaseUnusedConnections(devices));
    }
}
