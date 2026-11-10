using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Models;
using NovaVMS.Presentation.Localization;

namespace NovaVMS.Presentation.ViewModels;

/// <summary>
/// Row of the settings version history: the columns of the desktop table and the title and details of the phone list.
/// </summary>
public sealed partial class SettingsVersionRow : ObservableObject
{
    private readonly Func<SettingsVersionInfo, Task> _apply;

    public SettingsVersionInfo Info { get; }

    public long Id => Info.Id;

    public string CreatedAtText { get; }

    public string MachineName => Info.MachineName;

    public string IpAddress => Info.IpAddress;

    public string UserName => Info.UserName;

    public string AppVersion => Info.AppVersion;

    public string Title { get; }

    public string Details { get; }

    public string StateText { get; }

    public bool HasState => StateText.Length > 0;

    /// <param name="info">The stored version.</param>
    /// <param name="isLatest">The newest version in the database.</param>
    /// <param name="isFromThisInstallation">Sent from this computer or phone.</param>
    /// <param name="apply">Applies the version after asking the user.</param>
    public SettingsVersionRow(SettingsVersionInfo info, bool isLatest, bool isFromThisInstallation, Func<SettingsVersionInfo, Task> apply)
    {
        var state = new List<string>();

        _apply = apply;

        Info = info;
        CreatedAtText = info.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", Localizer.Instance.Culture);
        Title = $"#{info.Id} · {CreatedAtText}";
        Details = string.Join(" · ", new[] { info.MachineName, info.IpAddress, info.UserName, info.AppVersion }.Where(text => !string.IsNullOrWhiteSpace(text)));

        if (isLatest)
        {
            state.Add(Localizer.Text("Sync_StateLatest"));
        }

        if (isFromThisInstallation)
        {
            state.Add(Localizer.Text("Sync_StateThisComputer"));
        }

        StateText = string.Join(", ", state);
    }

    [RelayCommand]
    private Task ApplyAsync()
    {
        return _apply(Info);
    }
}
