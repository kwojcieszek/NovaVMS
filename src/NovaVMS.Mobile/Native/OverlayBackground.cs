using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NovaVMS.Mobile.Native;

/// <summary>
/// Keeps the top level of the playback menu shown above the native video transparent. The top level exists only once
/// the menu is attached to it; until then the theme gives it an opaque background (white in the light theme), which
/// covered the picture whenever the menu was used.
/// </summary>
internal static class OverlayBackground
{
    public static void Attach(Control overlay)
    {
        overlay.AttachedToVisualTree += OnAttached;

        MakeTransparent(overlay);
    }

    public static void Detach(object? overlay)
    {
        if (overlay is Control control)
        {
            control.AttachedToVisualTree -= OnAttached;
        }
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control overlay)
        {
            MakeTransparent(overlay);
        }
    }

    private static void MakeTransparent(Control overlay)
    {
        if (TopLevel.GetTopLevel(overlay) is { } topLevel)
        {
            topLevel.Background = Brushes.Transparent;
        }
    }
}
