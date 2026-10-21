using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using NovaVMS.App.Models;
using NovaVMS.App.ViewModels;

namespace NovaVMS.App.Views;

public partial class GroupsSettingsView : UserControl
{
    // The preview runs only while the page is shown. The data context can arrive after the page is attached.
    private GroupsSettingsViewModel? _viewModel;

    public GroupsSettingsView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _viewModel?.Preview.SetActive(false);
        _viewModel = DataContext as GroupsSettingsViewModel;
        _viewModel?.Preview.SetActive(this.IsAttachedToVisualTree());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewModel?.Preview.SetActive(true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _viewModel?.Preview.SetActive(false);
        base.OnDetachedFromVisualTree(e);
    }

    // A click previews the camera even when it is already selected, e.g. to retry after an error.
    private void OnCameraTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is StyledElement { DataContext: CameraDescriptor camera })
        {
            _viewModel?.Preview.Select(camera);
        }
    }
}
