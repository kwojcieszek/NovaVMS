using System.Net;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Isapi;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// Client for Hikvision devices over the device network protocol (port 8000) using the vendor's HCNetSDK library.
/// Video and recording search use native SDK calls; configuration and PTZ use ISAPI requests
/// tunnelled through the same connection. Devices without ISAPI fall back to the data reported at login.
/// </summary>
public sealed class HcNetSdkDeviceClient : IDeviceClient, IDeviceConnectionClient
{
    private const int SnapshotBufferSize = 4 * 1024 * 1024;
    private const ushort CurrentResolution = 0xFF;

    private readonly Device _device;
    private readonly HcNetSdkIsapiTransport _transport;
    private readonly IsapiDeviceService _deviceService;
    private readonly IsapiPtzService _ptz;
    private readonly HcNetSdkRecordingService _recordings = new(new HcNetSdkRecordingApi());
    private readonly Dictionary<int, uint> _activePtzCommands = new();

    public HcNetSdkDeviceClient(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _transport = new HcNetSdkIsapiTransport(device);
        _deviceService = new IsapiDeviceService(_transport);
        _ptz = new IsapiPtzService(_transport, device);
    }

    public async Task<DeviceConnectionResult> TryConnectAsync(CancellationToken cancellationToken = default)
    {
        var result = await HcNetSdkSessionPool.TryAcquireAsync(_device, cancellationToken).ConfigureAwait(false);
        return new DeviceConnectionResult(result.Error);
    }

    public async Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _deviceService.GetDeviceInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DeviceCommunicationException exception) when (IsIsapiUnavailable(exception))
        {
            var session = await HcNetSdkSessionPool.AcquireAsync(_device, cancellationToken).ConfigureAwait(false);
            return new DeviceInfo
            {
                SerialNumber = session.SerialNumber,
                Type = GuessDeviceType(session)
            };
        }
    }

    public async Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _deviceService.GetChannelsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DeviceCommunicationException exception) when (IsIsapiUnavailable(exception))
        {
            var session = await HcNetSdkSessionPool.AcquireAsync(_device, cancellationToken).ConfigureAwait(false);
            return Enumerable.Range(1, session.ChannelCount)
                .Select(number => new Channel { Number = number })
                .ToList();
        }
    }

    public async Task<byte[]> GetSnapshotAsync(int channelNumber, CancellationToken cancellationToken = default)
    {
        var session = await HcNetSdkSessionPool.AcquireAsync(_device, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var parameters = new NetDvrJpegParameters
            {
                PictureSize = CurrentResolution,
                PictureQuality = 0
            };
            var buffer = new byte[SnapshotBufferSize];
            uint length = 0;
            if (!HcNetSdkNative.CaptureJpegPicture(session.UserId, session.ToSdkChannel(channelNumber), ref parameters, buffer, (uint)buffer.Length, ref length))
            {
                throw HcNetSdkErrors.LastError(_device.Host, _device.SdkPort);
            }

            return buffer.AsSpan(0, (int)Math.Min(length, (uint)buffer.Length)).ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

    public VideoSource GetLiveSource(int channelNumber, StreamType streamType)
    {
        EnsureAvailable();
        EnsureStreamDecoder();
        // PlayCtrl handles the recorder's native video timing for MAIN and SUB alike; LibVLC's
        // program-stream demuxer stops after the first frame on some recorders. Keep the
        // multiplexed stream when audio is requested; the decoded YUV path carries video only.
        return VideoSource.FromStream(withAudio => HcNetSdkVideoStream.OpenLive(_device, channelNumber, streamType,
            decodeVideo: !withAudio));
    }

    public async Task<IReadOnlyList<RecordingSegment>> SearchRecordingsAsync(int channelNumber, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var session = await HcNetSdkSessionPool.AcquireAsync(_device, cancellationToken).ConfigureAwait(false);
        return await _recordings.SearchAsync(session, channelNumber, from, to, cancellationToken).ConfigureAwait(false);
    }

    public VideoSource GetPlaybackSource(int channelNumber, DateTime start, DateTime end)
    {
        EnsureAvailable();
        EnsureStreamDecoder();
        return VideoSource.FromStream(() => HcNetSdkVideoStream.OpenPlayback(_device, channelNumber, start, end));
    }

    public async Task PtzMoveAsync(int channelNumber, int pan, int tilt, int zoom, CancellationToken cancellationToken = default)
    {
        try
        {
            await _ptz.MoveAsync(channelNumber, pan, tilt, zoom, cancellationToken).ConfigureAwait(false);
        }
        catch (DeviceCommunicationException exception) when (IsIsapiUnavailable(exception))
        {
            await SendPtzCommandAsync(channelNumber, pan, tilt, zoom, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task PtzStopAsync(int channelNumber, CancellationToken cancellationToken = default)
    {
        uint command;
        lock (_activePtzCommands)
        {
            if (!_activePtzCommands.Remove(channelNumber, out command))
            {
                command = 0;
            }
        }

        if (command == 0)
        {
            await _ptz.StopAsync(channelNumber, cancellationToken).ConfigureAwait(false);
            return;
        }

        var session = await HcNetSdkSessionPool.AcquireAsync(_device, cancellationToken).ConfigureAwait(false);
        await Task.Run(() => HcNetSdkNative.PtzControlWithSpeed(session.UserId, session.ToSdkChannel(channelNumber), command, 1, HcNetSdkConstants.PtzMinSpeed), cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<PtzPreset>> GetPtzPresetsAsync(int channelNumber, CancellationToken cancellationToken = default)
    {
        return _ptz.GetPresetsAsync(channelNumber, cancellationToken);
    }

    public Task GotoPtzPresetAsync(int channelNumber, PtzPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return _ptz.GotoPresetAsync(channelNumber, preset.Id, cancellationToken);
    }

    public void Dispose()
    {
        _transport.Dispose();
    }

    /// <summary>
    /// Loads the SDK before a stream is handed to the player, so a missing library is reported immediately.
    /// </summary>
    private void EnsureStreamDecoder()
    {
        if (!string.IsNullOrEmpty(_device.StreamEncryptionKey))
        {
            var bytes = HikvisionStreamDecoder.EncodeKey(_device.StreamEncryptionKey);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            _ = PlayCtrlApi.Get();
        }
    }

    private static void EnsureAvailable()
    {
        try
        {
            HcNetSdkRuntime.EnsureInitialized();
        }
        catch (DeviceCommunicationException exception)
        {
            throw new NotSupportedException(exception.Message, exception);
        }
    }

    /// <summary>
    /// The tunnel reports requests rejected by the device as HTTP 404 (see HcNetSdkIsapiTransport);
    /// credential and network errors are real failures and are not hidden by the fallback.
    /// </summary>
    private static bool IsIsapiUnavailable(DeviceCommunicationException exception)
    {
        return exception.StatusCode == HttpStatusCode.NotFound;
    }

    private async Task SendPtzCommandAsync(int channelNumber, int pan, int tilt, int zoom, CancellationToken cancellationToken)
    {
        var command = SelectPtzCommand(pan, tilt, zoom);
        if (command == 0)
        {
            await PtzStopAsync(channelNumber, cancellationToken).ConfigureAwait(false);
            return;
        }

        var magnitude = Math.Max(Math.Abs(pan), Math.Max(Math.Abs(tilt), Math.Abs(zoom)));
        var speed = (uint)Math.Clamp((int)Math.Ceiling(magnitude / 100.0 * HcNetSdkConstants.PtzMaxSpeed), (int)HcNetSdkConstants.PtzMinSpeed, (int)HcNetSdkConstants.PtzMaxSpeed);
        var session = await HcNetSdkSessionPool.AcquireAsync(_device, cancellationToken).ConfigureAwait(false);
        var sdkChannel = session.ToSdkChannel(channelNumber);

        var succeeded = await Task.Run(() => HcNetSdkNative.PtzControlWithSpeed(session.UserId, sdkChannel, command, 0, speed), cancellationToken).ConfigureAwait(false);
        if (!succeeded)
        {
            throw HcNetSdkErrors.LastError(_device.Host, _device.SdkPort);
        }

        lock (_activePtzCommands)
        {
            _activePtzCommands[channelNumber] = command;
        }
    }

    private static uint SelectPtzCommand(int pan, int tilt, int zoom)
    {
        if (zoom != 0)
        {
            return zoom > 0 ? HcNetSdkConstants.PtzZoomIn : HcNetSdkConstants.PtzZoomOut;
        }

        if (tilt > 0)
        {
            return pan < 0 ? HcNetSdkConstants.PtzUpLeft : pan > 0 ? HcNetSdkConstants.PtzUpRight : HcNetSdkConstants.PtzTiltUp;
        }

        if (tilt < 0)
        {
            return pan < 0 ? HcNetSdkConstants.PtzDownLeft : pan > 0 ? HcNetSdkConstants.PtzDownRight : HcNetSdkConstants.PtzTiltDown;
        }

        if (pan != 0)
        {
            return pan < 0 ? HcNetSdkConstants.PtzPanLeft : HcNetSdkConstants.PtzPanRight;
        }

        return 0;
    }

    private static DeviceType GuessDeviceType(HcNetSdkSession session)
    {
        if (session.ChannelCount <= 1)
        {
            return DeviceType.IpCamera;
        }

        return session.AnalogChannelCount > 0 ? DeviceType.Dvr : DeviceType.Nvr;
    }
}
