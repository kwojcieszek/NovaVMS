using NovaVMS.App.Localization;
using NovaVMS.Core.Models;

namespace NovaVMS.App.ViewModels;

/// <summary>
/// Row of the settings version history.
/// </summary>
public sealed class SettingsVersionRowViewModel : ViewModelBase
{
    public SettingsVersionRowViewModel(SettingsVersionInfo info, bool isLatest, bool isFromThisComputer)
    {
        Info = info;
        CreatedAtText = info.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", Localizer.Instance.Culture);

        var state = new List<string>();
        if (isLatest)
        {
            state.Add(Localizer.Text("Sync_StateLatest"));
        }

        if (isFromThisComputer)
        {
            state.Add(Localizer.Text("Sync_StateThisComputer"));
        }

        StateText = string.Join(", ", state);
    }

    public SettingsVersionInfo Info { get; }

    public long Id => Info.Id;

    public string CreatedAtText { get; }

    public string MachineName => Info.MachineName;

    public string IpAddress => Info.IpAddress;

    public string UserName => Info.UserName;

    public string AppVersion => Info.AppVersion;

    public string StateText { get; }
}
