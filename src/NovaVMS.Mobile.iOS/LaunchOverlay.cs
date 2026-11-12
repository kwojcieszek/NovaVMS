using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using CoreGraphics;
using NovaVMS.Mobile.Views;
using UIKit;

namespace NovaVMS.Mobile.iOS;

/// <summary>
/// The launch screen picture (logo and name on the window background) drawn by UIKit over the window until
/// Avalonia has rendered the shell, so the start never shows an empty window.
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

    /// <summary>
    /// Removes the overlay once the shell has read the configuration and rendered its pages, so the start screen is
    /// shown once and is followed directly by the camera list.
    /// </summary>
    public void RemoveWhenShown(MobileShell shell)
    {
        if (shell.IsStarted)
        {
            RemoveAfterRender(shell);

            return;
        }

        shell.Started += OnShellStarted;
    }

    private void OnShellStarted(object? sender, EventArgs args)
    {
        if (sender is MobileShell shell)
        {
            shell.Started -= OnShellStarted;

            RemoveAfterRender(shell);
        }
    }

    private async void RemoveAfterRender(Control shell)
    {
        if (ElementComposition.GetElementVisual(shell) is not { } visual)
        {
            return;
        }

        // Animation callbacks run on the UI thread and can outrun the renderer.
        // Wait for layout and composition, then for the render thread to finish the batch.
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        visual.Compositor.RequestCompositionUpdate(() =>
            rendered.SetResult());
        await rendered.Task;
        await visual.Compositor.RequestCompositionBatchCommitAsync().Rendered;
        await Dispatcher.UIThread.InvokeAsync(RemoveFromSuperview);
    }
}
