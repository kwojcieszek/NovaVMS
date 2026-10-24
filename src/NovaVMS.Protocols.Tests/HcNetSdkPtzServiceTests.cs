using System.Runtime.InteropServices;
using NovaVMS.Core.Exceptions;
using NovaVMS.Protocols.HcNetSdk;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class HcNetSdkPtzServiceTests
{
    [Theory]
    [InlineData(17, 33)]
    [InlineData(18, 34)]
    [InlineData(19, 35)]
    [InlineData(20, 36)]
    public async Task DigitalPtzMoveAndStopUseMappedSdkChannelAndSameSpeed(int number, int sdkNumber)
    {
        var api = new FakeApi();
        var ptz = new HcNetSdkPtzService(api);
        await ptz.MoveAsync(Session(), number, -56, 56, 0, default);
        await ptz.StopAsync(Session(), number, default);
        Assert.Equal(new[] { (7, sdkNumber, 25u, 0u, 4u), (7, sdkNumber, 25u, 1u, 4u) }, api.Calls);
    }

    [Theory]
    [InlineData(0, 56, 0, 21)]
    [InlineData(0, -56, 0, 22)]
    [InlineData(-56, 0, 0, 23)]
    [InlineData(56, 0, 0, 24)]
    [InlineData(56, 56, 0, 26)]
    [InlineData(-56, -56, 0, 27)]
    [InlineData(56, -56, 0, 28)]
    [InlineData(0, 0, 56, 11)]
    [InlineData(0, 0, -56, 12)]
    public async Task DirectionsAndZoomUseNativeCommands(int pan, int tilt, int zoom, uint command)
    {
        var api = new FakeApi();
        await new HcNetSdkPtzService(api).MoveAsync(Session(), 1, pan, tilt, zoom, default);
        Assert.Equal((7, 1, command, 0u, 4u), Assert.Single(api.Calls));
    }

    [Fact]
    public async Task FailedStopIsReportedAndCanBeRetried()
    {
        var api = new FakeApi();
        var ptz = new HcNetSdkPtzService(api);
        await ptz.MoveAsync(Session(), 17, 0, 0, 100, default);
        api.Success = false;
        await Assert.ThrowsAsync<DeviceCommunicationException>(() => ptz.StopAsync(Session(), 17, default));
        api.Success = true;
        await ptz.StopAsync(Session(), 17, default);
        Assert.Equal(3, api.Calls.Count);
        Assert.Equal(api.Calls[1], api.Calls[2]);
    }

    [Fact]
    public async Task FailedMoveDoesNotCreateAStopCommand()
    {
        var api = new FakeApi { Success = false };
        var ptz = new HcNetSdkPtzService(api);
        await Assert.ThrowsAsync<DeviceCommunicationException>(() => ptz.MoveAsync(Session(), 17, 56, 0, 0, default));
        await ptz.StopAsync(Session(), 17, default);
        Assert.Single(api.Calls);
    }

    [Fact]
    public async Task ZeroVectorStopsLastCommandAndCancellationDoesNotStartMovement()
    {
        var api = new FakeApi();
        var ptz = new HcNetSdkPtzService(api);
        await ptz.MoveAsync(Session(), 17, 56, 0, 0, default);
        await ptz.MoveAsync(Session(), 17, 0, 0, 0, default);
        Assert.Equal(1u, api.Calls[1].Stop);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ptz.MoveAsync(Session(), 17, 56, 0, 0, new CancellationToken(true)));
        Assert.Equal(2, api.Calls.Count);
    }

    private static HcNetSdkSession Session()
    {
        var buffer = Marshal.AllocHGlobal(HcNetSdkSession.DeviceInfoBufferSize);
        try
        {
            Marshal.Copy(new byte[HcNetSdkSession.DeviceInfoBufferSize], 0, buffer, HcNetSdkSession.DeviceInfoBufferSize);
            Marshal.WriteByte(buffer, 52, 16);
            Marshal.WriteByte(buffer, 53, 1);
            Marshal.WriteByte(buffer, 55, 4);
            Marshal.WriteByte(buffer, 66, 33);
            return new HcNetSdkSession(7, "recorder", 8000, buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private sealed class FakeApi : IHcNetSdkPtzApi
    {
        public bool Success = true;
        public List<(int User, int Channel, uint Command, uint Stop, uint Speed)> Calls { get; } = new();
        public bool Control(int userId, int channel, uint command, uint stop, uint speed)
        {
            Calls.Add((userId, channel, command, stop, speed));
            return Success;
        }
        public uint GetLastError() => 23;
    }
}
