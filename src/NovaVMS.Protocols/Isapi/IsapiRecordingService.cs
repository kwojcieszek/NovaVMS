using System.Xml.Linq;
using NovaVMS.Core.Models;
using NovaVMS.Protocols.Common;

namespace NovaVMS.Protocols.Isapi;

/// <summary>
/// Searches recordings through ISAPI ContentMgmt (paged results).
/// </summary>
internal sealed class IsapiRecordingService
{
    private const int PageSize = 50;
    private const int MaxPages = 40;
    private const string MoreResultsStatus = "MORE";

    private readonly IIsapiTransport _transport;

    public IsapiRecordingService(IIsapiTransport transport)
    {
        _transport = transport;
    }

    public async Task<IReadOnlyList<RecordingSegment>> SearchAsync(int channelNumber, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var trackId = IsapiStreamId.Track(channelNumber);
        var searchId = Guid.NewGuid().ToString("D").ToUpperInvariant();
        var segments = new List<RecordingSegment>();
        var position = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            var request = BuildSearchRequest(searchId, trackId, from, to, position);
            var document = await _transport.PostXmlAsync("ISAPI/ContentMgmt/search", request, cancellationToken).ConfigureAwait(false);
            var root = document.Root;
            if (root is null)
            {
                break;
            }

            var matches = root.DescendantsByLocalName("searchMatchItem").ToList();
            foreach (var match in matches)
            {
                var segment = ParseMatch(match, channelNumber);
                if (segment is not null)
                {
                    segments.Add(segment);
                }
            }

            var status = root.ChildValue("responseStatusStrg");
            if (!string.Equals(status, MoreResultsStatus, StringComparison.OrdinalIgnoreCase) || matches.Count == 0)
            {
                break;
            }

            position += matches.Count;
        }

        return segments.OrderBy(segment => segment.Start).ToList();
    }

    private static XElement BuildSearchRequest(string searchId, int trackId, DateTime from, DateTime to, int position)
    {
        var ns = IsapiNamespace.Value;
        return new XElement(ns + "CMSearchDescription",
            new XAttribute("version", IsapiNamespace.Version),
            new XElement(ns + "searchID", searchId),
            new XElement(ns + "trackList",
                new XElement(ns + "trackID", trackId)),
            new XElement(ns + "timeSpanList",
                new XElement(ns + "timeSpan",
                    new XElement(ns + "startTime", IsapiTime.ToIso(from)),
                    new XElement(ns + "endTime", IsapiTime.ToIso(to)))),
            new XElement(ns + "maxResults", PageSize),
            // The "Postion" spelling matches the ISAPI specification.
            new XElement(ns + "searchResultPostion", position),
            new XElement(ns + "metadataList",
                new XElement(ns + "metadataDescriptor", "//recordType.meta.std-cgi.com")));
    }

    private static RecordingSegment? ParseMatch(XElement match, int channelNumber)
    {
        var timeSpan = match.DescendantsByLocalName("timeSpan").FirstOrDefault();
        if (timeSpan is null)
        {
            return null;
        }

        if (!IsapiTime.TryParse(timeSpan.ChildValue("startTime"), out var start))
        {
            return null;
        }

        if (!IsapiTime.TryParse(timeSpan.ChildValue("endTime"), out var end))
        {
            return null;
        }

        return new RecordingSegment
        {
            ChannelNumber = channelNumber,
            Start = start,
            End = end
        };
    }
}
