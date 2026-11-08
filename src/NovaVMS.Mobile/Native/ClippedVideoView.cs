using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using LibVLCSharp.Platforms.Android;
using Org.Videolan.Libvlc;
using AndroidRect = Android.Graphics.Rect;

namespace NovaVMS.Mobile.Native;

/// <summary>
/// LibVLC video rendered to a <see cref="TextureView"/>, which, unlike the SurfaceView of the LibVLCSharp
/// video view, is drawn within the view hierarchy and can be clipped. A preview scrolled partly out of the
/// preview list is cut at its edge instead of covering the header and the navigation.
/// </summary>
public sealed class ClippedVideoView : FrameLayout, TextureView.ISurfaceTextureListener
{
    private readonly LibVLCSharp.Shared.MediaPlayer _player;
    private readonly TextureView _texture;
    private readonly AWindow _window;
    private readonly LayoutChangeListener _layoutListener;
    private bool _attached;

    public ClippedVideoView(Context context, LibVLCSharp.Shared.MediaPlayer player)
        : base(context)
    {
        _player = player;
        _texture = new TextureView(context);
        _window = new AWindow(null);
        _layoutListener = new LayoutChangeListener(_window);

        AddView(_texture, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // The player waits for the surface; the views are attached once the texture exists.
        _window.SetVideoView(_texture);
        _texture.SurfaceTextureListener = this;
        _texture.AddOnLayoutChangeListener(_layoutListener);
        _player.SetAndroidContext(_window.Handle);
    }

    /// <summary>Visible part of the view in Avalonia units, converted with <paramref name="scale"/> to pixels; null shows all of it.</summary>
    public void SetVisibleArea(Avalonia.Rect? area, double scale)
    {
        ClipBounds = area is { } visible
            ? new AndroidRect(
                (int)Math.Floor(visible.X * scale),
                (int)Math.Floor(visible.Y * scale),
                (int)Math.Ceiling(visible.Right * scale),
                (int)Math.Ceiling(visible.Bottom * scale))
            : null;
    }

    /// <summary>
    /// Releases the surface from the player; the view is not used afterwards. Avalonia destroys native views
    /// later than the tile disposes its player, and a disposed LibVLC player must not be called.
    /// </summary>
    public void Detach()
    {
        if (_player.NativeReference != IntPtr.Zero)
        {
            _player.SetAndroidContext(IntPtr.Zero);
        }

        _texture.RemoveOnLayoutChangeListener(_layoutListener);

        if (_attached)
        {
            _attached = false;
            _window.DetachViews();
        }
    }

    // AWindow replaces this listener with its own when the views are attached.
    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _window.AttachViews();
        _window.SetWindowSize(width, height);
    }

    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
    {
        return true;
    }

    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
    {
    }

    public void OnSurfaceTextureUpdated(SurfaceTexture surface)
    {
    }
}
