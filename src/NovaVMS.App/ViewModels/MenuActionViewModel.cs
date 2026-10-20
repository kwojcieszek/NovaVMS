using System.Windows.Input;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Dynamically generated context menu entry.
/// </summary>
public sealed class MenuActionViewModel : ViewModelBase
{
    public MenuActionViewModel(string header, ICommand command)
    {
        Header = header;
        Command = command;
    }

    public string Header { get; }

    public ICommand Command { get; }
}
