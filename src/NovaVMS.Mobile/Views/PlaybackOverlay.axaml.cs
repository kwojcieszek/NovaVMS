using Avalonia.Controls;
using Avalonia.Input;
using NovaVMS.Mobile.ViewModels;

namespace NovaVMS.Mobile.Views;

/// <summary>
/// Transparent playback menu of a full screen camera, shown by the player in its own layer above the native picture.
/// A tap outside the menu bar hides it; using the menu keeps it open.
/// </summary>
public partial class PlaybackOverlay : UserControl
{
    public PlaybackOverlay()
    {
        InitializeComponent();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        (DataContext as CameraViewModel)?.KeepOverlay();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // Buttons handle their own taps; anything else is a tap on the picture behind the menu.
        if (!e.Handled && DataContext is CameraViewModel camera)
        {
            camera.IsOverlayVisible = false;
        }
    }
}
