using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using NovaVMS.Mobile.ViewModels;

namespace NovaVMS.Mobile.Views;

public partial class CameraView : UserControl
{
    private const double MinimumVideoHeight = 100;
    private const double MaximumVideoHeight = 420;
    private const double MaximumHeightShare = 0.4;
    private const double DoubleTapDistance = 40;

    private static readonly TimeSpan DoubleTapTime = TimeSpan.FromMilliseconds(450);

    private DateTime _lastTapTime;
    private Point _lastTapPosition;
    private DispatcherTimer? _singleTap;
    private CameraViewModel? _camera;

    public CameraView()
    {
        InitializeComponent();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        ResizeVideo();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_camera is not null)
        {
            _camera.PropertyChanged -= OnCameraPropertyChanged;
        }

        _camera = DataContext as CameraViewModel;

        if (_camera is not null)
        {
            _camera.PropertyChanged += OnCameraPropertyChanged;
        }

        ApplyFullScreen();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _singleTap?.Stop();

        base.OnDetachedFromVisualTree(e);
    }

    private void OnCameraPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CameraViewModel.IsFullScreen))
        {
            ApplyFullScreen();
        }
    }

    // The playback menu of a full screen camera lives in the player's layer above the picture.
    private void ApplyFullScreen()
    {
        ResizeVideo();

        if (_camera is { IsFullScreen: true } camera)
        {
            camera.AttachOverlay(new PlaybackOverlay { DataContext = camera });
        }
    }

    // Native video is drawn above Avalonia content, so a full screen camera has no visible buttons; iOS also has
    // no system back button. A double tap on the picture switches full screen; a single tap on a full screen
    // picture opens the playback menu, so touching the picture by accident does not close the camera.
    // The double tap is recognized when the finger is lifted, and acted on only after that touch has ended:
    // removing the native video view under a finger that is still down makes UIKit cancel the touch, and the
    // lost end of the touch then kept the next double tap from being recognized.
    private void OnVideoPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not CameraViewModel camera)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var position = e.GetPosition(this);
        var second = now - _lastTapTime <= DoubleTapTime
            && Math.Abs(position.X - _lastTapPosition.X) <= DoubleTapDistance
            && Math.Abs(position.Y - _lastTapPosition.Y) <= DoubleTapDistance;

        if (!second)
        {
            _lastTapTime = now;
            _lastTapPosition = position;

            if (camera.IsFullScreen)
            {
                // A single tap opens the playback menu once no second tap follows.
                _singleTap ??= new DispatcherTimer(DoubleTapTime, DispatcherPriority.Normal, OnSingleTap);
                _singleTap.Stop();
                _singleTap.Start();
            }

            return;
        }

        _singleTap?.Stop();
        _lastTapTime = default;
        e.Handled = true;

        Dispatcher.UIThread.Post(camera.ToggleFullScreen, DispatcherPriority.Background);
    }

    private void OnSingleTap(object? sender, EventArgs e)
    {
        _singleTap?.Stop();

        if (DataContext is CameraViewModel { IsFullScreen: true } camera)
        {
            camera.ToggleOverlay();
        }
    }

    // Full screen the video spans every row of the page; otherwise 16:9 video that never takes more than 40% of the screen height.
    private void ResizeVideo()
    {
        var fullScreen = DataContext is CameraViewModel { IsFullScreen: true };

        Grid.SetRow(VideoArea, fullScreen ? 0 : 1);
        Grid.SetRowSpan(VideoArea, fullScreen ? 3 : 1);

        if (fullScreen)
        {
            VideoArea.Height = double.NaN;

            return;
        }

        var screen = TopLevel.GetTopLevel(this)?.Bounds ?? Bounds;
        var height = Math.Clamp(Math.Min(Bounds.Width * 9 / 16, screen.Height * MaximumHeightShare), MinimumVideoHeight, MaximumVideoHeight);

        VideoArea.Height = height;
    }
}
