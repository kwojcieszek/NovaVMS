using Avalonia;
using NovaVMS.Protocols.Rtsp;

namespace NovaVMS.Mobile.Native;

/// <summary>
/// Native view decoding pictures with the hardware decoder of the platform and showing them.
/// <see cref="Render"/> is called on the receiving thread of the stream, never on the interface thread.
/// </summary>
internal interface IVideoRenderer
{
    /// <summary>Hands one picture to the decoder; false while the decoder waits for a key frame or is not ready.</summary>
    bool Render(VideoAccessUnit picture);

    /// <summary>Decodes again from the next key frame, e.g. after pictures were skipped while paused.</summary>
    void RequireKeyFrame();

    /// <summary>Prepares for another stream; the shown picture stays where the platform can keep it until the first new one.</summary>
    void Restart();

    /// <summary>Removes the shown picture; called when the stream stops.</summary>
    void Clear();

    /// <summary>Stretches the picture to the view (true) or keeps the camera proportions (false).</summary>
    void SetFill(bool fill);

    /// <summary>Width divided by height to show the picture in when not stretched; null uses the picture size.</summary>
    void SetAspectRatio(double? ratio);

    /// <summary>
    /// Area the view may draw in, in Avalonia units of the top level, converted with <paramref name="scale"/> to pixels;
    /// null shows all of it. The view applies it at the position it is placed at, together with every move.
    /// </summary>
    void SetVisibleArea(Rect? area, double scale);

    /// <summary>Places Avalonia content in a transparent native layer above the picture; null removes it.</summary>
    void SetOverlay(Avalonia.Controls.Control? overlay);

    /// <summary>Shows or hides the overlay layer.</summary>
    void SetOverlayVisible(bool visible);

    /// <summary>Releases the decoder; the view is not used afterwards.</summary>
    void Detach();
}
