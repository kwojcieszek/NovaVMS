using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;

namespace NovaVMS.App.Views;

public partial class MainWindow : Window
{
    private const double DragThreshold = 6;

    private Point? _dragStart;
    private ResourceNodeViewModel? _dragNode;
    private GridLength _resourceColumnWidth = new(290);
    private bool _initialized;
    private bool _shutdownStarted;
    private bool _canClose;

    public MainWindow()
    {
        InitializeComponent();

        // Tunnelling, because TreeViewItem handles pointer presses itself for selection.
        ResourceTree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        ResourceTree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel);
        ResourceTree.AddHandler(PointerReleasedEvent, OnTreePointerReleased, RoutingStrategies.Tunnel);
        // Intercept group double-clicks before the item header toggles expansion.
        ResourceTree.AddHandler(DoubleTappedEvent, OnTreeDoubleTapped, RoutingStrategies.Tunnel);
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_canClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        base.OnClosing(e);
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        // Releasing streams and devices can take several seconds; tell the user the application is closing.
        var progress = new ShutdownWindow();
        progress.Show(this);
        try
        {
            // Await stops while surfaces and the dispatcher are still alive. Closing
            // the window first would race LibVLC callbacks and native window destruction.
            UnhandledExceptionLogger.IsShuttingDown = true;
            if (DataContext is MainWindowViewModel viewModel)
                await viewModel.CompleteShutdownAsync();
            if (Application.Current is App app) await app.DisposeServicesAsync();
        }
        catch (Exception exception)
        {
            UnhandledExceptionLogger.Log("Application shutdown", exception);
        }
        finally
        {
            progress.CanClose = true;
            progress.Close();
            _canClose = true;
            Close();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!_initialized && DataContext is MainWindowViewModel viewModel)
        {
            _initialized = true;
            viewModel.InitializeCommand.Execute(null);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ExitRequested += (_, _) => Close();
            viewModel.Live.PropertyChanged += OnLivePropertyChanged;
        }
    }

    private void OnLivePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LiveViewViewModel.IsVideoOnly) || sender is not LiveViewViewModel live)
        {
            return;
        }

        // The resource column has a fixed (user-resizable) width; collapse it and restore the width afterwards.
        var column = MainGrid.ColumnDefinitions[0];
        if (live.IsVideoOnly)
        {
            _resourceColumnWidth = column.Width;
            column.Width = new GridLength(0);
            MainGrid.ColumnDefinitions[1].Width = new GridLength(0);
        }
        else
        {
            column.Width = _resourceColumnWidth;
            MainGrid.ColumnDefinitions[1].Width = new GridLength(4);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainWindowViewModel { Live.IsVideoOnly: true } videoOnly)
        {
            videoOnly.Live.IsVideoOnly = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && DataContext is MainWindowViewModel viewModel && viewModel.Live.IsMaximized)
        {
            viewModel.Live.RestoreLayout();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private static ResourceNodeViewModel? GetNode(object? source)
    {
        return (source as StyledElement)?.DataContext as ResourceNodeViewModel;
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var settingsViewModel = viewModel.CreateSettings();
        if (settingsViewModel is null)
        {
            return;
        }

        var settingsWindow = new SettingsWindow
        {
            DataContext = settingsViewModel
        };

        await settingsWindow.ShowDialog(this);
        settingsViewModel.Dispose();
    }

    private void OnTreeTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || IsTreeButton(e.Source))
        {
            return;
        }

        if (GetNode(e.Source) is ResourceGroupNodeViewModel group)
        {
            viewModel.ShowGroupCommand.Execute(group);
        }
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsTreeButton(e.Source))
        {
            return;
        }

        // Only the chevron changes expansion when a group is clicked.
        // The single click still shows its cameras in the live view.
        if (GetNode(e.Source) is ResourceGroupNodeViewModel)
        {
            e.Handled = true;
            return;
        }

        if (DataContext is MainWindowViewModel viewModel && GetNode(e.Source) is CameraNodeViewModel)
        {
            viewModel.OpenSelectedCommand.Execute(null);
        }
    }

    private static bool IsTreeButton(object? source)
    {
        return (source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null;
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsTreeButton(e.Source))
        {
            _dragStart = null;
            _dragNode = null;
            return;
        }

        if (!e.GetCurrentPoint(ResourceTree).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragStart = e.GetPosition(ResourceTree);
        _dragNode = GetNode(e.Source);
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStart = null;
        _dragNode = null;
    }

    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not Point start || _dragNode is null || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (!e.GetCurrentPoint(ResourceTree).Properties.IsLeftButtonPressed)
        {
            _dragStart = null;
            _dragNode = null;
            return;
        }

        var delta = e.GetPosition(ResourceTree) - start;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        var cameras = viewModel.Tree.GetCameras(_dragNode);
        _dragStart = null;
        _dragNode = null;
        if (cameras.Count == 0)
        {
            return;
        }

        await DragDrop.DoDragDropAsync(e, CameraDragData.Create(cameras), DragDropEffects.Copy);
    }
}
