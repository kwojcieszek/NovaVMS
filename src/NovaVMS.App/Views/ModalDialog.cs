using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace NovaVMS.App.Views;

/// <summary>Suspends native video overlays for the lifetime of a modal window.</summary>
internal sealed class ModalDialog
{
    private ModalDialog() { }

    internal static readonly AttachedProperty<int> DepthProperty =
        AvaloniaProperty.RegisterAttached<ModalDialog, Window, int>("Depth");

    internal static async Task<T> ShowAsync<T>(Window dialog, Window owner)
    {
        // Finish the input event first: a split selector popup may still hold pointer capture.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var owners = new List<Window>();
        for (Window? window = owner; window is not null; window = window.Owner as Window)
            owners.Add(window);
        foreach (var window in owners)
            window.SetValue(DepthProperty, window.GetValue(DepthProperty) + 1);
        try { return await dialog.ShowDialog<T>(owner); }
        finally
        {
            foreach (var window in owners)
                window.SetValue(DepthProperty, window.GetValue(DepthProperty) - 1);
        }
    }
}
