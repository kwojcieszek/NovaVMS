using Avalonia;
using Avalonia.Controls;
using NovaVMS.App.ViewModels;

namespace NovaVMS.App.Views;

public partial class GroupsSettingsView : UserControl
{
    public GroupsSettingsView() => InitializeComponent();
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is GroupsSettingsViewModel vm) vm.Preview.SetActive(true);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is GroupsSettingsViewModel vm) vm.Preview.SetActive(false);
        base.OnDetachedFromVisualTree(e);
    }
}
