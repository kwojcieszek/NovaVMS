using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Services;
using NovaVMS.Presentation.Localization;
using NovaVMS.Presentation.Models;
using NovaVMS.Presentation.Services;

namespace NovaVMS.App.ViewModels;

public sealed partial class ApplicationLogViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ApplicationLogEntry> _entries = [];

    [ObservableProperty]
    private ApplicationLogEntry? _selectedEntry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    internal static IReadOnlyList<ApplicationLogEntry> ParseEntries(string text) => ApplicationLogFile.ParseEntries(text);

    internal static (string Path, string Text, bool Truncated) ReadLatest(params string[] directories)
        => ApplicationLogFile.ReadLatest(directories);

    private bool CanRefresh() => !IsBusy;

    private bool CanClear() => !IsBusy && FilePath.Length > 0;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task ClearAsync()
    {
        if (!CanClear())
        {
            return;
        }

        IsBusy = true;

        try
        {
            await UnhandledExceptionLogger.ClearFileAsync(FilePath);

            SelectedEntry = null;
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

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!CanRefresh())
        {
            return;
        }

        IsBusy = true;

        try
        {
            await UnhandledExceptionLogger.FlushAsync();

            var result = await Task.Run(() => ReadLatest(UnhandledExceptionLogger.LogDirectory,
                UnhandledExceptionLogger.FallbackLogDirectory));
            var previous = SelectedEntry;
            FilePath = result.Path;
            Entries = ParseEntries(result.Text);
            SelectedEntry = Entries.FirstOrDefault(entry => entry == previous) ?? Entries.FirstOrDefault();
            Status = result.Truncated ? Localizer.Text("Log_Tail")
                : Entries.Count == 0 ? Localizer.Text("Log_Empty") : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            FilePath = string.Empty;
            Entries = [];
            SelectedEntry = null;
            Status = Localizer.Format("Log_Error", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
