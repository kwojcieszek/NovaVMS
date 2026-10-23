using Avalonia.Controls;
using Avalonia;
using Avalonia.Threading;
using NovaVMS.App.ViewModels;
using System.ComponentModel;

namespace NovaVMS.App.Views;

public partial class PlaybackView : UserControl
{
    private PlaybackViewModel? _playback;

    public PlaybackView()
    {
        InitializeComponent();
        PrimaryVideo.AttachedToVisualTree += OnVideoAttached;
        SecondaryVideo.AttachedToVisualTree += OnVideoAttached;
        VideoHost.LayoutUpdated += (_, _) => UpdateSurfaceOrder();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_playback is not null) _playback.PropertyChanged -= OnPlaybackChanged;
        base.OnDataContextChanged(e);
        _playback = DataContext as PlaybackViewModel;
        if (_playback is not null) _playback.PropertyChanged += OnPlaybackChanged;
        UpdateSurfaceOrder();
    }

    private void OnPlaybackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.IsPrimaryPlayerActive)) UpdateSurfaceOrder();
    }

    private void OnVideoAttached(object? sender, VisualTreeAttachmentEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateSurfaceOrder, DispatcherPriority.Loaded);

    private void UpdateSurfaceOrder()
    {
        if (_playback?.PrimaryPlayer is not { } primary || _playback.SecondaryPlayer is not { } secondary) return;
        PrimaryVideo.ZIndex = _playback.IsPrimaryPlayerActive ? 1 : 0;
        SecondaryVideo.ZIndex = _playback.IsPrimaryPlayerActive ? 0 : 1;
        if (OperatingSystem.IsWindows())
            NativeVideoSurfaceOrder.ShowInFront(_playback.IsPrimaryPlayerActive ? primary.Hwnd : secondary.Hwnd,
                _playback.IsPrimaryPlayerActive ? secondary.Hwnd : primary.Hwnd);
    }
}
