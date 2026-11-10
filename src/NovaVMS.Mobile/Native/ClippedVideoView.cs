using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Views;
using Android.Widget;
using Avalonia.Android;
using Avalonia.Controls;
using Java.Nio;
using NovaVMS.Protocols.Rtsp;
using AndroidRect = Android.Graphics.Rect;
using Color = Android.Graphics.Color;

namespace NovaVMS.Mobile.Native;

/// <summary>
/// Hardware decoded video: pictures go to a <see cref="MediaCodec"/> decoder that renders into a
/// <see cref="TextureView"/>. Unlike a SurfaceView, the texture is drawn within the view hierarchy and can be clipped,
/// so a preview scrolled partly out of the preview list is cut at its edge. Decoding runs on the receiving thread.
/// </summary>
public sealed class ClippedVideoView : FrameLayout, TextureView.ISurfaceTextureListener, IVideoRenderer
{
    private static readonly TimeSpan InputTimeout = TimeSpan.FromMilliseconds(50);

    private readonly object _gate = new();
    private readonly TextureView _texture;
    private readonly MediaCodec.BufferInfo _info = new();
    private Surface? _surface;
    private MediaCodec? _decoder;
    private IReadOnlyList<byte[]>? _decoderParameters;
    private VideoCodec _decoderCodec;
    private bool _needKeyFrame = true;
    private bool _detached;
    private AvaloniaView? _overlay;
    private bool _fill;
    private int _videoWidth;
    private int _videoHeight;
    private AndroidRect? _area;

    public ClippedVideoView(Context context)
        : base(context)
    {
        SetBackgroundColor(Color.Black);

        _texture = new TextureView(context)
        {
            SurfaceTextureListener = this
        };

        AddView(_texture, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    public bool Render(VideoAccessUnit picture)
    {
        lock (_gate)
        {
            if (_detached || _surface is null)
            {
                return false;
            }

            if (_needKeyFrame && !picture.IsKeyFrame)
            {
                return false;
            }

            try
            {
                if (_decoder is null || picture.Codec != _decoderCodec || !ReferenceEquals(picture.ParameterSets, _decoderParameters))
                {
                    if (!picture.IsKeyFrame)
                    {
                        _needKeyFrame = true;

                        return false;
                    }

                    StartDecoder(picture);
                }

                var decoder = _decoder!;
                var index = decoder.DequeueInputBuffer((long)InputTimeout.TotalMicroseconds);

                if (index < 0)
                {
                    // The decoder is behind; a skipped picture would damage the following ones.
                    _needKeyFrame = true;
                    Drain(decoder);

                    return false;
                }

                var input = decoder.GetInputBuffer(index)!;
                var size = picture.AnnexBSize(false);

                if (size > input.Capacity())
                {
                    decoder.QueueInputBuffer(index, 0, 0, 0, 0);
                    _needKeyFrame = true;

                    return false;
                }

                var data = new byte[size];

                picture.CopyAnnexB(data, false);
                input.Clear();
                input.Put(data);
                decoder.QueueInputBuffer(index, 0, size, picture.Timestamp * 100 / 9, picture.IsKeyFrame ? MediaCodecBufferFlags.KeyFrame : 0);
                _needKeyFrame = false;

                Drain(decoder);

                return true;
            }
            catch (Exception exception) when (exception is Java.Lang.IllegalStateException or MediaCodec.CodecException or Java.IO.IOException)
            {
                StopDecoder();
                _needKeyFrame = true;

                return false;
            }
        }
    }

    public void RequireKeyFrame()
    {
        lock (_gate)
        {
            _needKeyFrame = true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            StopDecoder();
            _needKeyFrame = true;
        }
    }

    public void SetFill(bool fill)
    {
        _fill = fill;

        Post(ApplyTransform);
    }

    public void SetVisibleArea(Avalonia.Rect? area, double scale)
    {
        _area = area is { } visible
            ? new AndroidRect(
                (int)Math.Floor(visible.X * scale),
                (int)Math.Floor(visible.Y * scale),
                (int)Math.Ceiling(visible.Right * scale),
                (int)Math.Ceiling(visible.Bottom * scale))
            : null;

        ApplyVisibleArea();
    }

    public void SetOverlay(Control? overlay)
    {
        if (_overlay is { } previous)
        {
            _overlay = null;

            OverlayBackground.Detach(previous.Content);

            previous.Content = null;
            RemoveView(previous);
            previous.Dispose();
        }

        if (overlay is null)
        {
            return;
        }

        // A second Avalonia view with a transparent background, above the video texture. Its picture is drawn on a
        // surface of its own, which lies below the window by default: the window then cuts a hole through the video
        // and the background behind the application shows instead of the picture. On top of the window the surface
        // is blended over the video. The activity itself stays opaque, so no transparency level is requested.
        var view = new AvaloniaView(Context!)
        {
            Content = overlay,
            Visibility = ViewStates.Gone
        };

        view.SetBackgroundColor(Color.Transparent);

        for (var index = 0; index < view.ChildCount; index++)
        {
            if (view.GetChildAt(index) is SurfaceView surface)
            {
                surface.SetZOrderOnTop(true);
            }
        }

        OverlayBackground.Attach(overlay);

        AddView(view, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _overlay = view;
    }

    public void SetOverlayVisible(bool visible)
    {
        if (_overlay is { } overlay)
        {
            overlay.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
        }
    }

    public void Detach()
    {
        SetOverlay(null);

        lock (_gate)
        {
            _detached = true;
            StopDecoder();
            _surface?.Release();
            _surface = null;
        }
    }

    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
    {
        lock (_gate)
        {
            if (!_detached)
            {
                _surface = new Surface(surface);
                _needKeyFrame = true;
            }
        }

        ApplyTransform();
    }

    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
    {
        lock (_gate)
        {
            StopDecoder();
            _surface?.Release();
            _surface = null;
        }

        return true;
    }

    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
    {
        ApplyTransform();
    }

    public void OnSurfaceTextureUpdated(SurfaceTexture surface)
    {
    }

    // The host places the view with layout margins, which take effect in this layout pass; cutting the view here
    // keeps the cut and the position of the same frame together, so a scrolled preview never covers the header.
    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        base.OnLayout(changed, left, top, right, bottom);

        ApplyVisibleArea();
    }

    private static ByteBuffer AnnexB(IEnumerable<byte[]> nals)
    {
        var bytes = nals.SelectMany(nal => new byte[] { 0, 0, 0, 1 }.Concat(nal)).ToArray();

        return ByteBuffer.Wrap(bytes)!;
    }

    // The area is in the coordinates of the Avalonia view that hosts this view; the cut is in its own coordinates.
    private void ApplyVisibleArea()
    {
        if (_area is not { } area)
        {
            ClipBounds = null;

            return;
        }

        var visible = new AndroidRect(area.Left - Left, area.Top - Top, area.Right - Left, area.Bottom - Top);

        if (!visible.Intersect(0, 0, Math.Max(Width, 1), Math.Max(Height, 1)))
        {
            visible.Set(0, 0, 0, 0);
        }

        ClipBounds = visible;
    }

    private void StartDecoder(VideoAccessUnit picture)
    {
        StopDecoder();

        var mime = picture.Codec == VideoCodec.H265 ? MediaFormat.MimetypeVideoHevc : MediaFormat.MimetypeVideoAvc;
        // The real size is read from the parameter sets; the decoder reports it once it starts.
        var format = MediaFormat.CreateVideoFormat(mime!, 1920, 1080);
        var parameters = picture.ParameterSets;

        if (picture.Codec == VideoCodec.H265)
        {
            format.SetByteBuffer("csd-0", AnnexB(parameters));
        }
        else
        {
            format.SetByteBuffer("csd-0", AnnexB(parameters.Take(parameters.Count - 1)));
            format.SetByteBuffer("csd-1", AnnexB(parameters.Skip(parameters.Count - 1)));
        }

        // Live video: decode with the lowest latency the device offers.
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            format.SetInteger(MediaFormat.KeyLowLatency, 1);
        }

        var decoder = MediaCodec.CreateDecoderByType(mime!);

        decoder.Configure(format, _surface, null, MediaCodecConfigFlags.None);
        decoder.Start();

        _decoder = decoder;
        _decoderCodec = picture.Codec;
        _decoderParameters = parameters;
    }

    private void StopDecoder()
    {
        if (_decoder is not { } decoder)
        {
            return;
        }

        _decoder = null;
        _decoderParameters = null;

        try
        {
            decoder.Stop();
        }
        catch (Java.Lang.IllegalStateException)
        {
        }

        decoder.Release();
    }

    // Decoded pictures are shown as soon as they are ready.
    private void Drain(MediaCodec decoder)
    {
        while (true)
        {
            var index = decoder.DequeueOutputBuffer(_info, 0);

            if (index >= 0)
            {
                decoder.ReleaseOutputBuffer(index, true);

                continue;
            }

            if (index == (int)MediaCodecInfoState.OutputFormatChanged)
            {
                var format = decoder.OutputFormat;
                var width = format.GetInteger(MediaFormat.KeyWidth);
                var height = format.GetInteger(MediaFormat.KeyHeight);

                if (format.ContainsKey("crop-right") && format.ContainsKey("crop-left"))
                {
                    width = format.GetInteger("crop-right") - format.GetInteger("crop-left") + 1;
                    height = format.GetInteger("crop-bottom") - format.GetInteger("crop-top") + 1;
                }

                _videoWidth = width;
                _videoHeight = height;
                Post(ApplyTransform);

                continue;
            }

            return;
        }
    }

    // The texture stretches the picture to the view; keeping the camera proportions scales it back.
    private void ApplyTransform()
    {
        var width = _texture.Width;
        var height = _texture.Height;
        var matrix = new Matrix();

        if (!_fill && width > 0 && height > 0 && _videoWidth > 0 && _videoHeight > 0)
        {
            var view = (float)width / height;
            var video = (float)_videoWidth / _videoHeight;
            var scaleX = video < view ? video / view : 1f;
            var scaleY = video > view ? view / video : 1f;

            matrix.SetScale(scaleX, scaleY, width / 2f, height / 2f);
        }

        _texture.SetTransform(matrix);
    }
}
