namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>Controls PTZ over the recorder's native SDK channel, independently of ISAPI proxy support.</summary>
internal sealed class HcNetSdkPtzService(IHcNetSdkPtzApi api)
{
    private readonly Dictionary<int, (uint Command, uint Speed)> _active = new();

    public Task MoveAsync(HcNetSdkSession session, int channelNumber, int pan, int tilt, int zoom, CancellationToken token)
    {
        var command = SelectPtzCommand(pan, tilt, zoom);
        if (command == 0) return StopAsync(session, channelNumber, token);
        var magnitude = Math.Max(Math.Abs((long)pan), Math.Max(Math.Abs((long)tilt), Math.Abs((long)zoom)));
        var speed = (uint)Math.Clamp((int)Math.Ceiling(Math.Min(magnitude, 100) / 100.0 * HcNetSdkConstants.PtzMaxSpeed),
            (int)HcNetSdkConstants.PtzMinSpeed, (int)HcNetSdkConstants.PtzMaxSpeed);
        return Task.Run(() =>
        {
            Send(session, channelNumber, command, 0, speed);
            _active[channelNumber] = (command, speed);
        }, token);
    }

    public Task StopAsync(HcNetSdkSession session, int channelNumber, CancellationToken token)
    {
        return Task.Run(() =>
        {
            if (!_active.TryGetValue(channelNumber, out var move)) return;
            Send(session, channelNumber, move.Command, 1, move.Speed);
            _active.Remove(channelNumber);
        }, token);
    }

    private void Send(HcNetSdkSession session, int channelNumber, uint command, uint stop, uint speed)
    {
        // Read the SDK error on the same worker thread as the failed native call.
        if (api.Control(session.UserId, session.ToSdkChannel(channelNumber), command, stop, speed)) return;
        var code = api.GetLastError();
        if (HcNetSdkErrors.InvalidatesSession(code)) HcNetSdkSessionPool.Invalidate(session);
        throw HcNetSdkErrors.FromCode(code, session.Host, session.Port);
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

}

internal interface IHcNetSdkPtzApi
{
    bool Control(int userId, int channel, uint command, uint stop, uint speed);
    uint GetLastError();
}

internal sealed class HcNetSdkPtzApi : IHcNetSdkPtzApi
{
    public bool Control(int userId, int channel, uint command, uint stop, uint speed)
        => HcNetSdkNative.PtzControlWithSpeed(userId, channel, command, stop, speed);
    public uint GetLastError() => HcNetSdkNative.GetLastError();
}
