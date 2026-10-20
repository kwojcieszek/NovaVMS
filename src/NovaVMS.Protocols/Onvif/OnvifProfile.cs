namespace NovaVMS.Protocols.Onvif;

/// <summary>
/// ONVIF media profile: one encoder configuration of a video source.
/// </summary>
internal sealed record OnvifProfile(string Token, string SourceToken, int Width, int Height, bool HasPtz)
{
    public int PixelCount => Width * Height;
}
