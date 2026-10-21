using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>Checks configured endpoints afresh, independently of the endpoint pinned by live playback.</summary>
internal static class DeviceAvailability
{
    public static async Task<bool> CheckAsync(IDeviceReachability reachability, Device device, CancellationToken token = default)
    {
        var primary = device.ForHost(device.Host.Trim());
        var secondary = device.ForSecondaryAddress();
        var checks = new List<Task<bool>> { CheckEndpointAsync(reachability, primary, token) };
        if (!string.IsNullOrWhiteSpace(secondary.Host)
            && (!string.Equals(primary.Host, secondary.Host, StringComparison.OrdinalIgnoreCase)
                || primary.ConnectionPort != secondary.ConnectionPort))
            checks.Add(CheckEndpointAsync(reachability, secondary, token));
        var results = await Task.WhenAll(checks).ConfigureAwait(false);
        return results.Any(reachable => reachable);
    }

    private static async Task<bool> CheckEndpointAsync(IDeviceReachability reachability, Device endpoint, CancellationToken token)
    {
        try { return await reachability.IsReachableAsync(endpoint, token).ConfigureAwait(false); }
        catch (Exception exception) when (DeviceFailure.IsExpected(exception))
        {
            DeviceOperationErrors.Report($"Device availability: {endpoint.DisplayName}", exception);
            return false;
        }
    }
}
