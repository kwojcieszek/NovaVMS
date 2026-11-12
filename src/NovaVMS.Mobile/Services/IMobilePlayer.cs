using Avalonia.Controls;
using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Services;

/// <summary>Platform video player hosted inside the mobile shell.</summary>
public interface IMobilePlayer : IAsyncDisposable
{
    Control View { get; }

    /// <summary>False when the player shows only the picture; the sound switch is hidden then.</summary>
    bool SupportsAudio => true;

    event EventHandler<string>? StatusChanged;

    /// <summary>
    /// True while a started stream connects and waits for its first picture; false once the picture is shown or the
    /// stream stops or fails. Raised on any thread.
    /// </summary>
    event EventHandler<bool>? ConnectingChanged;

    Task PlayAsync(VideoSource source, Device device, PlaybackOptions options, CancellationToken cancellationToken = default);

    Task StopAsync();

    void SetPaused(bool paused);

    void SetMuted(bool muted);

    /// <summary>Stretches the picture to the shape of the view instead of keeping the camera proportions.</summary>
    void SetFillArea(bool fill);

    /// <summary>
    /// Avalonia content shown above the picture, e.g. the playback menu of a full screen camera; null removes it.
    /// Native video is drawn above the Avalonia window, so the player places the content in its own native layer.
    /// </summary>
    void SetOverlay(Control? overlay)
    {
    }

    /// <summary>Shows or hides the overlay; a hidden overlay lets touches through to the picture.</summary>
    void SetOverlayVisible(bool visible)
    {
    }
}
