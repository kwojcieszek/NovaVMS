using System.Runtime.InteropServices;
using NovaVMS.Core.Exceptions;
using NovaVMS.Protocols.HcNetSdk;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class HcNetSdkRecordingServiceTests
{
    private static readonly DateTime Day = new(2026, 10, 20);

    [Fact]
    public async Task FindsAllRecordingTypesOnMappedDigitalChannelAndKeepsLocalTime()
    {
        var api = new FakeApi();
        api.Results.Enqueue((1002, default));
        api.Results.Enqueue((1000, File(12, 13)));
        api.Results.Enqueue((1000, File(8, 9)));
        api.Results.Enqueue((1000, File(8, 9)));
        api.Results.Enqueue((1003, default));
        var found = await new HcNetSdkRecordingService(api).SearchAsync(Session(), 3, Day, Day.AddDays(1), default);
        Assert.Equal(33, api.Condition.Channel); // Two analog inputs precede the IP inputs.
        Assert.Equal(0xffu, api.Condition.FileType);
        Assert.Equal(0xffu, api.Condition.IsLocked);
        Assert.Equal(Day, api.Condition.Start.ToDateTime());
        Assert.Equal(new[] { Day.AddHours(8), Day.AddHours(12) }, found.Select(segment => segment.Start));
        Assert.All(found, segment => { Assert.Equal(3, segment.ChannelNumber); Assert.Equal(DateTimeKind.Unspecified, segment.Start.Kind); });
        Assert.Equal(1, api.CloseCalls);
    }

    [Theory]
    [InlineData(1001)]
    [InlineData(1003)]
    public async Task EmptySearchClosesHandle(int status)
    {
        var api = new FakeApi();
        api.Results.Enqueue((status, default));
        Assert.Empty(await new HcNetSdkRecordingService(api).SearchAsync(Session(), 1, Day, Day.AddDays(1), default));
        Assert.Equal(1, api.CloseCalls);
    }

    [Fact]
    public async Task CancellationWhileFindingClosesHandle()
    {
        var api = new FakeApi();
        using var stop = new CancellationTokenSource();
        api.OnNext = stop.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new HcNetSdkRecordingService(api).SearchAsync(Session(), 1, Day, Day.AddDays(1), stop.Token));
        Assert.Equal(1, api.CloseCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReportsNativeErrorsAndReleasesOnlyOpenedHandles(bool openFails)
    {
        var api = new FakeApi { Handle = openFails ? -1 : 42 };
        api.Results.Enqueue((1004, default));
        await Assert.ThrowsAsync<DeviceCommunicationException>(() =>
            new HcNetSdkRecordingService(api).SearchAsync(Session(), 1, Day, Day.AddDays(1), default));
        Assert.Equal(openFails ? 0 : 1, api.CloseCalls);
    }

    [Fact]
    public void StructuresMatchSdkAbi()
    {
        Assert.Equal(24, Marshal.SizeOf<NetDvrTime>());
        Assert.Equal(96, Marshal.SizeOf<NetDvrFileCondition>());
        Assert.Equal(188, Marshal.SizeOf<NetDvrFindData>());
        Assert.Equal(48, Marshal.OffsetOf<NetDvrFileCondition>(nameof(NetDvrFileCondition.Start)).ToInt32());
        Assert.Equal(100, Marshal.OffsetOf<NetDvrFindData>(nameof(NetDvrFindData.Start)).ToInt32());
    }

    private static NetDvrFindData File(int start, int end) => new()
        { Start = NetDvrTime.From(Day.AddHours(start)), End = NetDvrTime.From(Day.AddHours(end)) };

    private static HcNetSdkSession Session()
    {
        var buffer = Marshal.AllocHGlobal(HcNetSdkSession.DeviceInfoBufferSize);
        try
        {
            Marshal.Copy(new byte[HcNetSdkSession.DeviceInfoBufferSize], 0, buffer, HcNetSdkSession.DeviceInfoBufferSize);
            Marshal.WriteByte(buffer, 52, 2);
            Marshal.WriteByte(buffer, 53, 1);
            Marshal.WriteByte(buffer, 55, 4);
            Marshal.WriteByte(buffer, 66, 33);
            return new HcNetSdkSession(7, "recorder", 8000, buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private sealed class FakeApi : IHcNetSdkRecordingApi
    {
        public Queue<(int Status, NetDvrFindData Data)> Results { get; } = new();
        public NetDvrFileCondition Condition;
        public int Handle = 42;
        public int CloseCalls;
        public Action? OnNext;
        public int Find(int userId, ref NetDvrFileCondition condition) { Condition = condition; return Handle; }
        public int Next(int handle, out NetDvrFindData data)
        {
            OnNext?.Invoke();
            var result = Results.Count > 0 ? Results.Dequeue() : (1002, default(NetDvrFindData));
            data = result.Item2;
            return result.Item1;
        }
        public bool Close(int handle) { CloseCalls++; return true; }
        public uint GetLastError() => 23;
    }
}
