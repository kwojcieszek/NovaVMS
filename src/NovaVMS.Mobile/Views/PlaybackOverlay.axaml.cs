using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NovaVMS.Mobile.ViewModels;

namespace NovaVMS.Mobile.Views;

/// <summary>
/// Transparent playback menu of a full screen camera, shown by the player in its own layer above the native picture.
/// A tap outside the menu bars hides it; using the menu keeps it open.
/// </summary>
public partial class PlaybackOverlay : UserControl
{
    public PlaybackOverlay()
    {
        InitializeComponent();

        // Buttons and the hour bar handle their touches, so the menu is kept open before they see them.
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnAnyPointerMoved, RoutingStrategies.Tunnel);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // Buttons handle their own taps; a tap on a bar or the calendar keeps the menu, anything else is a tap on the picture.
        if (!e.Handled && !IsOnBar(e.Source as Visual) && DataContext is CameraViewModel camera)
        {
            camera.IsOverlayVisible = false;
        }
    }

    private static bool IsOnBar(Visual? source)
    {
        return source?.GetSelfAndVisualAncestors().Any(visual => visual is Border border && (border.Classes.Contains("bar") || border.Classes.Contains("sheet"))) == true;
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        (DataContext as CameraViewModel)?.KeepOverlay();
    }

    private void OnAnyPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Captured is not null)
        {
            (DataContext as CameraViewModel)?.KeepOverlay();
        }
    }
}
