using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NovaVMS.App.Models;
using NovaVMS.App.ViewModels;
using System.ComponentModel;
using Avalonia.Media;

namespace NovaVMS.App.Views;

/// <summary>
/// Live view tile: a click selects it, a double-click maximizes it or restores the split,
/// and cameras dragged from the resource tree can be dropped onto it.
/// </summary>
public partial class VideoTileView : UserControl
{
    private static readonly TimeSpan DefaultDoubleClickTime = TimeSpan.FromMilliseconds(500);
    private static readonly Size DefaultDoubleClickSize = new(4, 4);

    // Double-clicks are detected here rather than with DoubleTapped: selecting the tile on the first
    // click re-lays out the overlay window, which resets Avalonia's own click counting.
    private ulong? _lastPressTimestamp;
    private PixelPoint _lastPressPosition;
    private IPointer? _lastPointer;
    private VideoTileViewModel? _tile;
    private Window? _overlayWindow;
    private Window? _ownerWindow;
    // Last geometry given to the overlay window. The owner's layout pass runs often (status text, timers),
    // and setting a window position always moves the native window, even to the same place.
    private PixelPoint? _overlayPosition;
    private Size _overlaySize;

    public VideoTileView()
    {
        InitializeComponent();
        VideoHost.Children.Remove(VideoOverlay);

        // Both surfaces must receive presses before child controls handle them.
        VideoOverlay.AddHandler(PointerPressedEvent, OnTilePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        HeaderButton.AddHandler(PointerPressedEvent, OnTilePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);

        foreach (var dropTarget in new Control[] { VideoOverlay, HeaderButton })
        {
            dropTarget.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            dropTarget.AddHandler(DragDrop.DropEvent, OnDrop);
        }

        foreach (var video in new[] { Video, SecondaryVideo })
        {
            video.AttachedToVisualTree += OnVideoAttached;
            video.DetachedFromVisualTree += OnVideoDetached;
        }

        VideoHost.SizeChanged += (_, _) => UpdateOverlayWindow();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (!ReferenceEquals(_tile, DataContext)) ResetDoubleClick();
        if (_tile is not null) _tile.PropertyChanged -= OnTilePropertyChanged;
        _tile = DataContext as VideoTileViewModel;
        if (_tile is not null) _tile.PropertyChanged += OnTilePropertyChanged;

        // The overlay is hosted in a separate window, so it does not inherit the data context.
        VideoOverlay.DataContext = DataContext;
        UpdateOverlayWindow();

        if (AreVideosAttached())
        {
            NotifyViewReadyAfterLayout();
        }
    }

    private void OnTilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoTileViewModel.IsVisibleInGrid)) UpdateOverlayWindow();
        if (e.PropertyName == nameof(VideoTileViewModel.IsPrimaryPlayerActive)) UpdateSurfaceOrder();
    }

    private void UpdateSurfaceOrder()
    {
        if (!OperatingSystem.IsWindows() || _tile is not { PrimaryPlayer: { } primary, SecondaryPlayer: { } secondary } tile)
            return;
        NativeVideoSurfaceOrder.ShowInFront(
            tile.IsPrimaryPlayerActive ? primary.Hwnd : secondary.Hwnd,
            tile.IsPrimaryPlayerActive ? secondary.Hwnd : primary.Hwnd);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        _ownerWindow = owner;
        owner.LayoutUpdated += OnOwnerLayoutUpdated;
        owner.PositionChanged += OnOwnerPositionChanged;
        owner.PropertyChanged += OnOwnerPropertyChanged;
        // Descendants attach after this override; cached bounds may already be non-zero
        // when a tab is reattached. Wait until VideoHost has its own visual root.
        Dispatcher.UIThread.Post(RestoreOverlayWindow, DispatcherPriority.Loaded);
    }

    private void RestoreOverlayWindow()
    {
        if (_ownerWindow is not { } owner || !this.IsAttachedToVisualTree()
            || owner.GetValue(ModalDialog.DepthProperty) > 0) return;
        if (_overlayWindow is not null) { UpdateOverlayWindow(); return; }
        _overlayPosition = null;
        _overlaySize = default;
        _overlayWindow = new VideoOverlayWindow
        {
            SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.Manual,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false,
            ShowInTaskbar = false,
            CanResize = false,
            Focusable = false,
            Content = VideoOverlay
        };
        UpdateOverlayWindow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ResetDoubleClick();
        if (_ownerWindow is { } owner)
        {
            owner.LayoutUpdated -= OnOwnerLayoutUpdated;
            owner.PositionChanged -= OnOwnerPositionChanged;
            owner.PropertyChanged -= OnOwnerPropertyChanged;
        }
        CloseOverlayWindow();
        _ownerWindow = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void CloseOverlayWindow()
    {
        var overlay = _overlayWindow;
        _overlayWindow = null;
        if (overlay is not null)
        {
            overlay.Content = null;
            overlay.Close();
        }
        _overlayPosition = null;
        _overlaySize = default;
    }

    private void OnOwnerLayoutUpdated(object? sender, EventArgs e)
    {
        if (_ownerWindow?.GetValue(ModalDialog.DepthProperty) == 0) UpdateSurfaceOrder();
        UpdateOverlayWindow();
    }
    private void OnOwnerPositionChanged(object? sender, PixelPointEventArgs e) => UpdateOverlayWindow();
    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ModalDialog.DepthProperty)
        {
            ResetDoubleClick();
            if (_ownerWindow?.GetValue(ModalDialog.DepthProperty) > 0)
                CloseOverlayWindow();
            else
                Dispatcher.UIThread.Post(RestoreOverlayWindow, DispatcherPriority.Loaded);
            return;
        }
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty || e.Property == OpacityProperty)
            UpdateOverlayWindow();
    }

    private void UpdateOverlayWindow()
    {
        if (_overlayWindow is not { } overlay || _ownerWindow is not { } owner) return;
        if (!this.IsAttachedToVisualTree() || !VideoHost.IsAttachedToVisualTree()
            || !ReferenceEquals(TopLevel.GetTopLevel(VideoHost), owner)
            || !IsEffectivelyVisible || _tile?.IsVisibleInGrid == false
            || owner.GetValue(ModalDialog.DepthProperty) > 0
            || !owner.IsVisible || owner.WindowState == WindowState.Minimized
            || VideoHost.Bounds.Width <= 0 || VideoHost.Bounds.Height <= 0)
        {
            if (overlay.IsVisible)
            {
                ResetDoubleClick();
                overlay.Hide();
            }
            return;
        }
        // Set the full tile bounds before the first Show. Keep this same window mapped across
        // SUB/MAIN handovers instead of letting each VideoView open a SizeToContent window.
        var size = VideoHost.Bounds.Size;
        var position = VideoHost.PointToScreen(default);
        if (size != _overlaySize)
        {
            overlay.Width = size.Width;
            overlay.Height = size.Height;
            _overlaySize = size;
        }

        overlay.Opacity = owner.Opacity;
        if (position != _overlayPosition || !overlay.IsVisible)
        {
            overlay.Position = position;
            _overlayPosition = position;
        }

        if (!overlay.IsVisible) overlay.Show(owner);
        _tile?.SetVideoSize(size.Width * owner.RenderScaling, size.Height * owner.RenderScaling);
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
            if (AreVideosAttached() && DataContext is VideoTileViewModel tile)
            {
                UpdateSurfaceOrder();
                tile.SetViewReady(true);
            }
        }, DispatcherPriority.Background);
    }

    private bool AreVideosAttached()
    {
        return Video.IsAttachedToVisualTree() && SecondaryVideo.IsAttachedToVisualTree();
    }

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not VideoTileViewModel tile || sender is not Control surface)
        {
            return;
        }
        if (e.GetCurrentPoint(null).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            ResetDoubleClick();
            return;
        }

        var topLevel = TopLevel.GetTopLevel(surface);
        var doubleClickTime = topLevel?.PlatformSettings?.GetDoubleTapTime(e.Pointer.Type) ?? DefaultDoubleClickTime;
        var doubleClickSize = topLevel?.PlatformSettings?.GetDoubleTapSize(e.Pointer.Type) ?? DefaultDoubleClickSize;
        var scaling = topLevel?.RenderScaling ?? 1;
        var position = surface.PointToScreen(e.GetPosition(surface));
        // Avalonia's count spans windows and can continue with 3, 4, ... after a double-click.
        // Pair presses ourselves in screen coordinates so moving/resizing an overlay does not
        // lose a double-click or immediately undo the previous maximize.
        var isDoubleClick = _lastPressTimestamp is ulong last && e.Timestamp >= last
            && e.Timestamp - last <= (ulong)doubleClickTime.TotalMilliseconds
            && ReferenceEquals(_lastPointer, e.Pointer)
            && Math.Abs((double)position.X - _lastPressPosition.X) <= doubleClickSize.Width * scaling / 2
            && Math.Abs((double)position.Y - _lastPressPosition.Y) <= doubleClickSize.Height * scaling / 2;

        if (isDoubleClick)
        {
            // A third quick click starts a new double-click instead of toggling again.
            ResetDoubleClick();
            tile.ToggleMaximizeCommand.Execute(null);
        }
        else
        {
            _lastPressTimestamp = e.Timestamp;
            _lastPressPosition = position;
            _lastPointer = e.Pointer;
            tile.SelectCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void ResetDoubleClick()
    {
        _lastPressTimestamp = null;
        _lastPointer = null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DataContext is VideoTileViewModel { CanAcceptDrop: true } && CameraDragData.Contains(e.DataTransfer)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
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
