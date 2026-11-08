using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Localization;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>
/// Row of the settings version history.
/// </summary>
public sealed partial class SettingsVersionRowViewModel : ViewModelBase
{
    private readonly SyncSettingsViewModel _owner;

    public SettingsVersionInfo Info { get; }

    public string Title { get; }

    public string Details { get; }

    public string StateText { get; }

    public bool HasState => StateText.Length > 0;

    public SettingsVersionRowViewModel(SyncSettingsViewModel owner, SettingsVersionInfo info, bool isLatest, bool isFromThisDevice)
    {
        var state = new List<string>();

        _owner = owner;

        Info = info;
        Title = $"#{info.Id} · {info.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", Localizer.Instance.Culture)}";
        Details = string.Join(" · ", new[] { info.MachineName, info.IpAddress, info.UserName, info.AppVersion }.Where(text => !string.IsNullOrWhiteSpace(text)));

        if (isLatest)
        {
            state.Add(Localizer.Text("Sync_StateLatest"));
        }

        if (isFromThisDevice)
        {
            state.Add(Localizer.Text("Sync_StateThisComputer"));
        }

        StateText = string.Join(", ", state);
    }

    [RelayCommand]
    private Task ApplyAsync()
    {
        return _owner.ApplyVersionAsync(Info);
    }
}
