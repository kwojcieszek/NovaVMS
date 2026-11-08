using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.VisualTree;
using LibVLCSharp.Shared;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;
using NovaVMS.Mobile.Services;
#if ANDROID
using Avalonia.Android;
#else
using Avalonia.iOS;
#endif

namespace NovaVMS.Mobile.Native;

/// <summary>Compiled against platform-specific LibVLC bindings, never the desktop video control.</summary>
public sealed class VlcPlayer : IMobilePlayer
{
    // One LibVLC instance for all players: camera group previews run several players at once.
    private static readonly Lazy<LibVLC> SharedLibrary = new(CreateLibrary);

    private readonly LibVLC _vlc;
    private readonly LibVLCSharp.Shared.MediaPlayer _player;
    private Media? _media;
    private IDisposable? _network;
    private Stream? _stream;
    private StreamMediaInput? _input;
    private bool _fillArea;
    private Size _viewSize;

    public Control View { get; }

    public event EventHandler<string>? StatusChanged;

    public VlcPlayer()
    {
        _vlc = SharedLibrary.Value;
        _player = new LibVLCSharp.Shared.MediaPlayer(_vlc)
        {
            EnableHardwareDecoding = true,
            Mute = true
        };

        _player.Playing += (_, _) => StatusChanged?.Invoke(this, Localizer.Text("Mobile_Playing"));
        _player.EncounteredError += (_, _) => StatusChanged?.Invoke(this, Localizer.Text("Mobile_PlaybackError"));
        _player.EndReached += (_, _) => StatusChanged?.Invoke(this, Localizer.Text("Mobile_StreamEnded"));

        View = new VideoHost(_player, OnViewSizeChanged);
    }

    public async Task PlayAsync(VideoSource source, Device device, PlaybackOptions options, CancellationToken cancellationToken = default)
    {
        await StopAsync();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (source.IsStream)
            {
                _stream = source.OpenStream(options.WithAudio);
                _input = new StreamMediaInput(_stream);
                _media = new Media(_vlc, _input);
            }
            else
            {
                var uri = source.OpenUri(out _network);

                _media = new Media(_vlc, uri);
                _media.AddOption($":rtsp-user={device.Username}");
                _media.AddOption($":rtsp-pwd={device.Password}");
                _media.AddOption(":rtsp-tcp");
                _media.AddOption(":rtsp-frame-buffer-size=4194304");
            }

            ApplyOptions(_media, options);
            _player.EnableHardwareDecoding = options.HardwareDecoding;
            cancellationToken.ThrowIfCancellationRequested();
            StatusChanged?.Invoke(this, Localizer.Text("Mobile_Connecting"));

            if (!_player.Play(_media))
            {
                throw new InvalidOperationException("Video playback could not start.");
            }
        }
        catch
        {
            await StopAsync();

            throw;
        }
    }

    public async Task StopAsync()
    {
        // Stream disposal unblocks LibVLC before its synchronous stop waits for the input thread.
        await Task.Run(() =>
        {
            _stream?.Dispose();
            _network?.Dispose();
            _player.Stop();
            _media?.Dispose();
            _input?.Dispose();

            _media = null;
            _input = null;
            _stream = null;
            _network = null;
        });
    }

    public void SetPaused(bool paused)
    {
        _player.SetPause(paused);
    }

    public void SetMuted(bool muted)
    {
        _player.Mute = muted;
    }

    public void SetFillArea(bool fill)
    {
        _fillArea = fill;

        ApplyAspectRatio();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await Task.Run(_player.Dispose);
    }

    private static LibVLC CreateLibrary()
    {
        LibVLCSharp.Shared.Core.Initialize();

        return new LibVLC("--no-video-title-show", "--no-osd");
    }

    /// <summary>
    /// Media options of the playback profile. With the platform decoder no video filter is added: frames are rendered
    /// straight to the surface and a filter would force a copy of every frame. Software decoded previews are made
    /// cheap: the desktop "fps" filter limits the frame rate, non-reference frames and deblocking are skipped.
    /// </summary>
    private static void ApplyOptions(Media media, PlaybackOptions options)
    {
        media.AddOption($":network-caching={options.NetworkCachingMs}");

        if (!options.WithAudio)
        {
            media.AddOption(":no-audio");
        }

        if (options.HardwareDecoding)
        {
            return;
        }

        if (options.MaxFrameRate > 0)
        {
            media.AddOption(":video-filter=fps");
            media.AddOption($":fps-fps={options.MaxFrameRate}");
        }

        if (options.LowCostDecoding)
        {
            media.AddOption(":avcodec-skip-frame=1");
            media.AddOption(":avcodec-skiploopfilter=4");
            media.AddOption(":avcodec-fast");
            media.AddOption(":avcodec-threads=1");
            media.AddOption(":avcodec-hurry-up");
            media.AddOption(":drop-late-frames");
        }
    }

    private void OnViewSizeChanged(Size size)
    {
        _viewSize = size;

        ApplyAspectRatio();
    }

    // Like the desktop "fit" mode: the view's own shape is given to the player, so the picture has no black bars.
    private void ApplyAspectRatio()
    {
        var ratio = _fillArea && _viewSize.Width >= 1 && _viewSize.Height >= 1
            ? VideoDisplayGeometry.FromDimensions((uint)Math.Round(_viewSize.Width), (uint)Math.Round(_viewSize.Height))
            : null;

        if (_player.AspectRatio != ratio)
        {
            _player.AspectRatio = ratio;
        }
    }

    /// <summary>
    /// Hosts the clippable video view. Inside a scroll viewer the view is cut to the visible part of the viewer,
    /// because native views are drawn above Avalonia content and are not clipped by it.
    /// </summary>
    private sealed class VideoHost(LibVLCSharp.Shared.MediaPlayer player, Action<Size> sizeChanged) : NativeControlHost
    {
        private ClippedVideoView? _native;
        private Rect? _visibleArea;

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);

            sizeChanged(e.NewSize);
        }

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
            _native = new ClippedVideoView(global::Android.App.Application.Context, player);
#else
            _native = new ClippedVideoView(player);
#endif
            _visibleArea = null;

            UpdateVisibleArea();

#if ANDROID
            return new AndroidViewControlHandle(_native);
#else
            return new UIViewControlHandle(_native);
#endif
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            _native?.Detach();

            base.DestroyNativeControlCore(control);

            _native = null;
        }

        // Scrolling arranges the list again, so every layout pass may move the view in or out of the viewer.
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

        // Part of this control inside the nearest scroll viewer, in its own coordinates; null outside a viewer.
        private Rect? VisibleArea()
        {
            var viewer = this.FindAncestorOfType<ScrollViewer>();

            if (viewer is null || this.TranslatePoint(default, viewer) is not { } origin)
            {
                return null;
            }

            var own = new Rect(origin, Bounds.Size);
            var visible = own.Intersect(new Rect(viewer.Bounds.Size));

            if (visible.Width <= 0 || visible.Height <= 0)
            {
                return default(Rect);
            }

            return visible.Translate(-(Vector)origin);
        }
    }
}
