using System.Collections.Concurrent;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.Protocols;

/// <summary>Chooses an address/port pair once per device configuration and application session.</summary>
internal sealed class DeviceAddressSelector
{
    private readonly IDeviceReachability _reachability;
    private readonly ConcurrentDictionary<AddressKey, Lazy<Task<Endpoint>>> _choices = new();

    public DeviceAddressSelector(IDeviceReachability reachability)
    {
        _reachability = reachability;
    }

    public string? GetSelectedHost(Device device)
    {
        return GetSelected(device)?.Host;
    }

    public Device GetSelectedConnection(Device device)
    {
        var endpoint = GetSelected(device);

        return endpoint is null ? device.ForHost(device.Host.Trim()) : device.ForConnection(endpoint.Host, endpoint.Port, endpoint.RtspPort);
    }

    public async Task<string> ResolveAsync(Device device, bool remember, CancellationToken cancellationToken)
    {
        return (await ResolveConnectionAsync(device, remember, cancellationToken).ConfigureAwait(false)).Host;
    }

    public async Task<Device> ResolveConnectionAsync(Device device, bool remember, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = device.ForHost(device.Host.Trim());
        snapshot.SecondaryHost = (device.SecondaryHost ?? "").Trim();
        var key = AddressKey.From(snapshot);
        Endpoint endpoint;

        if (!remember)
        {
            // Manual synchronization can use an existing choice without pinning a new one.
            endpoint = _choices.TryGetValue(key, out var existing)
                ? await existing.Value.WaitAsync(cancellationToken).ConfigureAwait(false)
                : await ProbeAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var choice = _choices.GetOrAdd(key, _ => new Lazy<Task<Endpoint>>(
                () => ProbeAsync(snapshot, CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                endpoint = await choice.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch when (choice.IsValueCreated && choice.Value.IsFaulted)
            {
                _choices.TryRemove(new KeyValuePair<AddressKey, Lazy<Task<Endpoint>>>(key, choice));

                throw;
            }
        }

        return snapshot.ForConnection(endpoint.Host, endpoint.Port, endpoint.RtspPort);
    }

    public void RetainOnly(IReadOnlyCollection<Device> devices)
    {
        var current = devices.Select(AddressKey.From).ToHashSet();

        foreach (var key in _choices.Keys)
        {
            if (!current.Contains(key))
            {
                _choices.TryRemove(key, out _);
            }
        }
    }

    private Endpoint? GetSelected(Device device)
    {
        return _choices.TryGetValue(AddressKey.From(device), out var choice) && choice.IsValueCreated
                && choice.Value.IsCompletedSuccessfully ? choice.Value.Result : null;
    }

    private async Task<Endpoint> ProbeAsync(Device device, CancellationToken token)
    {
        var primary = new Endpoint(device.Host, device.ConnectionPort);
        var secondary = device.ForSecondaryAddress();

        if (string.IsNullOrWhiteSpace(device.SecondaryHost)
            || (string.Equals(device.Host, secondary.Host, StringComparison.OrdinalIgnoreCase)
                && device.ConnectionPort == secondary.ConnectionPort))
        {
            return primary;
        }

        if (await _reachability.IsReachableAsync(device, token).ConfigureAwait(false))
        {
            return primary;
        }

        if (await _reachability.IsReachableAsync(secondary, token).ConfigureAwait(false))
        {
            return new Endpoint(secondary.Host, secondary.ConnectionPort, device.SecondaryRtspPort);
        }

        return primary;
    }

    private sealed record Endpoint
    {
        public string Host { get; init; }

        public int Port { get; init; }

        public int? RtspPort { get; init; }

        public Endpoint(string host, int port, int? rtspPort = null)
        {
            Host = host;
            Port = port;
            RtspPort = rtspPort;
        }
    }

    private sealed record AddressKey
    {
        public Guid Id { get; init; }

        public string Primary { get; init; }

        public string Secondary { get; init; }

        public DeviceProtocol Protocol { get; init; }

        public int HttpPort { get; init; }

        public int RtspPort { get; init; }

        public int SdkPort { get; init; }

        public int? SecondaryPort { get; init; }

        public int? SecondaryRtspPort { get; init; }

        public bool UseHttps { get; init; }

        public AddressKey(
            Guid id,
            string primary,
            string secondary,
            DeviceProtocol protocol,
            int httpPort,
            int rtspPort,
            int sdkPort,
            int? secondaryPort,
            int? secondaryRtspPort,
            bool useHttps)
        {
            Id = id;
            Primary = primary;
            Secondary = secondary;
            Protocol = protocol;
            HttpPort = httpPort;
            RtspPort = rtspPort;
            SdkPort = sdkPort;
            SecondaryPort = secondaryPort;
            SecondaryRtspPort = secondaryRtspPort;
            UseHttps = useHttps;
        }

        public static AddressKey From(Device device)
        {
            return new(device.Id, device.Host.Trim().ToUpperInvariant(),
                (device.SecondaryHost ?? "").Trim().ToUpperInvariant(), device.Protocol,
                device.HttpPort, device.RtspPort, device.SdkPort, device.SecondaryPort, device.SecondaryRtspPort, device.UseHttps);
        }
    }
}
