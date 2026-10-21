using System.Collections.Concurrent;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Protocols;

/// <summary>Chooses an endpoint once per device configuration and application session.</summary>
internal sealed class DeviceAddressSelector(IDeviceReachability reachability)
{
    private readonly ConcurrentDictionary<AddressKey, Lazy<Task<string>>> _choices = new();

    public string? GetSelectedHost(Device device)
    {
        return _choices.TryGetValue(AddressKey.From(device), out var choice) && choice.IsValueCreated
            && choice.Value.IsCompletedSuccessfully ? choice.Value.Result : null;
    }

    public async Task<string> ResolveAsync(Device device, bool remember, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = device.ForHost(device.Host.Trim());
        snapshot.SecondaryHost = (device.SecondaryHost ?? "").Trim();
        var key = AddressKey.From(snapshot);
        if (!remember)
        {
            // Manual synchronization must not select an address before a camera is opened.
            if (_choices.TryGetValue(key, out var existing))
                return await existing.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await ProbeAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        var choice = _choices.GetOrAdd(key, _ => new Lazy<Task<string>>(
            () => ProbeAsync(snapshot, CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            // One caller canceling must not cancel the shared choice for other channels/tiles.
            return await choice.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (choice.IsValueCreated && choice.Value.IsFaulted)
        {
            _choices.TryRemove(new KeyValuePair<AddressKey, Lazy<Task<string>>>(key, choice));
            throw;
        }
    }

    /// <summary>
    /// Forgets choices of removed devices and of previous connection settings.
    /// </summary>
    public void RetainOnly(IReadOnlyCollection<Device> devices)
    {
        var current = devices.Select(device =>
        {
            var snapshot = device.ForHost(device.Host.Trim());
            snapshot.SecondaryHost = (device.SecondaryHost ?? "").Trim();
            return AddressKey.From(snapshot);
        }).ToHashSet();
        foreach (var key in _choices.Keys)
        {
            if (!current.Contains(key))
            {
                _choices.TryRemove(key, out _);
            }
        }
    }

    private async Task<string> ProbeAsync(Device device, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(device.SecondaryHost)
            || string.Equals(device.Host, device.SecondaryHost, StringComparison.OrdinalIgnoreCase)) return device.Host;
        if (await reachability.IsReachableAsync(device, token).ConfigureAwait(false)) return device.Host;
        if (await reachability.IsReachableAsync(device.ForHost(device.SecondaryHost), token).ConfigureAwait(false))
            return device.SecondaryHost;
        // A failed first attempt is also a final choice: reconnects keep using primary.
        return device.Host;
    }

    private sealed record AddressKey(Guid Id, string Primary, string Secondary, DeviceProtocol Protocol,
        int HttpPort, int RtspPort, int SdkPort, bool UseHttps)
    {
        public static AddressKey From(Device device) => new(device.Id, device.Host.Trim().ToUpperInvariant(),
            (device.SecondaryHost ?? "").Trim().ToUpperInvariant(), device.Protocol,
            device.HttpPort, device.RtspPort, device.SdkPort, device.UseHttps);
    }
}
