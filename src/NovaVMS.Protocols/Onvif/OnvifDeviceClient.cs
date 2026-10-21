using System.Globalization;
using System.Xml.Linq;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Common;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols.Onvif;

/// <summary>
/// Client for cameras of other manufacturers using the ONVIF standard (Profile S: device information,
/// media profiles, RTSP stream addresses and PTZ). Each video source is one channel; its highest
/// resolution profile is the main stream and the next one the sub stream. Stream addresses are stored
/// on the channel during synchronization, so starting live view needs no request to the device.
/// Recording search and replay use Profile G when advertised by the device.
/// </summary>
public sealed class OnvifDeviceClient : IDeviceClient
{
    private const double VelocityScale = 100.0;

    private readonly Device _device;
    private readonly OnvifSoapClient _soap;
    private readonly OnvifRecordingService _recordings;
    private Uri? _mediaServiceUri;
    private Uri? _ptzServiceUri;
    private bool _servicesLoaded;

    public OnvifDeviceClient(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _soap = new OnvifSoapClient(device);
        _recordings = new OnvifRecordingService(device, _soap.CallAsync);
    }

    public async Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        var response = await _soap.CallAsync(_soap.DeviceServiceUri, new XElement(OnvifNamespaces.Device + "GetDeviceInformation"), cancellationToken).ConfigureAwait(false);
        var profiles = await GetProfilesAsync(cancellationToken).ConfigureAwait(false);
        var sourceCount = profiles.Select(profile => profile.SourceToken).Distinct().Count();

        var manufacturer = response.ChildValue("Manufacturer");
        var model = response.ChildValue("Model");
        return new DeviceInfo
        {
            DeviceName = $"{manufacturer} {model}".Trim(),
            Model = model,
            SerialNumber = response.ChildValue("SerialNumber"),
            FirmwareVersion = response.ChildValue("FirmwareVersion"),
            Type = sourceCount > 1 ? DeviceType.Nvr : DeviceType.IpCamera
        };
    }

    public async Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await GetProfilesAsync(cancellationToken).ConfigureAwait(false);
        var channels = new List<Channel>();

        var sources = profiles
            .GroupBy(profile => profile.SourceToken)
            .ToList();

        for (var index = 0; index < sources.Count; index++)
        {
            var ordered = sources[index].OrderByDescending(profile => profile.PixelCount).ToList();
            var main = ordered[0];
            var sub = ordered.Count > 1 ? ordered[1] : main;
            var ptzProfile = ordered.FirstOrDefault(profile => profile.HasPtz) ?? main;

            var mainUri = await GetStreamUriAsync(main.Token, cancellationToken).ConfigureAwait(false);
            var subUri = ReferenceEquals(sub, main) ? mainUri : await GetStreamUriAsync(sub.Token, cancellationToken).ConfigureAwait(false);

            channels.Add(new Channel
            {
                Number = index + 1,
                MainStreamUri = mainUri,
                MainAspectRatio = main.Width > 0 && main.Height > 0
                    ? VideoDisplayGeometry.FromDimensions((uint)main.Width, (uint)main.Height) : null,
                SubStreamUri = subUri,
                ProfileToken = ptzProfile.Token
            });
        }

        return channels;
    }

    public async Task<byte[]> GetSnapshotAsync(int channelNumber, CancellationToken cancellationToken = default)
    {
        var channel = GetChannel(channelNumber);
        var mediaUri = await GetMediaServiceUriAsync(cancellationToken).ConfigureAwait(false);
        var request = new XElement(OnvifNamespaces.Media + "GetSnapshotUri",
            new XElement(OnvifNamespaces.Media + "ProfileToken", channel.ProfileToken));
        var response = await _soap.CallAsync(mediaUri, request, cancellationToken).ConfigureAwait(false);
        var snapshotUri = response.DescendantsByLocalName("Uri").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrEmpty(snapshotUri))
        {
            throw new DeviceCommunicationException(ProtocolText.Get("OnvifNoMedia"));
        }

        return await _soap.DownloadAsync(Rebase(new Uri(snapshotUri)), cancellationToken).ConfigureAwait(false);
    }

    public VideoSource GetLiveSource(int channelNumber, StreamType streamType)
    {
        var channel = _device.Channels.FirstOrDefault(candidate => candidate.Number == channelNumber);
        var address = streamType == StreamType.Main
            ? channel?.MainStreamUri
            : channel?.SubStreamUri ?? channel?.MainStreamUri;

        if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            throw new NotSupportedException(ProtocolText.Format("OnvifNoStream", channelNumber));
        }

        // Cached ONVIF addresses can have been discovered using the other network address.
        uri = new UriBuilder(uri) { Host = _device.Host }.Uri;
        return string.IsNullOrEmpty(_device.StreamEncryptionKey) ? VideoSource.FromUri(uri)
            : NovaVMS.Protocols.HcNetSdk.HikvisionRtspVideoStream.CreateSource(uri, _device, playback: false);
    }

    public async Task<IReadOnlyList<RecordingSegment>> SearchRecordingsAsync(int channelNumber, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var (source, profiles) = await GetRecordingProfilesAsync(channelNumber, cancellationToken).ConfigureAwait(false);
        return await _recordings.SearchAsync(channelNumber, source, profiles, from, to, cancellationToken).ConfigureAwait(false);
    }

    public VideoSource GetPlaybackSource(int channelNumber, DateTime start, DateTime end)
    {
        return GetPlaybackSourceAsync(channelNumber, start, end).GetAwaiter().GetResult();
    }

    public async Task<VideoSource> GetPlaybackSourceAsync(int channelNumber, DateTime start, DateTime end, CancellationToken cancellationToken = default)
    {
        var (source, profiles) = await GetRecordingProfilesAsync(channelNumber, cancellationToken).ConfigureAwait(false);
        return await _recordings.GetSourceAsync(source, profiles, start, end, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string Source, IReadOnlyList<string> Profiles)> GetRecordingProfilesAsync(int channelNumber, CancellationToken cancellationToken)
    {
        var configured = GetProfileToken(channelNumber);
        var profiles = await GetProfilesAsync(cancellationToken).ConfigureAwait(false);
        var source = profiles.FirstOrDefault(profile => profile.Token == configured)?.SourceToken
            ?? throw new NotSupportedException(ProtocolText.Format("OnvifNoStream", channelNumber));
        return (source, profiles.Where(profile => profile.SourceToken == source).Select(profile => profile.Token).ToList());
    }

    public async Task PtzMoveAsync(int channelNumber, int pan, int tilt, int zoom, CancellationToken cancellationToken = default)
    {
        if (pan == 0 && tilt == 0 && zoom == 0)
        {
            await PtzStopAsync(channelNumber, cancellationToken).ConfigureAwait(false);
            return;
        }

        var schema = OnvifNamespaces.Schema;
        var velocity = new XElement(OnvifNamespaces.Ptz + "Velocity");
        if (pan != 0 || tilt != 0)
        {
            velocity.Add(new XElement(schema + "PanTilt",
                new XAttribute("x", ToVelocity(pan)),
                new XAttribute("y", ToVelocity(tilt))));
        }

        if (zoom != 0)
        {
            velocity.Add(new XElement(schema + "Zoom", new XAttribute("x", ToVelocity(zoom))));
        }

        var request = new XElement(OnvifNamespaces.Ptz + "ContinuousMove",
            new XElement(OnvifNamespaces.Ptz + "ProfileToken", GetProfileToken(channelNumber)),
            velocity);
        await CallPtzAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task PtzStopAsync(int channelNumber, CancellationToken cancellationToken = default)
    {
        var request = new XElement(OnvifNamespaces.Ptz + "Stop",
            new XElement(OnvifNamespaces.Ptz + "ProfileToken", GetProfileToken(channelNumber)),
            new XElement(OnvifNamespaces.Ptz + "PanTilt", "true"),
            new XElement(OnvifNamespaces.Ptz + "Zoom", "true"));
        return CallPtzAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<PtzPreset>> GetPtzPresetsAsync(int channelNumber, CancellationToken cancellationToken = default)
    {
        var request = new XElement(OnvifNamespaces.Ptz + "GetPresets",
            new XElement(OnvifNamespaces.Ptz + "ProfileToken", GetProfileToken(channelNumber)));
        var response = await CallPtzAsync(request, cancellationToken).ConfigureAwait(false);

        var presets = new List<PtzPreset>();
        foreach (var element in response.DescendantsByLocalName("Preset"))
        {
            var token = element.Attribute("token")?.Value ?? string.Empty;
            if (token.Length == 0)
            {
                continue;
            }

            var id = int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : presets.Count + 1;
            presets.Add(new PtzPreset
            {
                Id = id,
                Token = token,
                Name = element.ChildValue("Name")
            });
        }

        return presets.OrderBy(preset => preset.Id).ToList();
    }

    public Task GotoPtzPresetAsync(int channelNumber, PtzPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var request = new XElement(OnvifNamespaces.Ptz + "GotoPreset",
            new XElement(OnvifNamespaces.Ptz + "ProfileToken", GetProfileToken(channelNumber)),
            new XElement(OnvifNamespaces.Ptz + "PresetToken", preset.Token));
        return CallPtzAsync(request, cancellationToken);
    }

    public void Dispose()
    {
        _soap.Dispose();
    }

    private static string ToVelocity(int value)
    {
        return (Math.Clamp(value, -100, 100) / VelocityScale).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private Channel GetChannel(int channelNumber)
    {
        var channel = _device.Channels.FirstOrDefault(candidate => candidate.Number == channelNumber);
        if (channel is null || string.IsNullOrEmpty(channel.ProfileToken))
        {
            throw new NotSupportedException(ProtocolText.Format("OnvifNoStream", channelNumber));
        }

        return channel;
    }

    private string GetProfileToken(int channelNumber)
    {
        return GetChannel(channelNumber).ProfileToken!;
    }

    private async Task<XElement> CallPtzAsync(XElement request, CancellationToken cancellationToken)
    {
        await LoadServicesAsync(cancellationToken).ConfigureAwait(false);
        if (_ptzServiceUri is null)
        {
            throw new NotSupportedException(ProtocolText.Get("OnvifNoPtz"));
        }

        return await _soap.CallAsync(_ptzServiceUri, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Uri> GetMediaServiceUriAsync(CancellationToken cancellationToken)
    {
        await LoadServicesAsync(cancellationToken).ConfigureAwait(false);
        if (_mediaServiceUri is null)
        {
            throw new DeviceCommunicationException(ProtocolText.Get("OnvifNoMedia"));
        }

        return _mediaServiceUri;
    }

    private async Task LoadServicesAsync(CancellationToken cancellationToken)
    {
        if (_servicesLoaded)
        {
            return;
        }

        var request = new XElement(OnvifNamespaces.Device + "GetCapabilities",
            new XElement(OnvifNamespaces.Device + "Category", "All"));
        var response = await _soap.CallAsync(_soap.DeviceServiceUri, request, cancellationToken).ConfigureAwait(false);

        _mediaServiceUri = ReadServiceAddress(response, "Media");
        _ptzServiceUri = ReadServiceAddress(response, "PTZ");
        _servicesLoaded = true;
    }

    private Uri? ReadServiceAddress(XElement capabilities, string service)
    {
        var address = capabilities.DescendantsByLocalName(service).FirstOrDefault()?.ChildValue("XAddr");
        if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return Rebase(uri);
    }

    /// <summary>
    /// Devices behind NAT often report their internal address; keep the path but use the configured host and port.
    /// </summary>
    private Uri Rebase(Uri reported)
    {
        var builder = new UriBuilder(reported)
        {
            Host = _device.Host,
            Port = _device.HttpPort
        };
        return builder.Uri;
    }

    private async Task<IReadOnlyList<OnvifProfile>> GetProfilesAsync(CancellationToken cancellationToken)
    {
        var mediaUri = await GetMediaServiceUriAsync(cancellationToken).ConfigureAwait(false);
        var response = await _soap.CallAsync(mediaUri, new XElement(OnvifNamespaces.Media + "GetProfiles"), cancellationToken).ConfigureAwait(false);

        var profiles = new List<OnvifProfile>();
        foreach (var element in response.DescendantsByLocalName("Profiles"))
        {
            var token = element.Attribute("token")?.Value;
            var source = element.Elements().FirstOrDefault(child => child.Name.LocalName == "VideoSourceConfiguration");
            var encoder = element.Elements().FirstOrDefault(child => child.Name.LocalName == "VideoEncoderConfiguration");
            if (string.IsNullOrEmpty(token) || source is null || encoder is null)
            {
                continue;
            }

            var resolution = encoder.DescendantsByLocalName("Resolution").FirstOrDefault();
            _ = int.TryParse(resolution?.ChildValue("Width"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width);
            _ = int.TryParse(resolution?.ChildValue("Height"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var height);
            var hasPtz = element.Elements().Any(child => child.Name.LocalName == "PTZConfiguration");

            profiles.Add(new OnvifProfile(token, source.ChildValue("SourceToken"), width, height, hasPtz));
        }

        if (profiles.Count == 0)
        {
            throw new DeviceCommunicationException(ProtocolText.Get("OnvifNoMedia"));
        }

        return profiles;
    }

    private async Task<string> GetStreamUriAsync(string profileToken, CancellationToken cancellationToken)
    {
        var mediaUri = await GetMediaServiceUriAsync(cancellationToken).ConfigureAwait(false);
        var schema = OnvifNamespaces.Schema;
        var request = new XElement(OnvifNamespaces.Media + "GetStreamUri",
            new XElement(OnvifNamespaces.Media + "StreamSetup",
                new XElement(schema + "Stream", "RTP-Unicast"),
                new XElement(schema + "Transport",
                    new XElement(schema + "Protocol", "RTSP"))),
            new XElement(OnvifNamespaces.Media + "ProfileToken", profileToken));

        var response = await _soap.CallAsync(mediaUri, request, cancellationToken).ConfigureAwait(false);
        var address = response.DescendantsByLocalName("Uri").FirstOrDefault()?.Value.Trim();
        if (string.IsNullOrEmpty(address) || !Uri.TryCreate(address, UriKind.Absolute, out var streamUri))
        {
            throw new DeviceCommunicationException(ProtocolText.Get("OnvifNoMedia"));
        }

        // The RTSP port may differ from the HTTP port; only the host is replaced for devices behind NAT.
        return new UriBuilder(streamUri) { Host = _device.Host }.Uri.ToString();
    }
}
