using Avalonia.Controls;
using CoreGraphics;
using UIKit;

namespace NovaVMS.Mobile.iOS;

/// <summary>
/// The launch screen picture (logo and name on the window background) drawn by UIKit over the window until
/// Avalonia has drawn its first frames, so the start never shows an empty window.
/// </summary>
public sealed class LaunchOverlay : UIView
{
    /// <summary>Window background of the application, the same color as the launch screen.</summary>
    public static UIColor Background => UIColor.FromName("LaunchBackground") ?? UIColor.FromRGB(0x1B, 0x1C, 0x1F);

    public LaunchOverlay(CGRect frame)
        : base(frame)
    {
        BackgroundColor = Background;
        AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;

        var logo = new UIImageView(UIImage.FromBundle("LaunchLogo"))
        {
            ContentMode = UIViewContentMode.Center,
            Frame = Bounds,
            AutoresizingMask = UIViewAutoresizing.FlexibleDimensions
        };

        AddSubview(logo);
    }

    /// <summary>Removes the overlay once the shell is shown and Avalonia has drawn two frames over it.</summary>
    public void RemoveWhenShown(Control shell)
    {
        if (shell.IsLoaded)
        {
            RemoveAfterFrames(shell);

            return;
        }

        shell.Loaded += (_, _) => RemoveAfterFrames(shell);
    }

    private void RemoveAfterFrames(Control shell)
    {
        if (TopLevel.GetTopLevel(shell) is not { } topLevel)
        {
            RemoveFromSuperview();

            return;
        }

        // The callback runs before a frame is drawn; the second one comes after the first frame is on screen.
        topLevel.RequestAnimationFrame(_ => topLevel.RequestAnimationFrame(_ => RemoveFromSuperview()));
    }
}
