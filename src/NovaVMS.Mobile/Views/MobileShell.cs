using Avalonia;
using Avalonia.Controls;
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
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
        {
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
