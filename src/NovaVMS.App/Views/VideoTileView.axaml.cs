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
        if (e.PropertyName is not (nameof(VideoTileViewModel.TransitionFrame)
            or nameof(VideoTileViewModel.IsPrimaryViewVisible) or nameof(VideoTileViewModel.IsSecondaryViewVisible))) return;
        var tile = _tile;
        var frame = tile?.TransitionFrame;
        if (tile is null || frame is null) return;
        UpdateOverlayWindow();
        var version = tile.TransitionCoverVersion;
        // Wait until visibility bindings and native window layout have been applied.
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_tile, tile) || tile.TransitionCoverVersion != version || !ReferenceEquals(tile.TransitionFrame, frame)
                || !this.IsAttachedToVisualTree()) return;
            if (_overlayWindow is not { IsVisible: true } topLevel) return;
            // Acknowledge both the outgoing cover and, after mapping, the incoming cover.
            void ConfirmCover(TimeSpan timestamp)
            {
                if (!ReferenceEquals(_tile, tile) || !this.IsAttachedToVisualTree()
                    || tile.TransitionCoverVersion != version || !ReferenceEquals(tile.TransitionFrame, frame)
                    || !topLevel.IsVisible) return;
                // A painted window at the old tile size/position is not a ready cover.
                var tolerance = 1 / topLevel.RenderScaling;
                bool SameSize(Size size) => Math.Abs(size.Width - VideoHost.Bounds.Width) <= tolerance
                    && Math.Abs(size.Height - VideoHost.Bounds.Height) <= tolerance;
                if (SameSize(VideoOverlay.Bounds.Size) && SameSize(topLevel.Bounds.Size)
                    && topLevel.Position == VideoHost.PointToScreen(default))
                    tile.ConfirmTransitionCoverReady(frame, version);
                else
                {
                    UpdateOverlayWindow();
                    topLevel.RequestAnimationFrame(ConfirmCover);
                }
            }
            topLevel.RequestAnimationFrame(_ => topLevel.RequestAnimationFrame(ConfirmCover));
        }, DispatcherPriority.Background);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        _ownerWindow = owner;
        _overlayWindow = new Window
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
        UpdateOverlayWindow();
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
        base.OnDetachedFromVisualTree(e);
    }

    private void OnOwnerLayoutUpdated(object? sender, EventArgs e) => UpdateOverlayWindow();
    private void OnOwnerPositionChanged(object? sender, PixelPointEventArgs e) => UpdateOverlayWindow();
    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty || e.Property == OpacityProperty)
            UpdateOverlayWindow();
    }

    private void UpdateOverlayWindow()
    {
        if (_overlayWindow is not { } overlay || _ownerWindow is not { } owner) return;
        if (!this.IsAttachedToVisualTree() || !IsEffectivelyVisible || _tile?.IsVisibleInGrid == false
            || !owner.IsVisible || owner.WindowState == WindowState.Minimized
            || VideoHost.Bounds.Width <= 0 || VideoHost.Bounds.Height <= 0)
        {
            if (overlay.IsVisible) overlay.Hide();
            return;
        }
        // Set the full tile bounds before the first Show. Keep this same window mapped across
        // SUB/MAIN handovers instead of letting each VideoView open a SizeToContent window.
        overlay.Width = VideoHost.Bounds.Width;
        overlay.Height = VideoHost.Bounds.Height;
        overlay.Opacity = owner.Opacity;
        overlay.Position = VideoHost.PointToScreen(default);
        if (!overlay.IsVisible) overlay.Show(owner);
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
