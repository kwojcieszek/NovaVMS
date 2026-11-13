namespace NovaVMS.Protocols.Onvif;

/// <summary>
/// ONVIF media profile: one encoder configuration of a video source.
/// </summary>
internal sealed record OnvifProfile
{
    public string Token { get; init; }

    public string SourceToken { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public bool HasPtz { get; init; }

    public int PixelCount => Width * Height;

    public OnvifProfile(string token, string sourceToken, int width, int height, bool hasPtz)
    {
        Token = token;
        SourceToken = sourceToken;
        Width = width;
        Height = height;
        HasPtz = hasPtz;
    }
}
