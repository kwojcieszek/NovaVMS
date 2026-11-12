using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Mobile.Models;
using NovaVMS.Mobile.Services;
using NovaVMS.Presentation.Localization;
using NovaVMS.Presentation.Services;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Settings page with the newest application log file of the phone, newest entries first.</summary>
public sealed partial class LogViewModel : PageViewModel
{
    [ObservableProperty]
    private IReadOnlyList<LogEntryItemViewModel> _entries = [];

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _filePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    public override MobileTab Tab => MobileTab.Settings;

    public override void NavigateBack(ShellViewModel shell)
    {
        shell.ShowSettings();
    }

    private bool CanRefresh() => !IsBusy;

    private bool CanClear() => !IsBusy && FilePath.Length > 0;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsBusy = true;

        try
        {
            var result = await Task.Run(() => ApplicationLogFile.ReadLatest(MobileLog.LogDirectory));

            FilePath = result.Path;
            Entries = ApplicationLogFile.ParseEntries(result.Text).Select(entry => new LogEntryItemViewModel(entry)).ToList();
            Status = result.Truncated ? Localizer.Text("Log_Tail")
                : Entries.Count == 0 ? Localizer.Text("Log_Empty") : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            FilePath = string.Empty;
            Entries = [];
            Status = Localizer.Format("Log_Error", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task ClearAsync()
    {
        IsBusy = true;

        try
        {
            await MobileLog.ClearFileAsync(FilePath);

            Entries = [];
            Status = Localizer.Text("Log_Empty");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = Localizer.Format("Log_ClearError", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
