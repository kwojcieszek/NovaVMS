using System.Diagnostics;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Common;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols.Onvif;

/// <summary>Profile G recording discovery, track history and replay URI negotiation.</summary>
internal sealed class OnvifRecordingService(Device device,
    Func<Uri, XElement, CancellationToken, Task<XElement>> call)
{
    private static readonly XNamespace Search = "http://www.onvif.org/ver10/search/wsdl";
    private static readonly XNamespace Replay = "http://www.onvif.org/ver10/replay/wsdl";
    private static readonly XNamespace Recording = "http://www.onvif.org/ver10/recording/wsdl";
    private static readonly XNamespace Schema = OnvifNamespaces.Schema;
    private Uri? _search, _replay, _recording;
    private List<XElement>? _recordings;

    public async Task<IReadOnlyList<RecordingSegment>> SearchAsync(int channel, string sourceToken,
        IReadOnlyList<string> profiles, DateTime from, DateTime to, CancellationToken token)
    {
        if (from >= to) throw new ArgumentException("Recording search start must precede its end.");
        var recordings = await ForChannelAsync(sourceToken, profiles, token).ConfigureAwait(false);
        var segments = new List<RecordingSegment>();
        foreach (var recording in recordings)
        {
            var tracks = recording.Elements().Where(e => e.Name.LocalName == "Track" && e.ChildValue("TrackType") == "Video")
                .Select(e => e.ChildValue("TrackToken")).ToHashSet();
            if (tracks.Count == 0) continue;
            var request = new XElement(Search + "FindEvents",
                new XElement(Search + "StartPoint", Utc(from)), new XElement(Search + "EndPoint", Utc(to)),
                new XElement(Search + "Scope", new XElement(Schema + "IncludedRecordings", recording.ChildValue("RecordingToken"))),
                new XElement(Search + "SearchFilter"), new XElement(Search + "IncludeStartState", true),
                new XElement(Search + "KeepAliveTime", "PT10S"));
            var events = await RunSearchAsync(request, "GetEventSearchResults", "Result", token).ConfigureAwait(false);
            segments.AddRange(BuildSegments(events, tracks, channel, from, to));
        }
        return segments.DistinctBy(s => (s.Start, s.End)).OrderBy(s => s.Start).ToList();
    }

    public async Task<VideoSource> GetSourceAsync(string sourceToken, IReadOnlyList<string> profiles,
        DateTime start, DateTime end, CancellationToken token)
    {
        var recordings = await ForChannelAsync(sourceToken, profiles, token).ConfigureAwait(false);
        var recording = recordings.FirstOrDefault(r => r.Elements().Any(t => t.Name.LocalName == "Track"
            && t.ChildValue("TrackType") == "Video" && ParseTime(t.ChildValue("DataFrom")) <= start
            && ParseTime(t.ChildValue("DataTo")) >= start));
        if (recording is null) throw new DeviceCommunicationException(ProtocolText.Get("OnvifNoRecordingAtTime"));
        var response = await call(_replay!, new XElement(Replay + "GetReplayUri",
            new XElement(Replay + "StreamSetup", new XElement(Schema + "Stream", "RTP-Unicast"),
                new XElement(Schema + "Transport", new XElement(Schema + "Protocol", "RTSP"))),
            new XElement(Replay + "RecordingToken", recording.ChildValue("RecordingToken"))), token).ConfigureAwait(false);
        if (!Uri.TryCreate(response.ChildValue("Uri"), UriKind.Absolute, out var reported) || reported.Scheme != "rtsp")
            throw new DeviceCommunicationException(ProtocolText.Get("OnvifNoMedia"));
        var remote = new UriBuilder(reported) { Host = device.Host, UserName = "", Password = "" }.Uri;
        return VideoSource.FromUri(() =>
        {
            var session = new OnvifReplayProxy(remote, device.Username, device.Password, start.ToUniversalTime(), end.ToUniversalTime());
            return (session.Uri, (IDisposable)session);
        });
    }

    private async Task<List<XElement>> ForChannelAsync(string source, IReadOnlyList<string> profiles, CancellationToken token)
    {
        await LoadAsync(token).ConfigureAwait(false);
        var recordings = _recordings!;
        var matched = recordings.Where(r => r.DescendantsByLocalName("Source").Any(s =>
            s.ChildValue("VideoSourceToken") == source || s.ChildValue("SourceId") == source
            || profiles.Contains(s.ChildValue("SourceId")))).ToList();
        if (matched.Count > 0 || recordings.Count == 0) return matched;
        if (_recording is not null)
        {
            var jobs = await call(_recording, new XElement(Recording + "GetRecordingJobs"), token).ConfigureAwait(false);
            var references = jobs.DescendantsByLocalName("JobConfiguration").Where(j => j.DescendantsByLocalName("SourceToken")
                .Any(s => s.Attribute("Type")?.Value == Schema.NamespaceName + "/Profile" && profiles.Contains(s.ChildValue("Token"))))
                .Select(j => j.ChildValue("RecordingToken")).ToHashSet();
            matched = recordings.Where(r => references.Contains(r.ChildValue("RecordingToken"))).ToList();
        }
        // Only an unambiguous single-camera/single-recording device can omit source mapping.
        if (matched.Count == 0 && device.Channels.Count == 1 && recordings.Count == 1) return recordings;
        if (matched.Count == 0) throw new NotSupportedException(ProtocolText.Get("OnvifRecordingChannelUnknown"));
        return matched;
    }

    private async Task LoadAsync(CancellationToken token)
    {
        if (_recordings is not null) return;
        var services = await call(new UriBuilder(device.UseHttps ? "https" : "http", device.Host, device.HttpPort,
            "onvif/device_service").Uri, new XElement(OnvifNamespaces.Device + "GetServices",
            new XElement(OnvifNamespaces.Device + "IncludeCapability", false)), token).ConfigureAwait(false);
        foreach (var service in services.DescendantsByLocalName("Service"))
        {
            if (!Uri.TryCreate(service.ChildValue("XAddr"), UriKind.Absolute, out var address)) continue;
            var uri = new UriBuilder(address) { Host = device.Host, Port = device.HttpPort }.Uri;
            switch (service.ChildValue("Namespace"))
            {
                case "http://www.onvif.org/ver10/search/wsdl": _search = uri; break;
                case "http://www.onvif.org/ver10/replay/wsdl": _replay = uri; break;
                case "http://www.onvif.org/ver10/recording/wsdl": _recording = uri; break;
            }
        }
        if (_search is null || _replay is null) throw new NotSupportedException(ProtocolText.Get("OnvifNoPlayback"));
        _recordings = await RunSearchAsync(new XElement(Search + "FindRecordings", new XElement(Search + "Scope"),
            new XElement(Search + "KeepAliveTime", "PT10S")), "GetRecordingSearchResults", "RecordingInformation", token).ConfigureAwait(false);
    }

    private async Task<List<XElement>> RunSearchAsync(XElement request, string resultsMethod, string resultName, CancellationToken token)
    {
        var response = await call(_search!, request, token).ConfigureAwait(false);
        var searchToken = response.ChildValue("SearchToken");
        if (string.IsNullOrEmpty(searchToken)) throw new DeviceCommunicationException(ProtocolText.Get("OnvifNoMedia"));
        try
        {
            var results = new List<XElement>();
            var started = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromMinutes(1))
            {
                token.ThrowIfCancellationRequested();
                var page = await call(_search!, new XElement(Search + resultsMethod,
                    new XElement(Search + "SearchToken", searchToken), new XElement(Search + "MinResults", 1),
                    new XElement(Search + "MaxResults", 100), new XElement(Search + "WaitTime", "PT1S")), token).ConfigureAwait(false);
                results.AddRange(page.DescendantsByLocalName(resultName));
                var state = page.DescendantsByLocalName("SearchState").FirstOrDefault()?.Value;
                if (state == "Completed") return results;
                if (state is not ("Searching" or "Queued")) throw new DeviceCommunicationException(ProtocolText.Get("OnvifNoMedia"));
                await Task.Delay(25, token).ConfigureAwait(false);
            }
            throw new DeviceCommunicationException(ProtocolText.Get("Timeout"));
        }
        finally
        {
            // Even cancellation must release the device-side search session.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await call(_search!, new XElement(Search + "EndSearch", new XElement(Search + "SearchToken", searchToken)), cleanup.Token).ConfigureAwait(false); }
            catch (Exception e) when (DeviceFailure.IsExpected(e) || e is OperationCanceledException) { }
        }
    }

    internal static List<RecordingSegment> BuildSegments(IEnumerable<XElement> events, HashSet<string> videoTracks,
        int channel, DateTime from, DateTime to)
    {
        var open = new Dictionary<string, DateTime>();
        var result = new List<RecordingSegment>();
        foreach (var item in events.OrderBy(e => ParseTime(e.ChildValue("Time"))))
        {
            var track = item.ChildValue("TrackToken");
            var value = item.DescendantsByLocalName("SimpleItem").FirstOrDefault(e => e.Attribute("Name")?.Value == "IsDataPresent")?.Attribute("Value")?.Value;
            if (!videoTracks.Contains(track) || value is not ("true" or "false" or "1" or "0")) continue;
            var time = ParseTime(item.ChildValue("Time"));
            if (time > to) continue;
            if (time < from) time = from;
            if (value is "true" or "1") open.TryAdd(track, time);
            else if (open.Remove(track, out var start) && start < time)
                result.Add(new RecordingSegment { ChannelNumber = channel, Start = start, End = time });
        }
        foreach (var start in open.Values.Where(start => start < to))
            result.Add(new RecordingSegment { ChannelNumber = channel, Start = start, End = to });
        return result;
    }

    private static string Utc(DateTime time) => XmlConvert.ToString(time.ToUniversalTime(), XmlDateTimeSerializationMode.Utc);
    private static DateTime ParseTime(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();
}
