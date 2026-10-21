using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Services;

/// <summary>Each tile opens its device independently, including synchronous protocol setup.</summary>
internal static class LiveSourceLoader
{
    public static Task<VideoSource> OpenAsync(IDeviceClientFactory factory, Device device,
        int channel, StreamType streamType, CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            using var client = await factory.CreateAsync(device, cancellationToken: cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return await client.GetLiveSourceAsync(channel, streamType, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
}
