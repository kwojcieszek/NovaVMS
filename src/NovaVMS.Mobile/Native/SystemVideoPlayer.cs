using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.VisualTree;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Services;
using NovaVMS.Presentation.Localization;
using NovaVMS.Protocols.Rtsp;
#if ANDROID
using Avalonia.Android;
#else
using Avalonia.iOS;
#endif

namespace NovaVMS.Mobile.Native;

/// <summary>
/// Live and recorded video with the hardware decoder of the phone. The RTSP stream is received on the thread pool
/// and every picture goes from there straight to the decoder of the native view (iOS AVSampleBufferDisplayLayer,
/// Android MediaCodec); the interface thread only places the view. Compiled by the Android and iOS heads.
/// Only H.264 and H.265 video is shown; sound is not played.
/// </summary>
public sealed class SystemVideoPlayer : IMobilePlayer
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private readonly VideoHost _host;
    private CancellationTokenSource? _run;
    private Task? _receiving;
    private IDisposable? _lifetime;
    private volatile IVideoRenderer? _renderer;
    private volatile bool _paused;
    private int _playingReported;
    private bool _fill;
    private Control? _overlay;
    private bool _overlayVisible;

    public Control View => _host;

    public bool SupportsAudio => false;

    public event EventHandler<string>? StatusChanged;

    public SystemVideoPlayer()
    {
        _host = new VideoHost(this);
    }

    public async Task PlayAsync(VideoSource source, Device device, PlaybackOptions options, CancellationToken cancellationToken = default)
    {
        await StopAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var encryptedUri = source.EncryptedUri;

        if (source.IsStream && encryptedUri is null)
        {
            // Streams of the Hikvision SDK need the desktop decoders.
            throw new NotSupportedException("Data stream sources are not supported on phones.");
        }

        Uri uri;
        IDisposable? lifetime = null;

        if (encryptedUri is not null)
        {
            // Encrypted RTSP video is decrypted here with the stream key of the device.
            uri = encryptedUri;
        }
        else
        {
            // Opening a recording can start a local session that connects to the device.
            (uri, lifetime) = await Task.Run(() =>
            {
                var address = source.OpenUri(out var owned);

                return (address, owned);
            }, cancellationToken);
        }

        var streamKey = encryptedUri is null ? null : device.StreamEncryptionKey;
        var run = new CancellationTokenSource();
        var session = new RtspVideoSession(uri, device.Username, device.Password, Show, streamKey);

        _lifetime = lifetime;
        _run = run;
        _paused = false;
        _playingReported = 0;
        _renderer?.RequireKeyFrame();

        StatusChanged?.Invoke(this, Localizer.Text("Mobile_Connecting"));

        _receiving = Task.Run(() => ReceiveAsync(session, run.Token), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        var run = _run;
        var receiving = _receiving;
        var lifetime = _lifetime;

        _run = null;
        _receiving = null;
        _lifetime = null;

        if (run is null)
        {
            return;
        }

        await run.CancelAsync();

        if (receiving is not null)
        {
            // The session ends on its own; a camera that does not answer the TEARDOWN is not waited for.
            await Task.WhenAny(receiving, Task.Delay(StopTimeout));
        }

        run.Dispose();

        await Task.Run(() => lifetime?.Dispose());

        _renderer?.Clear();
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;

        if (!paused)
        {
            _renderer?.RequireKeyFrame();
        }
    }

    public void SetMuted(bool muted)
    {
    }

    public void SetFillArea(bool fill)
    {
        _fill = fill;
        _renderer?.SetFill(fill);
    }

    public void SetOverlay(Control? overlay)
    {
        _overlay = overlay;
        _overlayVisible = false;
        _renderer?.SetOverlay(overlay);
    }

    public void SetOverlayVisible(bool visible)
    {
        _overlayVisible = visible;
        _renderer?.SetOverlayVisible(visible);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task ReceiveAsync(RtspVideoSession session, CancellationToken token)
    {
        try
        {
            await session.RunAsync(token).ConfigureAwait(false);

            if (!token.IsCancellationRequested)
            {
                StatusChanged?.Invoke(this, Localizer.Text("Mobile_StreamEnded"));
            }
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            StatusChanged?.Invoke(this, Localizer.Text("Mobile_PlaybackError"));
        }
        catch (Exception)
        {
            // Stopped while connecting.
        }
    }

    // Called on the receiving thread for every picture.
    private void Show(VideoAccessUnit picture)
    {
        if (_paused || _renderer is not { } renderer)
        {
            return;
        }

        if (renderer.Render(picture) && Interlocked.Exchange(ref _playingReported, 1) == 0)
        {
            StatusChanged?.Invoke(this, Localizer.Text("Mobile_Playing"));
        }
    }

    /// <summary>
    /// Hosts the native video view. Inside a scroll viewer the view is cut to the area of the viewer,
    /// because native views are drawn above Avalonia content and are not clipped by it.
    /// </summary>
    private sealed class VideoHost(SystemVideoPlayer player) : NativeControlHost
    {
        private ClippedVideoView? _native;
        private Rect? _visibleArea;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            LayoutUpdated += OnLayoutUpdated;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            LayoutUpdated -= OnLayoutUpdated;

            base.OnDetachedFromVisualTree(e);
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
#if ANDROID
            _native = new ClippedVideoView(global::Android.App.Application.Context);
#else
            _native = new ClippedVideoView();
#endif
            _native.SetFill(player._fill);
            _native.SetOverlay(player._overlay);
            _native.SetOverlayVisible(player._overlayVisible);
            _visibleArea = null;

            UpdateVisibleArea();

            player._renderer = _native;

#if ANDROID
            return new AndroidViewControlHandle(_native);
#else
            return new UIViewControlHandle(_native);
#endif
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (ReferenceEquals(player._renderer, _native))
            {
                player._renderer = null;
            }

            _native?.Detach();

            base.DestroyNativeControlCore(control);

            _native = null;
        }

        // The viewer may move or change its size with any layout pass.
        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            UpdateVisibleArea();
        }

        private void UpdateVisibleArea()
        {
            if (_native is null)
            {
                return;
            }

            var area = VisibleArea();

            if (area == _visibleArea)
            {
                return;
            }

            _visibleArea = area;
            _native.SetVisibleArea(area, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        }

        // Area of the nearest scroll viewer in top level coordinates; null outside a viewer. The native view cuts
        // itself to it at the position it is actually placed at, so the cut follows the view while the list scrolls.
        private Rect? VisibleArea()
        {
            var viewer = this.FindAncestorOfType<ScrollViewer>();
            var topLevel = TopLevel.GetTopLevel(this);

            if (viewer is null || topLevel is null || viewer.TranslatePoint(default, topLevel) is not { } origin)
            {
                return null;
            }

            return new Rect(origin, viewer.Bounds.Size);
        }
    }
}
