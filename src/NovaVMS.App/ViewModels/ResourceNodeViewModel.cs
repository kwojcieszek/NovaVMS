using CommunityToolkit.Mvvm.ComponentModel;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Common base of resource tree nodes. Nodes keep a reference to the tree so their
/// context menus can reach the tree commands.
/// </summary>
public abstract partial class ResourceNodeViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isExpanded;

    protected ResourceNodeViewModel(ResourceTreeViewModel owner, string name)
    {
        Owner = owner;
        Name = name;
    }

    public ResourceTreeViewModel Owner { get; }

    public string Name { get; }
}
