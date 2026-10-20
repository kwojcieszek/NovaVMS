using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NovaVMS.App.Models;
using NovaVMS.App.ViewModels;

namespace NovaVMS.App.Views;

/// <summary>
/// Live view tile: a click selects it, a double-click maximizes it or restores the split,
/// and cameras dragged from the resource tree can be dropped onto it.
/// </summary>
public partial class VideoTileView : UserControl
{
    private static readonly TimeSpan DefaultDoubleClickTime = TimeSpan.FromMilliseconds(500);

    // Double-clicks are detected here rather than with DoubleTapped: selecting the tile on the first
    // click re-lays out the overlay window, which resets Avalonia's own click counting.
    private ulong? _lastPressTimestamp;

    public VideoTileView()
    {
        InitializeComponent();

        VideoOverlay.AddHandler(PointerPressedEvent, OnTilePointerPressed);
        // Tunnelling, because the header button marks pointer presses as handled.
        HeaderButton.AddHandler(PointerPressedEvent, OnTilePointerPressed, RoutingStrategies.Tunnel);

        foreach (var dropTarget in new Control[] { VideoOverlay, HeaderButton })
        {
            dropTarget.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            dropTarget.AddHandler(DragDrop.DropEvent, OnDrop);
        }

        Video.AttachedToVisualTree += OnVideoAttached;
        Video.DetachedFromVisualTree += OnVideoDetached;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // The overlay is hosted in a separate window, so it does not inherit the data context.
        VideoOverlay.DataContext = DataContext;

        if (Video.IsAttachedToVisualTree())
        {
            NotifyViewReadyAfterLayout();
        }
    }

    private void OnVideoAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        NotifyViewReadyAfterLayout();
    }

    private void OnVideoDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is VideoTileViewModel tile)
        {
            tile.SetViewReady(false);
        }
    }

    // The native surface is created while the video view attaches; the notification is deferred
    // until layout has finished so the player is guaranteed to have its window handle.
    private void NotifyViewReadyAfterLayout()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Video.IsAttachedToVisualTree() && DataContext is VideoTileViewModel tile)
            {
                tile.SetViewReady(true);
            }
        }, DispatcherPriority.Background);
    }

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not VideoTileViewModel tile || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var doubleClickTime = TopLevel.GetTopLevel(this)?.PlatformSettings?.GetDoubleTapTime(e.Pointer.Type) ?? DefaultDoubleClickTime;
        var isDoubleClick = e.ClickCount >= 2
            || (_lastPressTimestamp is ulong last && e.Timestamp - last <= (ulong)doubleClickTime.TotalMilliseconds);

        if (isDoubleClick)
        {
            // A third quick click starts a new double-click instead of toggling again.
            _lastPressTimestamp = null;
            tile.ToggleMaximizeCommand.Execute(null);
        }
        else
        {
            _lastPressTimestamp = e.Timestamp;
            tile.SelectCommand.Execute(null);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = CameraDragData.Contains(e.DataTransfer) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not VideoTileViewModel tile)
        {
            return;
        }

        var cameras = CameraDragData.Read(e.DataTransfer);
        tile.AcceptDrop(cameras);
        e.Handled = true;
    }
}
