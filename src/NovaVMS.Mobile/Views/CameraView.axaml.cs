using Avalonia;
using Avalonia.Controls;
using NovaVMS.Mobile.ViewModels;

namespace NovaVMS.Mobile.Views;

public partial class CameraView : UserControl
{
    private const double MinimumVideoHeight = 100;
    private const double MaximumVideoHeight = 420;
    private const double MaximumHeightShare = 0.4;

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

        ResizeVideo();
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
