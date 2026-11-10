namespace NovaVMS.Protocols.Rtsp;

/// <summary>Answer of an RTSP server: status code, headers and body.</summary>
internal sealed record RtspResponse(int Status, Dictionary<string, string> Headers, byte[] Body);
