using CoreAnimation;
using CoreGraphics;
using LibVLCSharp.Platforms.iOS;
using UIKit;

namespace NovaVMS.Mobile.Native;

/// <summary>
/// LibVLC video view inside a container whose layer is masked to the visible part. A preview scrolled partly
/// out of the preview list is cut at its edge instead of covering the header and the navigation.
/// </summary>
public sealed class ClippedVideoView : UIView
{
    private readonly LibVLCSharp.Shared.MediaPlayer _player;
    private readonly VideoView _video;
    private readonly CALayer _mask = new()
    {
        BackgroundColor = UIColor.Black.CGColor
    };

    public ClippedVideoView(LibVLCSharp.Shared.MediaPlayer player)
    {
        _player = player;
        _video = new VideoView
        {
            MediaPlayer = player,
            AutoresizingMask = UIViewAutoresizing.FlexibleDimensions
        };

        AddSubview(_video);
    }

    /// <summary>Visible part of the view in points (Avalonia units); null shows all of it.</summary>
    public void SetVisibleArea(Avalonia.Rect? area, double scale)
    {
        CATransaction.Begin();
        CATransaction.DisableActions = true;

        if (area is { } visible)
        {
            _mask.Frame = new CGRect(visible.X, visible.Y, visible.Width, visible.Height);
            Layer.Mask = _mask;
        }
        else
        {
            Layer.Mask = null;
        }

        CATransaction.Commit();
    }

    /// <summary>
    /// Releases the view from the player; the view is not used afterwards. Avalonia destroys native views
    /// later than the tile disposes its player, and a disposed LibVLC player must not be called.
    /// </summary>
    public void Detach()
    {
        if (_player.NativeReference != IntPtr.Zero)
        {
            _video.MediaPlayer = null;
        }
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        _video.Frame = Bounds;
    }
}
