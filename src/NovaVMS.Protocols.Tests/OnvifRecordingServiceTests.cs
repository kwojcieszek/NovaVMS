using System.Xml.Linq;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Common;
using NovaVMS.Protocols.Onvif;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class OnvifRecordingServiceTests
{
    private static readonly DateTime Day = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Local);
    private static readonly XNamespace Search = "http://www.onvif.org/ver10/search/wsdl";
    private static readonly XNamespace Schema = OnvifNamespaces.Schema;

    [Fact]
    public async Task SearchesPagedHistoryForSelectedSourceWithoutFillingRecordingGaps()
    {
        var requests = new List<XElement>();
        var page = 0;
        var service = new OnvifRecordingService(Device(), (uri, request, token) =>
        {
            requests.Add(request);
            Assert.Equal("recorder", uri.Host);
            return Task.FromResult(request.Name.LocalName switch
            {
                "GetServices" => Services(),
                "FindRecordings" => Token("recordings"),
                "GetRecordingSearchResults" => RecordingPage(),
                "FindEvents" => Token("history"),
                "GetEventSearchResults" => ++page == 1
                    ? Events("Searching", Event("video", 0, true), Event("audio", 0, true), Event("video", 2, false))
                    : Events("Completed", Event("video", 4, true), Event("video", 5, false)),
                "EndSearch" => new XElement(Search + "EndSearchResponse"),
                _ => throw new InvalidOperationException(request.Name.LocalName)
            });
        });
        var found = await service.SearchAsync(2, "source2", ["profile2"], Day, Day.AddHours(8), default);
        Assert.Equal(2, found.Count);
        Assert.Equal(Day, found[0].Start);
        Assert.Equal(Day.AddHours(2), found[0].End);
        Assert.Equal(Day.AddHours(4), found[1].Start);
        Assert.Equal(Day.AddHours(5), found[1].End);
        Assert.All(found, segment => Assert.Equal(2, segment.ChannelNumber));
        var eventsRequest = Assert.Single(requests, r => r.Name.LocalName == "FindEvents");
        Assert.Equal("recording2", eventsRequest.DescendantsByLocalName("IncludedRecordings").Single().Value);
        Assert.Equal("true", eventsRequest.ChildValue("IncludeStartState"));
        Assert.Equal(2, requests.Count(r => r.Name.LocalName == "EndSearch"));
    }

    [Fact]
    public async Task NegotiatesReplayForCorrectRecordingAndKeepsSourceOwnedByPlayer()
    {
        var service = new OnvifRecordingService(Device(), (_, request, _) => Task.FromResult(request.Name.LocalName switch
        {
            "GetServices" => Services(),
            "FindRecordings" => Token("recordings"),
            "GetRecordingSearchResults" => RecordingPage(),
            "EndSearch" => new XElement("EndSearchResponse"),
            "GetReplayUri" => Replay(request),
            _ => throw new InvalidOperationException(request.Name.LocalName)
        }));
        var source = await service.GetSourceAsync("source2", ["profile2"], Day.AddHours(1), Day.AddHours(2), default);
        var uri = source.OpenUri(out var lifetime);
        Assert.Equal("127.0.0.1", uri.Host);
        Assert.Equal("/archive/two", uri.AbsolutePath);
        Assert.Empty(uri.UserInfo);
        var proxy = Assert.IsType<OnvifReplayProxy>(lifetime);
        proxy.Dispose();
        await proxy.Completion.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task MissingProfileGIsReportedWithoutSwitchingProtocol()
    {
        var service = new OnvifRecordingService(Device(), (_, request, _) =>
        {
            Assert.Equal("GetServices", request.Name.LocalName);
            return Task.FromResult(new XElement("GetServicesResponse"));
        });
        await Assert.ThrowsAsync<NotSupportedException>(() => service.SearchAsync(2, "source2", ["profile2"], Day, Day.AddHours(1), default));
    }

    [Fact]
    public async Task ReplayConnectsToSecondaryRtspPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var device = Device();
            device.SecondaryHost = "127.0.0.1";
            device.SecondaryRtspPort = port;
            var service = new OnvifRecordingService(device.ForSecondaryAddress(), (_, request, _) => Task.FromResult(request.Name.LocalName switch
            {
                "GetServices" => Services(),
                "FindRecordings" => Token("recordings"),
                "GetRecordingSearchResults" => RecordingPage(),
                "EndSearch" => new XElement("EndSearchResponse"),
                "GetReplayUri" => Replay(request),
                _ => throw new InvalidOperationException(request.Name.LocalName)
            }));
            var source = await service.GetSourceAsync("source2", ["profile2"], Day.AddHours(1), Day.AddHours(2), default);
            var uri = source.OpenUri(out var lifetime);
            using var session = lifetime;
            using var player = new System.Net.Sockets.TcpClient();
            await player.ConnectAsync(uri.Host, uri.Port);
            using var remote = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var proxy = Assert.IsType<OnvifReplayProxy>(lifetime);
            proxy.Dispose();
            await proxy.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task CancellationEndsDeviceSideSearch()
    {
        using var stop = new CancellationTokenSource();
        var ended = false;
        var service = new OnvifRecordingService(Device(), (_, request, token) =>
        {
            switch (request.Name.LocalName)
            {
                case "GetServices": return Task.FromResult(Services());
                case "FindRecordings": stop.Cancel(); return Task.FromResult(Token("recordings"));
                case "EndSearch": ended = true; Assert.False(token.IsCancellationRequested); return Task.FromResult(new XElement("EndSearchResponse"));
                default: throw new InvalidOperationException();
            }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SearchAsync(2, "source2", ["profile2"], Day, Day.AddHours(1), stop.Token));
        Assert.True(ended);
    }

    private static Device Device() => new() { Host = "recorder", Protocol = DeviceProtocol.Onvif,
        Channels = [new Channel { Number = 1 }, new Channel { Number = 2 }] };
    private static XElement Services() => new("GetServicesResponse", new[] { "search", "replay", "recording" }.Select(name =>
        new XElement("Service", new XElement("Namespace", $"http://www.onvif.org/ver10/{name}/wsdl"),
            new XElement("XAddr", $"http://internal/onvif/{name}"))));
    private static XElement Token(string token) => new("Response", new XElement("SearchToken", token));
    private static XElement RecordingPage() => new("Response", new XElement("ResultList", new XElement("SearchState", "Completed"),
        new[] { 1, 2 }.Select(i => new XElement("RecordingInformation", new XElement("RecordingToken", $"recording{i}"),
            new XElement("Source", new XElement("VideoSourceToken", $"source{i}")),
            new XElement("Track", new XElement("TrackToken", "video"), new XElement("TrackType", "Video"),
                new XElement("DataFrom", Day.ToUniversalTime().ToString("O")), new XElement("DataTo", Day.AddDays(1).ToUniversalTime().ToString("O")))))));
    private static XElement Event(string track, int hour, bool present) => new(Schema + "Result",
        new XElement(Schema + "TrackToken", track), new XElement(Schema + "Time", Day.AddHours(hour).ToUniversalTime().ToString("O")),
        new XElement(Schema + "Event", new XElement(Schema + "SimpleItem", new XAttribute("Name", "IsDataPresent"), new XAttribute("Value", present))));
    private static XElement Events(string state, params XElement[] events) => new("Response",
        new XElement("ResultList", new XElement("SearchState", state), events));
    private static XElement Replay(XElement request)
    {
        Assert.Equal("recording2", request.ChildValue("RecordingToken"));
        Assert.Equal("RTSP", request.DescendantsByLocalName("Protocol").Single().Value);
        return new XElement("GetReplayUriResponse", new XElement("Uri", "rtsp://internal/archive/two"));
    }
}
