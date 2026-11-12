using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Presentation.Localization;
using NovaVMS.Protocols;
using NovaVMS.Protocols.Isapi;
using NovaVMS.Protocols.Onvif;

namespace NovaVMS.Mobile.Services;

public static class MobileDevices
{
    /// <summary>Checks an address and protocol the mobile application can connect to.</summary>
    public static string? Validate(Device device)
    {
        if (ValidateAddress(device) is { } error)
        {
            return error;
        }

        if (!IsSupported(device.Protocol))
        {
            return Localizer.Text("Mobile_UnsupportedProtocol");
        }

        return null;
    }

    /// <summary>Checks the address and ports; any protocol may be saved, because the configuration is shared with desktops.</summary>
    public static string? ValidateAddress(Device device)
    {
        if (Uri.CheckHostName(device.Host.Trim()) == UriHostNameType.Unknown)
        {
            return Localizer.Text("Mobile_InvalidHost");
        }

        if (device.HttpPort is < 1 or > 65535 || device.RtspPort is < 1 or > 65535 || device.SdkPort is < 1 or > 65535)
        {
            return Localizer.Text("Mobile_InvalidPort");
        }

        return null;
    }

    /// <summary>
    /// Connection settings used on this device: the optional protocol when preferred, otherwise the primary one.
    /// The Hikvision SDK is not available on phones, so a device using it falls back to its optional ISAPI/ONVIF connection.
    /// </summary>
    public static Device? Resolve(Device device, bool preferOptional)
    {
        var optional = device.OptionalConnection is { } connection && IsSupported(connection.Protocol);

        if (preferOptional && optional)
        {
            return device.ForProtocol(true);
        }

        if (IsSupported(device.Protocol))
        {
            return device.ForProtocol(false);
        }

        return optional ? device.ForProtocol(true) : null;
    }

    /// <summary>
    /// Chooses the reachable address and creates the client on the thread pool; the returned client also runs
    /// every request there, so connecting never blocks the interface thread.
    /// </summary>
    public static Task<IDeviceClient> ConnectAsync(Device device, bool preferOptional, CancellationToken cancellationToken = default)
    {
        return Task.Run<IDeviceClient>(async () =>
            new BackgroundDeviceClient(await CreateClientAsync(device, preferOptional, cancellationToken)), cancellationToken);
    }

    // A tap produces a bounded pulse. Stop is sent even when navigation cancels the pulse.
    public static async Task PulseAsync(IDeviceClient client, int channel, int pan, int tilt, int zoom,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await client.PtzMoveAsync(channel, pan, tilt, zoom, cancellationToken);
            await Task.Delay(250, cancellationToken);
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            await client.PtzStopAsync(channel, timeout.Token);
        }
    }

    private static async Task<IDeviceClient> CreateClientAsync(Device device, bool preferOptional, CancellationToken cancellationToken)
    {
        var resolved = Resolve(device, preferOptional);

        if (resolved is null)
        {
            throw new ArgumentException(Localizer.Text("Mobile_UnsupportedProtocol"));
        }

        if (Validate(resolved) is { } error)
        {
            throw new ArgumentException(error);
        }

        var selected = resolved.ForHost(resolved.Host.Trim());
        var name = device.DisplayName;

        MobileLog.Post($"Connect: {name}: protocol {resolved.Protocol}{(resolved.Protocol != device.Protocol ? $" (configured {device.Protocol}, optional connection used)" : "")}, "
            + $"primary {Endpoint(selected)}"
            + (string.IsNullOrWhiteSpace(resolved.SecondaryHost) ? ", no secondary address" : $", secondary {Endpoint(resolved.ForSecondaryAddress())}"));

        if (!string.IsNullOrWhiteSpace(resolved.SecondaryHost))
        {
            var reachability = new DeviceReachability();
            var primaryReachable = await reachability.IsReachableAsync(selected, cancellationToken);

            MobileLog.Post($"Connect: {name}: primary {selected.Host}:{selected.ConnectionPort} {(primaryReachable ? "reachable" : "not reachable within 3 s")}");

            if (!primaryReachable)
            {
                var secondary = resolved.ForSecondaryAddress();
                var secondaryReachable = await reachability.IsReachableAsync(secondary, cancellationToken);

                MobileLog.Post($"Connect: {name}: secondary {secondary.Host}:{secondary.ConnectionPort} {(secondaryReachable ? "reachable" : "not reachable within 3 s")}");

                if (secondaryReachable)
                {
                    selected = secondary;
                }
            }
        }

        MobileLog.Post($"Connect: {name}: using {Endpoint(selected)}");

        return selected.Protocol switch
        {
            DeviceProtocol.Isapi => new IsapiDeviceClient(selected),
            DeviceProtocol.Onvif => new OnvifDeviceClient(selected),
            _ => throw new NotSupportedException()
        };
    }

    // The port the protocol talks to (HTTP for ISAPI and ONVIF) and the video port.
    private static string Endpoint(Device device)
    {
        var scheme = device.UseHttps ? "https" : "http";

        return $"{device.Protocol} {scheme}://{device.Host}:{device.ConnectionPort}, RTSP port {device.RtspPort}";
    }

    private static bool IsSupported(DeviceProtocol protocol)
    {
        return protocol is DeviceProtocol.Isapi or DeviceProtocol.Onvif;
    }
}
