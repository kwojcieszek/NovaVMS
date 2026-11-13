namespace NovaVMS.Protocols.Rtsp;

/// <summary>Answer of an RTSP server: status code, headers and body.</summary>
internal sealed record RtspResponse
{
    public int Status { get; init; }

    public Dictionary<string, string> Headers { get; init; }

    public byte[] Body { get; init; }

    public RtspResponse(int status, Dictionary<string, string> headers, byte[] body)
    {
        Status = status;
        Headers = headers;
        Body = body;
    }
}
