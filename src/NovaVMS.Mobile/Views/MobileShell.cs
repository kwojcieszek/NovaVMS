using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using NovaVMS.Mobile.Services;
using NovaVMS.Mobile.ViewModels;

namespace NovaVMS.Mobile.Views;

/// <summary>Root page hosted by the Android and iOS heads; forwards lifecycle events to <see cref="ShellViewModel"/>.</summary>
public sealed class MobileShell : ContentPage
{
    private readonly ShellViewModel _viewModel;
    private TopLevel? _topLevel;

    public MobileShell(MobilePlatform platform)
    {
        _viewModel = new ShellViewModel(platform);

        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        // The shell applies the safe area itself, so the header and navigation colors reach the screen edges.
        AutomaticallyApplySafeAreaPadding = false;
        TopLevel.SetAutoSafeAreaPadding(this, false);
        Content = new ShellView
        {
            DataContext = _viewModel
        };

        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
    }

    public async Task DisposeAsync()
    {
        await _viewModel.DisposeAsync();
    }

    public Task SuspendAsync()
    {
        return _viewModel.SuspendAsync();
    }

    public void Resume()
    {
        _viewModel.Resume();
    }

    public Task<bool> GoBackAsync()
    {
        return _viewModel.GoBackAsync();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _topLevel = TopLevel.GetTopLevel(this);

        if (_topLevel is not null)
        {
            _topLevel.BackRequested += OnBackRequested;
            _viewModel.Files = new StorageFileService(_topLevel);

            if (_topLevel.InsetsManager is { } insets)
            {
                insets.SafeAreaChanged += OnSafeAreaChanged;
                _topLevel.ScalingChanged += OnScalingChanged;

                ApplySafeArea(insets.SafeAreaPadding);
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
        {
            if (_topLevel.InsetsManager is { } insets)
            {
                insets.SafeAreaChanged -= OnSafeAreaChanged;
            }

            _topLevel.ScalingChanged -= OnScalingChanged;
            _topLevel.BackRequested -= OnBackRequested;
            _topLevel = null;
            _viewModel.Files = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    // The system back button (and gesture) returns to the previous page; on the camera list it leaves the application.
    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanGoBack)
        {
            return;
        }

        e.Handled = true;

        _ = _viewModel.GoBackAsync();
    }

    // Rotating the phone moves the notch and the home indicator.
    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e)
    {
        ApplySafeArea(e.SafeAreaPadding);
    }

    // The first safe area arrives before the screen scaling is known and is then given in pixels.
    private void OnScalingChanged(object? sender, EventArgs e)
    {
        if (_topLevel?.InsetsManager is { } insets)
        {
            ApplySafeArea(insets.SafeAreaPadding);
        }
    }

    // The window is drawn under the status bar, the notch or Dynamic Island and the home indicator.
    private void ApplySafeArea(Thickness padding)
    {
        _viewModel.SafeArea = padding;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = await _viewModel.GoBackAsync();
    }
}
