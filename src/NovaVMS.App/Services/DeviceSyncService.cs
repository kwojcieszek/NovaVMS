using System.Collections.Concurrent;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>
/// Reads device information and channel lists, and tracks device availability.
/// </summary>
public sealed class DeviceSyncService
{
    private readonly ConfigurationService _configuration;
    private readonly IDeviceClientFactory _clientFactory;
    private readonly ConcurrentDictionary<Guid, bool> _online = new();
    private readonly OperationLifetime _operations = new();

    public Task ShutdownAsync() => _operations.StopAsync();

    public DeviceSyncService(ConfigurationService configuration, IDeviceClientFactory clientFactory)
    {
        _configuration = configuration;
        _clientFactory = clientFactory;
    }

    public event EventHandler? StatusChanged;

    public bool IsOnline(Guid deviceId)
    {
        return _online.TryGetValue(deviceId, out var online) && online;
    }

    /// <summary>
    /// Synchronizes all devices in parallel and saves the configuration. Returns the number of devices online.
    /// </summary>
    public async Task<int> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var devices = _configuration.Current.Devices.ToList();
        var results = await Task.WhenAll(devices.Select(device => TrySyncAsync(device, cancellationToken)));

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var device in devices)
        {
            _configuration.SyncDeviceGroup(device, allowCreate: false);
        }

        await _configuration.SaveAsync();
        return results.Count(result => result.Success);
    }

    public Task<DeviceSyncResult> TrySyncAsync(Device device, CancellationToken cancellationToken = default)
        => _operations.RunAsync(token => TrySyncCoreAsync(device, token), cancellationToken);

    private async Task<DeviceSyncResult> TrySyncCoreAsync(Device device, CancellationToken cancellationToken)
    {
        try
        {
            using var client = await _clientFactory.CreateAsync(device, rememberAddress: false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (client is IDeviceConnectionClient connectionClient)
            {
                var connection = await connectionClient.TryConnectAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!connection.Success)
                {
                    SetOnline(device.Id, false);
                    var message = DeviceOperationErrors.Report($"Device synchronization: {device.DisplayName} ({device.Host})", connection.Error!);
                    return new DeviceSyncResult(false, null, message);
                }
            }

            var info = await client.GetDeviceInfoAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var channels = await client.GetChannelsAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            device.Type = info.Type;
            device.Model = string.IsNullOrWhiteSpace(info.Model) ? device.Model : info.Model;
            device.SerialNumber = string.IsNullOrWhiteSpace(info.SerialNumber) ? device.SerialNumber : info.SerialNumber;
            device.FirmwareVersion = string.IsNullOrWhiteSpace(info.FirmwareVersion) ? device.FirmwareVersion : info.FirmwareVersion;
            if (string.IsNullOrWhiteSpace(device.Name))
            {
                device.Name = info.DeviceName;
            }

            // Known cameras keep their identity, so views and groups still show the same cameras.
            ChannelIdentity.Keep(device.Channels, channels);
            if (_clientFactory.UsesOptionalProtocol(device) && device.OptionalConnection is { } optional)
            {
                optional.Channels = channels.ToList();
                // Keep common camera identities but retain the primary protocol's media metadata.
                device.Channels = channels.Select(channel => new Channel
                {
                    Id = channel.Id, Number = channel.Number, Name = channel.Name,
                    Enabled = channel.Enabled, IsFavorite = channel.IsFavorite,
                    MainAspectRatio = channel.MainAspectRatio,
                    MainStreamUri = device.Channels.FirstOrDefault(old => old.Id == channel.Id)?.MainStreamUri,
                    SubStreamUri = device.Channels.FirstOrDefault(old => old.Id == channel.Id)?.SubStreamUri,
                    ProfileToken = device.Channels.FirstOrDefault(old => old.Id == channel.Id)?.ProfileToken
                }).ToList();
            }
            else device.Channels = channels.ToList();
            SetOnline(device.Id, true);
            return new DeviceSyncResult(true, info, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception) || exception is OperationCanceledException)
        {
            SetOnline(device.Id, false);
            var message = DeviceOperationErrors.Report($"Device synchronization: {device.DisplayName} ({device.Host})", exception);
            return new DeviceSyncResult(false, null, message);
        }
    }

    private void SetOnline(Guid deviceId, bool online)
    {
        _online[deviceId] = online;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
