using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Views;

/// <summary>
/// PTZ panel: the camera moves while a direction button is held down.
/// </summary>
public partial class PtzPanel : UserControl
{
    private bool _isMoving;

    public PtzPanel()
    {
        InitializeComponent();

        // Tunnelling, because Button handles pointer events itself and marks them as handled.
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
    }

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not PtzViewModel viewModel)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var button = (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true);
        if (button?.Tag is not PtzDirection direction || !button.IsEffectivelyEnabled)
        {
            return;
        }

        _isMoving = true;
        await viewModel.StartMoveAsync(direction);
    }

    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isMoving || DataContext is not PtzViewModel viewModel)
        {
            return;
        }

        _isMoving = false;
        await viewModel.StopMoveAsync();
    }
}
