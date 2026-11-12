using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>
/// Checks configured endpoints afresh, independently of the endpoint pinned by live playback.
/// A device counts as available when it answers on its control port or on the port of another protocol:
/// RTSP for every protocol and additionally the Hikvision SDK port when ISAPI or ONVIF is configured.
/// </summary>
internal static class DeviceAvailability
{
    /// <summary>Interval between periodic checks; the first periodic check also runs one interval after start.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    public static async Task<bool> CheckAsync(IDeviceReachability reachability, Device device, CancellationToken token = default)
    {
        var primary = device.ForHost(device.Host.Trim());
        var secondary = device.ForSecondaryAddress();
        var hasSecondary = !string.IsNullOrWhiteSpace(secondary.Host);
        var probed = new HashSet<(string Host, int Port)>();
        var checks = new List<Task<bool>>();

        AddControlCheck(reachability, primary, probed, checks, token);

        if (hasSecondary)
        {
            AddControlCheck(reachability, secondary, probed, checks, token);
        }

        AddAlternativeChecks(reachability, primary, primary.SdkPort, probed, checks, token);

        if (hasSecondary)
        {
            var secondarySdkPort = device.SecondarySdkPort ?? device.SdkPort;

            AddAlternativeChecks(reachability, secondary, secondarySdkPort, probed, checks, token);
        }

        var results = await Task.WhenAll(checks).ConfigureAwait(false);

        return results.Any(reachable => reachable);
    }

    private static void AddControlCheck(IDeviceReachability reachability, Device endpoint,
        HashSet<(string Host, int Port)> probed, List<Task<bool>> checks, CancellationToken token)
    {
        if (!probed.Add(Key(endpoint.Host, endpoint.ConnectionPort)))
        {
            return;
        }

        var check = ProbeAsync(endpoint.DisplayName, () => reachability.IsReachableAsync(endpoint, token));

        checks.Add(check);
    }

    private static void AddAlternativeChecks(IDeviceReachability reachability, Device endpoint, int sdkPort,
        HashSet<(string Host, int Port)> probed, List<Task<bool>> checks, CancellationToken token)
    {
        AddPortCheck(reachability, endpoint, endpoint.RtspPort, probed, checks, token);

        if (endpoint.Protocol != DeviceProtocol.HikvisionSdk)
        {
            AddPortCheck(reachability, endpoint, sdkPort, probed, checks, token);
        }
    }

    private static void AddPortCheck(IDeviceReachability reachability, Device endpoint, int port,
        HashSet<(string Host, int Port)> probed, List<Task<bool>> checks, CancellationToken token)
    {
        if (port is < 1 or > 65535 || !probed.Add(Key(endpoint.Host, port)))
        {
            return;
        }

        var check = ProbeAsync(endpoint.DisplayName, () => reachability.IsPortReachableAsync(endpoint.Host, port, token));

        checks.Add(check);
    }

    private static (string Host, int Port) Key(string host, int port)
    {
        return (host.Trim().ToUpperInvariant(), port);
    }

    private static async Task<bool> ProbeAsync(string name, Func<Task<bool>> probe)
    {
        try
        {
            return await probe().ConfigureAwait(false);
        }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            DeviceOperationErrors.Report($"Device availability: {name}", exception);

            return false;
        }
    }
}
