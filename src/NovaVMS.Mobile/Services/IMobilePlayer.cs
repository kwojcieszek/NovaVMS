using Avalonia.Controls;
using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Services;

/// <summary>Platform video player hosted inside the mobile shell.</summary>
public interface IMobilePlayer : IAsyncDisposable
{
    Control View { get; }

    event EventHandler<string>? StatusChanged;

    Task PlayAsync(VideoSource source, Device device, PlaybackOptions options, CancellationToken cancellationToken = default);

    Task StopAsync();

    void SetPaused(bool paused);

    void SetMuted(bool muted);

    /// <summary>Stretches the picture to the shape of the view instead of keeping the camera proportions.</summary>
    void SetFillArea(bool fill);
}
