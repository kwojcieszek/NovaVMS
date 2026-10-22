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

    // Double-clicks are detected here rather than with DoubleTapped: selecting the tile on the first
    // click re-lays out the overlay window, which resets Avalonia's own click counting.
    private ulong? _lastPressTimestamp;
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

        VideoOverlay.AddHandler(PointerPressedEvent, OnTilePointerPressed);
        // Tunnelling, because the header button marks pointer presses as handled.
        HeaderButton.AddHandler(PointerPressedEvent, OnTilePointerPressed, RoutingStrategies.Tunnel);

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
        owner.LayoutUpdated += OnOwnerLayoutUpdated;
        owner.PositionChanged += OnOwnerPositionChanged;
        owner.PropertyChanged += OnOwnerPropertyChanged;
        // Descendants attach after this override; cached bounds may already be non-zero
        // when a tab is reattached. Wait until VideoHost has its own visual root.
        Dispatcher.UIThread.Post(UpdateOverlayWindow, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_ownerWindow is { } owner)
        {
            owner.LayoutUpdated -= OnOwnerLayoutUpdated;
            owner.PositionChanged -= OnOwnerPositionChanged;
            owner.PropertyChanged -= OnOwnerPropertyChanged;
        }
        if (_overlayWindow is { } overlay)
        {
            overlay.Content = null;
            overlay.Close();
        }
        _overlayWindow = null;
        _ownerWindow = null;
        _overlayPosition = null;
        _overlaySize = default;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnOwnerLayoutUpdated(object? sender, EventArgs e)
    {
        if (_ownerWindow?.GetValue(ModalDialog.DepthProperty) == 0) UpdateSurfaceOrder();
        UpdateOverlayWindow();
    }
    private void OnOwnerPositionChanged(object? sender, PixelPointEventArgs e) => UpdateOverlayWindow();
    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty || e.Property == OpacityProperty
            || e.Property == ModalDialog.DepthProperty)
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
            if (overlay.IsVisible) overlay.Hide();
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
