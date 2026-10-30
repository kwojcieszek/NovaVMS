using System.Xml.Linq;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Common;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols.Isapi;

/// <summary>
/// Reads device information and the channel list through ISAPI, independently of the transport.
/// </summary>
internal sealed class IsapiDeviceService
{
    private readonly IIsapiTransport _transport;

    public IsapiDeviceService(IIsapiTransport transport)
    {
        _transport = transport;
    }

    public async Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken)
    {
        var document = await _transport.GetXmlAsync("ISAPI/System/deviceInfo", cancellationToken).ConfigureAwait(false);
        var root = document.Root;
        if (root is null)
        {
            throw new DeviceCommunicationException(ProtocolText.Get("EmptyDeviceInfo"));
        }

        var model = root.ChildValue("model");
        return new DeviceInfo
        {
            DeviceName = root.ChildValue("deviceName"),
            Model = model,
            SerialNumber = root.ChildValue("serialNumber"),
            MacAddress = root.ChildValue("macAddress"),
            FirmwareVersion = root.ChildValue("firmwareVersion"),
            Type = ParseDeviceType(root.ChildValue("deviceType"), model)
        };
    }

    public async Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        var document = await _transport.GetXmlAsync("ISAPI/Streaming/channels", cancellationToken).ConfigureAwait(false);
        var channels = new List<Channel>();

        foreach (var element in document.DescendantsByLocalName("StreamingChannel"))
        {
            if (!int.TryParse(element.ChildValue("id"), out var streamId))
            {
                continue;
            }

            if (streamId % IsapiStreamId.Multiplier != IsapiStreamId.MainSuffix)
            {
                continue;
            }

            var channelNumber = streamId / IsapiStreamId.Multiplier;
            var video = element.Elements().FirstOrDefault(child => child.Name.LocalName == "Video");
            _ = uint.TryParse(video?.ChildValue("videoResolutionWidth"), out var width);
            _ = uint.TryParse(video?.ChildValue("videoResolutionHeight"), out var height);
            channels.Add(new Channel
            {
                Number = channelNumber,
                MainAspectRatio = VideoDisplayGeometry.FromDimensions(width, height),
                Name = ChannelNameNormalizer.Normalize(element.ChildValue("channelName"), channelNumber),
                Enabled = !string.Equals(element.ChildValue("enabled"), "false", StringComparison.OrdinalIgnoreCase)
            });
        }

        await ApplyChannelStatusAsync(channels, cancellationToken).ConfigureAwait(false);

        return channels.OrderBy(channel => channel.Number).ToList();
    }

    public static DeviceType ParseDeviceType(string deviceType, string model)
    {
        if (deviceType.Contains("NVR", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceType.Nvr;
        }

        if (deviceType.Contains("DVR", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceType.Dvr;
        }

        // Some recorders (e.g. Turbo HD DVRs) report "IPCamera"; the model prefix is more reliable:
        // DS-7/DS-8/DS-9 are recorders, "NI" in the model marks a network video recorder.
        var recorderPrefixes = new[] { "DS-7", "DS-8", "DS-9", "iDS-7", "iDS-9" };
        if (recorderPrefixes.Any(prefix => model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return model.Contains("NI", StringComparison.OrdinalIgnoreCase) ? DeviceType.Nvr : DeviceType.Dvr;
        }

        if (deviceType.StartsWith("IP", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceType.IpCamera;
        }

        return DeviceType.Unknown;
    }

    private async Task ApplyChannelStatusAsync(List<Channel> channels, CancellationToken cancellationToken)
    {
        XDocument document;
        try
        {
            document = await _transport.GetXmlAsync("ISAPI/ContentMgmt/InputProxy/channels/status", cancellationToken).ConfigureAwait(false);
        }
        catch (DeviceCommunicationException)
        {
            // IP cameras do not expose proxy channel status - treat all channels as available.
            return;
        }

        foreach (var status in document.DescendantsByLocalName("InputProxyChannelStatus"))
        {
            if (!int.TryParse(status.ChildValue("id"), out var channelNumber) || channelNumber <= 0)
            {
                continue;
            }

            var channel = channels.FirstOrDefault(candidate => candidate.Number == channelNumber);
            if (channel is null)
            {
                // Unused recorder inputs may have no streaming profile but still appear in its input inventory.
                channel = new Channel { Number = channelNumber, Enabled = false };
                channels.Add(channel);
            }
            channel.Online = !string.Equals(status.ChildValue("online"), "false", StringComparison.OrdinalIgnoreCase);
        }
    }
}
